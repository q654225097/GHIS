using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inpatient.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

[ApiController]
[Route("api/v4/ipd")]
public class InpatientController : ControllerBase
{
	public sealed record AdmitPatientRequestDto(string PatientId, string CampusId, string AdmitDeptId, string AdmitWardId, string BedCode, bool IsBorrowedBed, decimal PrepaymentAmount = 2000.00m);

	public sealed record TransferEncounterRequestDto(string TransferType, string ToDeptId, string ToWardId, string ToBedCode, string? OperatorId = null);

	public sealed record AddPrepaymentRequestDto(decimal Amount, string? OperatorId = null);

	private readonly IRepository<IpdBed> _bedRepo;

	private readonly IRepository<IpdEncounter> _encounterRepo;

	private readonly IRepository<IpdTransferLog> _transferRepo;

	private readonly DischargeChecklistService _dischargeService;

	private readonly IDbConnectionFactory _connFactory;

	private readonly IDepartmentReadOnlyContract? _deptContract;

	private readonly IAuditTrailContract? _auditTrail;

	private readonly IUserSession? _userSession;

	public InpatientController(IRepository<IpdBed> bedRepo, IRepository<IpdEncounter> encounterRepo, IRepository<IpdTransferLog> transferRepo, DischargeChecklistService dischargeService, IDbConnectionFactory? connFactory = null, IDepartmentReadOnlyContract? deptContract = null, IAuditTrailContract? auditTrail = null, IUserSession? userSession = null)
	{
		_bedRepo = bedRepo;
		_encounterRepo = encounterRepo;
		_transferRepo = transferRepo;
		_dischargeService = dischargeService;
		_connFactory = connFactory ?? new SqliteConnectionFactory();
		_deptContract = deptContract;
		_auditTrail = auditTrail;
		_userSession = userSession;
	}

	[HttpGet("beds")]
	public async Task<ApiResponse<IReadOnlyList<IpdBed>>> GetBeds([FromQuery] string? wardId = null)
	{
		return ApiResponse<IReadOnlyList<IpdBed>>.Success(await _bedRepo.QueryAsync("SELECT * FROM ipd_beds\nWHERE (@WardId IS NULL OR ward_id = @WardId)\nORDER BY ward_id ASC, room_code ASC, bed_code ASC", new
		{
			WardId = wardId
		}));
	}

	[HttpGet("encounters")]
	public async Task<ApiResponse<IReadOnlyList<IpdEncounter>>> GetEncounters([FromQuery] string? admitDeptId = null, [FromQuery] string? admitWardId = null, [FromQuery] int? status = null)
	{
		return ApiResponse<IReadOnlyList<IpdEncounter>>.Success(await _encounterRepo.QueryAsync("SELECT * FROM ipd_encounters\nWHERE (@DeptId IS NULL OR admit_dept_id = @DeptId)\n  AND (@WardId IS NULL OR admit_ward_id = @WardId)\n  AND (@Status IS NULL OR status = @Status)\nORDER BY created_at_ms DESC", new
		{
			DeptId = admitDeptId,
			WardId = admitWardId,
			Status = status
		}));
	}

	[HttpPost("admit")]
	public async Task<ApiResponse<IpdEncounter>> AdmitPatient([FromBody] AdmitPatientRequestDto req)
	{
		var token = Guid.NewGuid().ToString("N");
		var encounter = new IpdEncounter
		{
			AdmissionNo = "IPD" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
			PatientId = req.PatientId,
			CampusId = req.CampusId,
			AdmitDeptId = req.AdmitDeptId,
			AdmitWardId = req.AdmitWardId,
			IsBorrowedBed = (short)(req.IsBorrowedBed ? 1 : 0),
			BedCode = req.BedCode,
			ActiveBedToken = token,
			PrepaymentBalance = req.PrepaymentAmount,
			Status = 1 // Admitted
		};
		await _encounterRepo.InsertAsync(encounter);

		// Update bed status
		var beds = await _bedRepo.QueryAsync(
			"SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode",
			new { WardId = req.AdmitWardId, BedCode = req.BedCode });
		if (beds.Count > 0)
		{
			var bed = beds[0];
			bed.BedStatus = 1; // Occupied
			bed.CurrentEncounterId = encounter.Id;
			await _bedRepo.UpdateAsync(bed);
		}

		return ApiResponse<IpdEncounter>.Success(encounter, req.IsBorrowedBed ? "跨病区借床入院登记成功" : "常规入院分床成功");
	}

	[HttpPost("encounters/{encounterId}/transfer")]
	public async Task<ApiResponse<IpdTransferLog>> TransferEncounter(string encounterId, [FromBody] TransferEncounterRequestDto req)
	{
		if (!HasAnyRole("IPD_RESIDENT", "IPD_ATTENDING", "WARD_NURSE"))
		{
			return ApiResponse<IpdTransferLog>.Fail(403, "仅住院医师、主治医师或病区护士可以办理转科/转病区/换床");
		}
		IpdEncounter enc = await _encounterRepo.GetByIdAsync(encounterId);
		if (enc == null)
		{
			return ApiResponse<IpdTransferLog>.Fail(404, "未找到住院记录");
		}
		if (enc.Status != 1 && enc.Status != 4)
		{
			return ApiResponse<IpdTransferLog>.Fail(400, "仅在院状态患者允许办理转床/转科/转病区");
		}
		IpdTransferLog log = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			long nowMs = SystemTime.UtcNowMs;
			IReadOnlyList<IpdEncounter> lockedEncounters = await _encounterRepo.QueryAsync(
				"SELECT * FROM ipd_encounters WHERE id = @Id AND status IN (1, 4)",
				new { Id = encounterId }, ct);
			if (lockedEncounters.Count == 0)
			{
				throw new InvalidOperationException("住院记录状态已变化，仅在院患者允许办理转床/转科/转病区");
			}
			enc = lockedEncounters[0];
			string relationType = await ResolveAndValidateTransferScopeAsync(
				enc, req.ToDeptId, req.ToWardId, ct);
			bool isBorrowedBed = !relationType.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase);
			IReadOnlyList<IpdBed> targetBeds = await _bedRepo.QueryAsync(
				"SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode",
				new { WardId = req.ToWardId, BedCode = req.ToBedCode },
				ct);
			if (targetBeds.Count == 0)
			{
				throw new InvalidOperationException($"目标病区 [{req.ToWardId}] 床位 [{req.ToBedCode}] 不存在");
			}
			if (targetBeds[0].BedStatus == 1 && targetBeds[0].CurrentEncounterId != enc.Id)
			{
				throw new InvalidOperationException($"目标病区 [{req.ToWardId}] 床位 [{req.ToBedCode}] 已被占用，无法转入");
			}
			if (isBorrowedBed && enc.IsBorrowedBed == 0 && IsCrossWardMove(enc, req.ToDeptId, req.ToWardId))
			{
				await EnsureBorrowBedCapacityAsync(req.ToDeptId, req.ToWardId, null, ct);
			}
			int updatedEncounter = await _encounterRepo.ExecuteAsync(
				@"UPDATE ipd_encounters
SET admit_dept_id = @ToDeptId, admit_ward_id = @ToWardId, bed_code = @ToBedCode,
    is_borrowed_bed = @IsBorrowedBed, active_bed_token = @Token,
    updated_at_ms = @Now, updated_by = @OperatorId, version_no = version_no + 1
WHERE id = @Id AND status IN (1, 4)
  AND admit_dept_id = @FromDeptId AND admit_ward_id = @FromWardId AND bed_code = @FromBedCode",
				new
				{
					Id = enc.Id,
					ToDeptId = req.ToDeptId.Trim(),
					ToWardId = req.ToWardId.Trim(),
					ToBedCode = req.ToBedCode.Trim(),
					IsBorrowedBed = isBorrowedBed ? 1 : 0,
					Token = Guid.NewGuid().ToString("N"),
					Now = nowMs,
					OperatorId = req.OperatorId,
					FromDeptId = enc.AdmitDeptId?.Trim() ?? string.Empty,
					FromWardId = enc.AdmitWardId?.Trim() ?? string.Empty,
					FromBedCode = enc.BedCode ?? string.Empty
				}, ct);
			if (updatedEncounter != 1)
			{
				throw new InvalidOperationException("住院记录床位已在其他终端变更，请刷新后重试");
			}
			IpdTransferLog transferLog = new IpdTransferLog
			{
				EncounterId = enc.Id,
				TransferType = (string.IsNullOrWhiteSpace(req.TransferType) ? "DEPT_TRANSFER" : req.TransferType),
				FromDeptId = enc.AdmitDeptId,
				FromWardId = enc.AdmitWardId,
				FromBedCode = enc.BedCode,
				ToDeptId = req.ToDeptId,
				ToWardId = req.ToWardId,
				ToBedCode = req.ToBedCode,
				HandoverOutAtMs = nowMs,
				HandoverInAtMs = nowMs,
				CreatedBy = req.OperatorId,
				UpdatedBy = req.OperatorId
			};
			await _transferRepo.InsertAsync(transferLog, ct);
			if (!string.IsNullOrWhiteSpace(enc.BedCode))
			{
				IReadOnlyList<IpdBed> oldBeds = await _bedRepo.QueryAsync("SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode", new
				{
					WardId = enc.AdmitWardId,
					BedCode = enc.BedCode
				}, ct);
				if (oldBeds.Count > 0)
				{
					IpdBed oldBed = oldBeds[0];
					oldBed.BedStatus = 0;
					oldBed.CurrentEncounterId = null;
					await _bedRepo.UpdateAsync(oldBed, ct);
				}
			}
			if (targetBeds.Count > 0)
			{
				IpdBed newBed = targetBeds[0];
				newBed.BedStatus = 1;
				newBed.CurrentEncounterId = enc.Id;
				await _bedRepo.UpdateAsync(newBed, ct);
			}
			enc = (await _encounterRepo.QueryAsync(
				"SELECT * FROM ipd_encounters WHERE id = @Id", new { Id = enc.Id }, ct))[0];
			return transferLog;
		});
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("IPD_TRANSFER", "IpdEncounter", enc.Id, $"住院患者转科/换床：[{log.FromDeptId}/{log.FromWardId}/{log.FromBedCode}] -> [{log.ToDeptId}/{log.ToWardId}/{log.ToBedCode}]", req.OperatorId);
		}
		return ApiResponse<IpdTransferLog>.Success(log, "患者转科/转病区/换床交接完成，床位与权责已实时切换");
	}

	[HttpPost("encounters/{encounterId}/prepayment")]
	public async Task<ApiResponse<IpdEncounter>> AddPrepayment(string encounterId, [FromBody] AddPrepaymentRequestDto req)
	{
		await Task.CompletedTask;
		return ApiResponse<IpdEncounter>.Fail(409,
			"旧版押金接口已停用，请通过 /api/v4/ipd/encounters/{encounterId}/prepayments 办理并生成收据、缴费流水与审计记录");
	}

	[HttpGet("encounters/{encounterId}/access-check")]
	public async Task<IActionResult> CheckClinicalAccess(string encounterId, [FromQuery] string deptCategory, [FromQuery] string deptOrWardId)
	{
		IpdEncounter enc = await _encounterRepo.GetByIdAsync(encounterId);
		if (enc == null)
		{
			return NotFound(new
			{
				Code = 404,
				Message = "未找到住院记录"
			});
		}
		if (deptCategory.Equals("IPD", StringComparison.OrdinalIgnoreCase))
		{
			if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
			{
				return Unauthorized(new
				{
					Code = 401,
					Message = "请先登录后访问患者医疗档案"
				});
			}
			if (!_userSession.IsAdmin && (_userSession.ActiveDeptCategory != DepartmentCategory.IPD
				|| !_userSession.ActiveDeptId.Equals(deptOrWardId, StringComparison.OrdinalIgnoreCase)))
			{
				return StatusCode(403, new
				{
					Code = 403,
					Message = "无权限：执业科室与登录会话声明不一致"
				});
			}
			if (!enc.AdmitDeptId.Equals(deptOrWardId, StringComparison.OrdinalIgnoreCase))
			{
				return StatusCode(403, new
				{
					Code = 403,
					Message = "无权限：非管床医疗科室医生禁止访问借床患者医疗档案"
				});
			}
		}
		else if (deptCategory.Equals("WARD", StringComparison.OrdinalIgnoreCase))
		{
			if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
			{
				return Unauthorized(new
				{
					Code = 401,
					Message = "请先登录后访问患者医疗档案"
				});
			}
			bool sessionWardMatches = _userSession.IsAdmin || (_userSession.ActiveDeptCategory == DepartmentCategory.WARD
				&& (_userSession.ActiveDeptId.Equals(deptOrWardId, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(_userSession.ActiveWardId, deptOrWardId, StringComparison.OrdinalIgnoreCase)));
			if (!sessionWardMatches
				|| !enc.AdmitWardId.Equals(deptOrWardId, StringComparison.OrdinalIgnoreCase))
			{
				return StatusCode(403, new
				{
					Code = 403,
					Message = "无权限：非所在物理护理病区护士禁止执行床旁操作"
				});
			}
		}
		else
		{
			return BadRequest(new
			{
				Code = 400,
				Message = "deptCategory 仅支持 IPD 或 WARD"
			});
		}
		return Ok(new
		{
			Code = 200,
			Message = "科室执业范围鉴权通过",
			Data = enc
		});
	}

	[HttpPost("encounters/{encounterId}/discharge-check")]
	public async Task<ApiResponse<DischargeCheckResult>> VerifyDischargeChecklist(string encounterId)
	{
		DischargeCheckResult result = await _dischargeService.PerformSixItemCheckAsync(encounterId);
		if (!result.AllPassed)
		{
			return ApiResponse<DischargeCheckResult>.Fail(400, "出院关床六项核查未通过，存在未闭环业务", result);
		}
		return ApiResponse<DischargeCheckResult>.Success(result, "出院关床六项核查全部通过，允许释放床位并转结算");
	}

	[HttpPost("encounters/{encounterId}/close-ward-bed")]
	public async Task<ApiResponse<IpdEncounter>> CloseWardBed(string encounterId)
	{
		IpdEncounter? enc = await _encounterRepo.GetByIdAsync(encounterId);
		if (enc == null)
		{
			return ApiResponse<IpdEncounter>.Fail(404, "住院记录不存在");
		}
		return ApiResponse<IpdEncounter>.Fail(409,
			"关床必须通过收费处出院结算完成，不能直接释放床位或跳过财务结算",
			enc);
	}

	[HttpPost("encounters/{encounterId}/recall")]
	public async Task<ApiResponse<IpdEncounter>> RecallDischargedEncounter(string encounterId, [FromQuery] string? targetBedCode = null)
	{
		if (!HasAnyRole("IPD_RESIDENT", "IPD_ATTENDING", "WARD_NURSE", "ADMIN"))
		{
			return ApiResponse<IpdEncounter>.Fail(403, "仅住院医师、主治医师、责任护士或系统管理员可以办理出院召回");
		}
		IpdEncounter enc = await _encounterRepo.GetByIdAsync(encounterId);
		if (enc == null)
		{
			return ApiResponse<IpdEncounter>.Fail(404, "住院记录不存在");
		}
		if (enc.Status != 2 && enc.Status != 3)
		{
			return ApiResponse<IpdEncounter>.Fail(409, "仅出院待结算或已出院状态的患者可以办理出院召回");
		}
		string bedCodeToOccupy = ((!string.IsNullOrWhiteSpace(targetBedCode)) ? targetBedCode.Trim() : enc.BedCode);
		IpdBed bedToOccupy = null;
		if (!string.IsNullOrWhiteSpace(bedCodeToOccupy))
		{
			IReadOnlyList<IpdBed> beds = await _bedRepo.QueryAsync("SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode", new
			{
				WardId = enc.AdmitWardId,
				BedCode = bedCodeToOccupy
			});
			if (beds.Count > 0)
			{
				if (beds[0].BedStatus == 1 && beds[0].CurrentEncounterId != enc.Id && !string.IsNullOrWhiteSpace(beds[0].CurrentEncounterId))
				{
					return ApiResponse<IpdEncounter>.Fail(409, "原床位 [" + bedCodeToOccupy + "] 已被其他在院患者占用，出院召回时请指定其他空闲床位（targetBedCode）！");
				}
				bedToOccupy = beds[0];
			}
		}
		await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			enc.Status = 1;
			enc.TriageStatus = 1;
			enc.BedCode = bedCodeToOccupy;
			enc.ActiveBedToken = Guid.NewGuid().ToString("N");
			enc.UpdatedBy = _userSession?.UserId;
			await _encounterRepo.UpdateAsync(enc, ct);
			await _encounterRepo.ExecuteAsync(@"
UPDATE ipd_discharge_requests
SET status = CASE WHEN status = 'SETTLED' THEN 'RECALLED' ELSE 'CANCELLED' END,
    updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1
WHERE encounter_id = @EncounterId AND status IN ('PENDING', 'SETTLED')", new
			{
				Now = SystemTime.UtcNowMs,
				UserId = _userSession?.UserId,
				EncounterId = enc.Id
			}, ct);
			if (bedToOccupy != null)
			{
				bedToOccupy.BedStatus = 1;
				bedToOccupy.CurrentEncounterId = enc.Id;
				await _bedRepo.UpdateAsync(bedToOccupy, ct);
			}
		});
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("IPD_RECALL", "IpdEncounter", enc.Id,
				"住院患者 [" + enc.AdmissionNo + "] 出院召回成功，恢复床位：" + enc.BedCode
				+ "，关联出院申请已退回，可重新提交出院申请", _userSession?.UserId);
		}
		return ApiResponse<IpdEncounter>.Success(enc, "住院出院召回（Recall）成功，原住院号、床位与病历账务无损恢复在院状态");
	}

	private bool HasAnyRole(params string[] roles)
	{
		if (!HasSession) return false;
		if (IsAdmin()) return true;
		return RoleCode.HasAnyRole(_userSession.Roles, roles);
	}

	private bool IsAdmin() =>
		_userSession != null
		&& (RoleCode.IsAdmin(_userSession.RoleCode) || RoleCode.HasRole(_userSession.Roles, RoleCode.Admin));

	private bool HasSession => _userSession != null && !string.IsNullOrWhiteSpace(_userSession.UserId);

	private async Task<string> ResolveAndValidateTransferScopeAsync(
		IpdEncounter enc, string toDeptId, string toWardId, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(toDeptId) || string.IsNullOrWhiteSpace(toWardId))
		{
			throw new InvalidOperationException("请选择目标科室与目标病区");
		}
		if (HasAnyRole("WARD_NURSE") && !IsAdmin())
		{
			bool sameWard = enc.AdmitDeptId.Equals(toDeptId, StringComparison.OrdinalIgnoreCase)
				&& enc.AdmitWardId.Equals(toWardId, StringComparison.OrdinalIgnoreCase);
			bool samePhysicalWard = enc.AdmitWardId.Equals(toWardId, StringComparison.OrdinalIgnoreCase);
			if (!sameWard && !samePhysicalWard)
			{
				throw new UnauthorizedAccessException("病区护士仅能在本人在岗病区办理换床或借床，跨病区转移须由住院医师办理");
			}
			if (!HasSession || (!_userSession!.ActiveDeptId.Equals(toWardId, StringComparison.OrdinalIgnoreCase)
				&& !_userSession.ActiveWardId.Equals(toWardId, StringComparison.OrdinalIgnoreCase)))
			{
				throw new UnauthorizedAccessException("病区护士只能操作本人当前登录病区的床位");
			}
		}
		if (HasAnyRole("IPD_RESIDENT", "IPD_ATTENDING") && !HasAnyRole("WARD_NURSE") && !HasSession)
		{
			throw new UnauthorizedAccessException("请先登录后再办理转科");
		}
		if (HasAnyRole("IPD_RESIDENT", "IPD_ATTENDING") && !HasAnyRole("WARD_NURSE")
			&& !IsAdmin()
			&& !_userSession.ActiveDeptId.Equals(enc.AdmitDeptId, StringComparison.OrdinalIgnoreCase))
		{
			throw new UnauthorizedAccessException("住院医师只能办理本人当前执业科室患者的转科");
		}

		if (_deptContract != null
			&& !await _deptContract.IsDeptWardRelationValidAsync(toDeptId, toWardId))
		{
			throw new InvalidOperationException(
				$"目标科室 [{toDeptId}] 与目标病区 [{toWardId}] 不存在有效收治或借床关联");
		}
		string? relationType = await QueryRelationTypeAsync(toDeptId, toWardId, ct);
		if (relationType == null)
		{
			throw new InvalidOperationException("目标科室与病区没有有效映射关系（已停用或不存在）");
		}
		string normalized = relationType.Trim().ToUpperInvariant();
		if (normalized is not ("PRIMARY" or "BORROW_ALLOWED"))
		{
			throw new InvalidOperationException($"不支持的目标科室与病区关联类型 [{relationType}]");
		}
		return normalized;
	}

	private async Task<string?> QueryRelationTypeAsync(
		string deptId, string wardId, CancellationToken ct)
	{
		using var conn = _connFactory.CreateConnection();
		return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
			"SELECT relation_type FROM sys_dept_ward_relations WHERE ipd_dept_id = @DeptId AND ward_dept_id = @WardId AND is_active = 1 LIMIT 1",
			new { DeptId = deptId, WardId = wardId }, cancellationToken: ct));
	}

	private async Task EnsureBorrowBedCapacityAsync(
		string deptId, string wardId, string? excludedEncounterId, CancellationToken ct)
	{
		using var conn = _connFactory.CreateConnection();
		long? limit = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
			"SELECT max_borrow_beds FROM sys_dept_ward_relations WHERE ipd_dept_id = @DeptId AND ward_dept_id = @WardId AND is_active = 1 AND relation_type <> 'PRIMARY' LIMIT 1",
			new { DeptId = deptId, WardId = wardId }, cancellationToken: ct));
		if (limit == null)
		{
			throw new InvalidOperationException("目标科室与病区没有允许借床的映射关系");
		}
		long used = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
			"SELECT count(*) FROM ipd_encounters WHERE admit_dept_id = @DeptId AND admit_ward_id = @WardId AND is_borrowed_bed = 1 AND status IN (1, 4) AND id <> COALESCE(@ExcludedId, '')",
			new { DeptId = deptId, WardId = wardId, ExcludedId = excludedEncounterId }, cancellationToken: ct));
		if (used >= limit.Value)
		{
			throw new InvalidOperationException(
				$"跨科室借床已达上限（{used}/{limit.Value} 床），请选择本科室主病区或其他病区");
		}
	}

	private static bool IsCrossWardMove(
		IpdEncounter enc, string toDeptId, string toWardId)
	{
		return !enc.AdmitDeptId.Equals(toDeptId, StringComparison.OrdinalIgnoreCase)
			|| !enc.AdmitWardId.Equals(toWardId, StringComparison.OrdinalIgnoreCase)
			|| enc.IsBorrowedBed == 0;
	}
}
