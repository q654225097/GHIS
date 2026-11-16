using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.MedTech.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.MedTech.Controllers;

[ApiController]
[Route("api/v4/medtech")]
public class MedTechController : ControllerBase
{
	public sealed record CreateRequestDto(string SourceType, string SourceVisitOrOrderId, string PatientId, string OrderDeptId, string? OrderWardId, string ServiceItemCode, decimal FeeAmount);

	public sealed record PublishReportRequestDto(string ReportSummary, bool IsCriticalValue = false, string? ReporterId = null);

	public sealed record CriticalValueAckRequestDto(string? DoctorId, string? NurseId, string? ClinicalActionNote = null);

	public sealed record SurgeryCheckDto(string ScheduleId, bool SignInOk, bool TimeOutOk, bool SignOutOk, bool InstrumentCountBalanced);

	public sealed record DualCheckBloodDispenseDto(string RequestId, string? DispenserId, string DualCheckDispenserId, string BagBarcode);

	private readonly IRepository<MtcServiceRequest> _requestRepo;

	private readonly IRepository<MtcSurgerySchedule> _surgeryRepo;

	private readonly IRepository<MtcBloodRequest> _bloodRepo;

	private readonly IMedTechRoutingContract _routingService;

	private readonly IPriceTableContract? _priceTable;

	private readonly IAuditTrailContract? _auditTrail;

	private readonly IUserSession? _session;

	private readonly IPatientReadOnlyContract? _patientContract;

	private readonly IDepartmentReadOnlyContract? _departmentContract;

	private readonly IDbConnectionFactory _connFactory;

	public MedTechController(
		IRepository<MtcServiceRequest> requestRepo,
		IRepository<MtcSurgerySchedule> surgeryRepo,
		IRepository<MtcBloodRequest> bloodRepo,
		IMedTechRoutingContract routingService,
		IPriceTableContract? priceTable = null,
		IAuditTrailContract? auditTrail = null,
		IUserSession? session = null,
		IPatientReadOnlyContract? patientContract = null,
		IDepartmentReadOnlyContract? departmentContract = null,
		IDbConnectionFactory? connFactory = null)
	{
		_requestRepo = requestRepo;
		_surgeryRepo = surgeryRepo;
		_bloodRepo = bloodRepo;
		_routingService = routingService;
		_priceTable = priceTable;
		_auditTrail = auditTrail;
		_session = session;
		_patientContract = patientContract;
		_departmentContract = departmentContract;
		_connFactory = connFactory ?? new SqliteConnectionFactory();
	}

	[HttpGet("requests")]
	public async Task<ApiResponse<IReadOnlyList<MtcServiceRequest>>> GetRequests([FromQuery] string? patientId = null, [FromQuery] string? execMedTechDeptId = null, [FromQuery] bool? criticalOnly = null)
	{
		return ApiResponse<IReadOnlyList<MtcServiceRequest>>.Success(await _requestRepo.QueryAsync("SELECT * FROM mtc_service_requests\nWHERE (@PatientId IS NULL OR patient_id = @PatientId)\n  AND (@ExecDeptId IS NULL OR exec_med_tech_dept_id = @ExecDeptId)\n  AND (@CritOnly IS NULL OR is_critical_value = @CritOnly)\nORDER BY created_at_ms DESC", new
		{
			PatientId = patientId,
			ExecDeptId = execMedTechDeptId,
			CritOnly = (criticalOnly.HasValue ? new short?((short)(criticalOnly.Value ? 1 : 0)) : ((short?)null))
		}));
	}

	[HttpPost("requests")]
	public async Task<ApiResponse<MtcServiceRequest>> CreateRequest([FromBody] CreateRequestDto req)
	{
		var authError = RequirePrescribingRole();
		if (authError != null)
			return ApiResponse<MtcServiceRequest>.Fail(403, authError);
		if (req == null || string.IsNullOrWhiteSpace(req.ServiceItemCode))
			return ApiResponse<MtcServiceRequest>.Fail(400, "医技项目编码不能为空");

		var sourceType = (req.SourceType ?? "").Trim().ToUpperInvariant();
		if (sourceType != "OPD" && sourceType != "IPD")
			return ApiResponse<MtcServiceRequest>.Fail(400, "医技申请来源仅支持门诊 (OPD) 或住院 (IPD)");
		if (_priceTable == null)
			return ApiResponse<MtcServiceRequest>.Fail(503, "物价目录服务不可用，禁止开立医技申请");
		if (_patientContract == null)
			return ApiResponse<MtcServiceRequest>.Fail(503, "患者档案服务不可用，禁止开立医技申请");
		if (_departmentContract == null)
			return ApiResponse<MtcServiceRequest>.Fail(503, "科室主数据服务不可用，禁止开立医技申请");

		long nowMs = SystemTime.UtcNowMs;
		PriceItemSnapshotDto? priceSnapshot = await _priceTable.GetPriceSnapshotAsync(req.ServiceItemCode.Trim(), nowMs);
		if (priceSnapshot == null || priceSnapshot.UnitPrice < 0m)
			return ApiResponse<MtcServiceRequest>.Fail(400, $"医技项目 [{req.ServiceItemCode}] 不存在或未配置物价");

		var patient = await _patientContract.GetPatientSummaryAsync(req.PatientId);
		if (patient == null || !patient.IsActive)
			return ApiResponse<MtcServiceRequest>.Fail(400, "患者不存在或已停用，禁止开立医技申请");

		string sourceId = req.SourceVisitOrOrderId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(sourceId))
			return ApiResponse<MtcServiceRequest>.Fail(400, "必须指定有效的门诊就诊号或住院就诊记录");
		string orderDeptId = req.OrderDeptId?.Trim() ?? string.Empty;
		string? orderWardId = string.IsNullOrWhiteSpace(req.OrderWardId) ? null : req.OrderWardId.Trim();
		string? sourceVisitId;
		string? sourceOrderId;
		using (var conn = _connFactory.CreateConnection())
		{
			if (sourceType == "OPD")
			{
				var visit = await conn.QuerySingleOrDefaultAsync<SourceVisitRow>(
					"SELECT id, patient_id, opd_dept_id, doctor_id, status FROM opd_visits WHERE id = @Id OR visit_no = @Id LIMIT 1",
					new { Id = sourceId });
				if (visit == null)
					return ApiResponse<MtcServiceRequest>.Fail(400, "门诊就诊记录不存在");
				if (visit.Status is not (1 or 2))
					return ApiResponse<MtcServiceRequest>.Fail(409, "门诊就诊已结束或已取消，不能继续开立医技申请");
				if (!string.Equals(visit.PatientId, patient.Id, StringComparison.OrdinalIgnoreCase))
					return ApiResponse<MtcServiceRequest>.Fail(400, "医技申请患者与门诊就诊记录不一致");
				if (!string.Equals(visit.DoctorId, _session!.UserId, StringComparison.OrdinalIgnoreCase) && !IsAdmin())
					return ApiResponse<MtcServiceRequest>.Fail(403, "只能为本人接诊的门诊患者开立医技申请");
				if (string.IsNullOrWhiteSpace(orderDeptId))
					orderDeptId = visit.OpdDeptId;
				if (!string.Equals(orderDeptId, visit.OpdDeptId, StringComparison.OrdinalIgnoreCase))
					return ApiResponse<MtcServiceRequest>.Fail(400, "申请科室必须与门诊就诊科室一致");
				if (orderWardId != null)
					return ApiResponse<MtcServiceRequest>.Fail(400, "门诊医技申请不能指定住院病区");
				sourceVisitId = visit.Id;
				sourceOrderId = null;
			}
			else
			{
				var encounter = await conn.QuerySingleOrDefaultAsync<SourceEncounterRow>(
					"SELECT id, patient_id, admit_dept_id, admit_ward_id, attending_doctor_id, status FROM ipd_encounters WHERE id = @Id OR admission_no = @Id LIMIT 1",
					new { Id = sourceId });
				if (encounter == null)
					return ApiResponse<MtcServiceRequest>.Fail(400, "住院就诊记录不存在");
				if (encounter.Status != 1)
					return ApiResponse<MtcServiceRequest>.Fail(409, "住院患者不在院（尚未接诊或已出院/召回），不能开立医技申请");
				if (!string.Equals(encounter.PatientId, patient.Id, StringComparison.OrdinalIgnoreCase))
					return ApiResponse<MtcServiceRequest>.Fail(400, "医技申请患者与住院记录不一致");
				if (!IsAdmin()
					&& !string.Equals(encounter.AttendingDoctorId, _session!.UserId, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(_session.ActiveDeptId, encounter.AdmitDeptId, StringComparison.OrdinalIgnoreCase))
					return ApiResponse<MtcServiceRequest>.Fail(403, "当前医生不属于该住院患者所在医疗组");
				if (string.IsNullOrWhiteSpace(orderDeptId))
					orderDeptId = encounter.AdmitDeptId;
				if (!string.Equals(orderDeptId, encounter.AdmitDeptId, StringComparison.OrdinalIgnoreCase))
					return ApiResponse<MtcServiceRequest>.Fail(400, "申请科室必须与住院患者所属科室一致");
				orderWardId ??= encounter.AdmitWardId;
				if (!string.Equals(orderWardId, encounter.AdmitWardId, StringComparison.OrdinalIgnoreCase))
					return ApiResponse<MtcServiceRequest>.Fail(400, "申请病区必须与住院患者所属病区一致");
				sourceVisitId = encounter.Id;
				sourceOrderId = null;
			}
		}
		var dept = await _departmentContract.GetDeptSummaryAsync(orderDeptId);
		if (dept == null || !dept.IsActive)
			return ApiResponse<MtcServiceRequest>.Fail(400, "申请科室不存在或已停用");

		MedTechRoutingResultDto routing = await _routingService.ResolveRoutingAsync(
			string.IsNullOrWhiteSpace(_session?.CampusId) ? "CAMPUS-01" : _session.CampusId,
			sourceType,
			orderDeptId,
			priceSnapshot.ItemCode,
			nowMs);
		if (routing == null || string.IsNullOrWhiteSpace(routing.ExecMedTechDeptId))
			return ApiResponse<MtcServiceRequest>.Fail(400, "未找到该医技项目的执行科室路由配置");

		MtcServiceRequest item = new MtcServiceRequest
		{
			RequestNo = "MTC" + DateTime.Now.ToString("yyMMddHHmmssfff") + Random.Shared.Next(10, 99),
			SourceType = sourceType,
			SourceVisitId = sourceVisitId,
			SourceOrderId = sourceOrderId,
			PatientId = patient.Id,
			OrderDeptId = orderDeptId,
			OrderWardId = orderWardId,
			ExecMedTechDeptId = routing.ExecMedTechDeptId,
			MedTechSubType = routing.MedTechSubType,
			ExecRoomCode = routing.ExecRoomCode,
			ServiceItemCode = priceSnapshot.ItemCode,
			FeeAmount = priceSnapshot.UnitPrice,
			IsFeeConfirmed = 0,
			Status = 0,
			CreatedBy = _session?.UserId,
			UpdatedBy = _session?.UserId
		};
		await _requestRepo.InsertAsync(item);
		await RecordAuditAsync("MTC_REQUEST_CREATE", item, $"开立{sourceType}医技申请 [{item.RequestNo}] {priceSnapshot.ItemName}，服务端定价 {item.FeeAmount:0.00} 元");
		return ApiResponse<MtcServiceRequest>.Success(item, "医技申请单开立并完成智能路由分发");
	}

	[HttpPost("requests/{reqId}/confirm-fee-and-receive")]
	public async Task<ApiResponse<MtcServiceRequest>> ReceiveSampleAndConfirmFee(string reqId)
	{
		var authError = RequireRoles("ROLE-MEDTECH", "ROLE-MEDTECH-STAFF", "MEDTECH", "ROLE-PHARMACIST", "PHARMACIST");
		if (authError != null)
			return ApiResponse<MtcServiceRequest>.Fail(403, authError);
		if (string.IsNullOrWhiteSpace(reqId))
			return ApiResponse<MtcServiceRequest>.Fail(400, "医技申请单号不能为空");

		var result = await _connFactory.ExecuteInTransactionAsync<ApiResponse<MtcServiceRequest>>(async ct =>
		{
			await using var conn = _connFactory.CreateConnection();
			var req = await conn.QuerySingleOrDefaultAsync<MtcServiceRequest>(
				new CommandDefinition("SELECT * FROM mtc_service_requests WHERE id = @Id", new { Id = reqId }, _connFactory.CurrentTransaction));
			if (req == null)
				return ApiResponse<MtcServiceRequest>.Fail(404, "医技申请单不存在");
			if (req.IsFeeConfirmed == 1)
			{
				if (req.Status >= 2)
					return ApiResponse<MtcServiceRequest>.Success(req, "医技申请单已完成核收确费（幂等返回）");
				return ApiResponse<MtcServiceRequest>.Fail(409, "医技申请单已确费，状态异常，请联系财务核查");
			}
			if (req.Status != 0)
				return ApiResponse<MtcServiceRequest>.Fail(409, "仅待核收状态的医技申请可执行到检确费，已撤销/已报告单据不能重复确认");

			var patient = _patientContract == null ? null : await _patientContract.GetPatientSummaryAsync(req.PatientId, ct);
			if (patient == null || !patient.IsActive)
				return ApiResponse<MtcServiceRequest>.Fail(400, "患者不存在或已停用，禁止核收确费");

			string? paymentOrderId = null;
			if (string.Equals(req.SourceType, "OPD", StringComparison.OrdinalIgnoreCase))
			{
				var registration = await conn.QuerySingleOrDefaultAsync<RegistrationPaymentRow>(
					new CommandDefinition(
						"""
						SELECT COALESCE(a.id, v.id) AS appointment_id,
						       v.id AS visit_id,
						       COALESCE(a.payment_id, p.id) AS payment_id,
						       COALESCE(a.paid_amount, p.total_amount, 0) AS paid_amount,
						       CASE WHEN a.refund_id IS NULL AND p.status = 1 THEN 1 ELSE 0 END AS payment_status
						FROM opd_visits v
						LEFT JOIN opd_appointments a
						       ON (a.visit_id = v.id OR (a.visit_id IS NULL AND a.patient_id = v.patient_id))
						      AND a.patient_id = @PatientId
						LEFT JOIN fin_payment_orders p
						       ON p.scene = 'OPD' AND p.status = 1
						      AND (p.id = a.payment_id
						           OR (p.visit_or_encounter_id = @SourceId AND p.patient_id = a.patient_id))
						WHERE (v.id = @SourceId OR v.visit_no = @SourceId) AND a.patient_id = @PatientId
						ORDER BY a.created_at_ms DESC
						LIMIT 1
						""",
						new { SourceId = req.SourceVisitId, PatientId = req.PatientId },
						_connFactory.CurrentTransaction));
				if (registration == null || string.IsNullOrWhiteSpace(registration.PaymentId)
					|| registration.PaymentStatus != 1 || registration.PaidAmount <= 0m)
					return ApiResponse<MtcServiceRequest>.Fail(409, "门诊患者尚未缴纳挂号/接诊费用，禁止核收确费");
				paymentOrderId = registration.PaymentId;
			}
			else if (string.Equals(req.SourceType, "IPD", StringComparison.OrdinalIgnoreCase))
			{
				var encounter = await conn.QuerySingleOrDefaultAsync<IpdPresenceRow>(
					new CommandDefinition(
						"SELECT id, status FROM ipd_encounters WHERE id = @Id OR admission_no = @Id LIMIT 1",
						new { Id = req.SourceVisitId },
						_connFactory.CurrentTransaction));
				if (encounter == null || encounter.Status != 1)
					return ApiResponse<MtcServiceRequest>.Fail(409, "住院患者不在院，禁止核收确费");
			}
			else
			{
				return ApiResponse<MtcServiceRequest>.Fail(409, "医技申请来源无效，禁止核收确费");
			}

			long now = SystemTime.UtcNowMs;
			var claimed = await conn.ExecuteAsync(
				new CommandDefinition(
					"""
					UPDATE mtc_service_requests
					SET is_fee_confirmed = 1, status = 2, received_at_ms = @Now,
					    updated_at_ms = @Now, updated_by = @OperatorId, version_no = version_no + 1
					WHERE id = @Id AND is_fee_confirmed = 0 AND status = 0
					""",
					new { Id = req.Id, Now = now, OperatorId = CurrentUserId },
					_connFactory.CurrentTransaction));
			if (claimed != 1)
				return ApiResponse<MtcServiceRequest>.Fail(409, "医技申请单已被撤单或已被其他人核收，请刷新后重试");

			await conn.ExecuteAsync(
				"""
				INSERT INTO fin_charge_ledgers
				    (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id,
				     item_code, item_name, quantity, unit_price, total_amount, status, payment_order_id,
				     source_service_request_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
				VALUES
				    (@Id, @PatientId, @VisitId, @OrderDeptId, @OrderWardId, @ExecDeptId,
				     @ItemCode, @ItemName, 1, @UnitPrice, @TotalAmount, 1, @PaymentOrderId,
				     @RequestId, 1, @Now, @OperatorId, @Now, @OperatorId, 1)
				""",
				new
				{
					Id = Guid.NewGuid().ToString("N"),
					PatientId = req.PatientId,
					VisitId = req.SourceVisitId,
					OrderDeptId = req.OrderDeptId,
					OrderWardId = req.OrderWardId,
					ExecDeptId = req.ExecMedTechDeptId,
					ItemCode = req.ServiceItemCode,
					ItemName = patient.FullName + " / " + req.ServiceItemCode,
					UnitPrice = req.FeeAmount,
					TotalAmount = req.FeeAmount,
					PaymentOrderId = paymentOrderId,
					RequestId = req.Id,
					Now = now,
					OperatorId = CurrentUserId
				},
				_connFactory.CurrentTransaction);
			await RecordAuditAsync(
				"MTC_FEE_CONFIRM",
				req,
				$"医技到检核收并写入财务记账台账：申请 [{req.RequestNo}]，金额 {req.FeeAmount:0.00} 元",
				ct);
			req.IsFeeConfirmed = 1;
			req.Status = 2;
			req.ReceivedAtMs = now;
			return ApiResponse<MtcServiceRequest>.Success(req, "标本已核收并完成确费，该项目已锁定禁止直接退费");
		});
		return result;
	}

	[HttpPost("requests/{reqId}/publish-report")]
	public async Task<ApiResponse<MtcServiceRequest>> PublishReport(string reqId, [FromBody] PublishReportRequestDto dto)
	{
		var authError = RequireRoles("ROLE-MEDTECH", "ROLE-MEDTECH-STAFF", "MEDTECH", "ROLE-PHARMACIST", "PHARMACIST");
		if (authError != null)
			return ApiResponse<MtcServiceRequest>.Fail(403, authError);
		MtcServiceRequest req = await _requestRepo.GetByIdAsync(reqId);
		if (req == null)
		{
			return ApiResponse<MtcServiceRequest>.Fail(404, "医技申请单不存在");
		}
		if (req.Status != 2)
		{
			return req.Status == 3
				? ApiResponse<MtcServiceRequest>.Fail(409, "医技报告已发布，禁止重复覆盖报告结论")
				: ApiResponse<MtcServiceRequest>.Fail(409, "医技申请单尚未完成核收，或已撤销，禁止发布报告");
		}
		if (req.IsFeeConfirmed != 1)
		{
			return ApiResponse<MtcServiceRequest>.Fail(400, "医技申请单尚未完成扫码到检核收确费，禁止直接发布报告");
		}
		if (string.IsNullOrWhiteSpace(dto.ReportSummary))
		{
			return ApiResponse<MtcServiceRequest>.Fail(400, "报告结论摘要不能为空");
		}
		req.Status = 3;
		req.ExecutedAtMs = SystemTime.UtcNowMs;
		req.ReportSummary = dto.ReportSummary.Trim();
		req.IsCriticalValue = (short)(dto.IsCriticalValue ? 1 : 0);
		req.ReportOfdArchiveId = "OFD-MTC-" + req.RequestNo;
		req.UpdatedBy = CurrentUserId;
		using (var conn = _connFactory.CreateConnection())
		{
			var updated = await conn.ExecuteAsync(
				"""
				UPDATE mtc_service_requests
				SET status = 3, executed_at_ms = @ExecutedAt, report_summary = @ReportSummary,
				    is_critical_value = @IsCriticalValue, report_ofd_archive_id = @ArchiveId,
				    updated_at_ms = @Now, updated_by = @OperatorId, version_no = version_no + 1
				WHERE id = @Id AND status = 2 AND is_fee_confirmed = 1
				""",
				new
				{
					req.Id,
					req.ExecutedAtMs,
					req.ReportSummary,
					req.IsCriticalValue,
					ArchiveId = req.ReportOfdArchiveId,
					Now = SystemTime.UtcNowMs,
					OperatorId = CurrentUserId
				});
			if (updated != 1)
				return ApiResponse<MtcServiceRequest>.Fail(409, "医技报告状态已变更或记录已被其他人处理，请刷新后重试");
		}
		if (dto.IsCriticalValue && _auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("MTC_CRITICAL_VALUE_ALERT", "MtcServiceRequest", req.Id, $"医技危急值强广播：申请单 [{req.RequestNo}] 结论：{req.ReportSummary}，等待管床医师与护士双签收", CurrentUserId, _session?.DisplayName, req.ExecMedTechDeptId);
		}
		await RecordAuditAsync("MTC_REPORT_PUBLISH", req, $"医技报告发布：{req.ReportSummary}" + (dto.IsCriticalValue ? "（危急值）" : ""));
		return ApiResponse<MtcServiceRequest>.Success(req, dto.IsCriticalValue ? "医技报告已发布并触发【危急值】全院声光强推送，需临床医生与病区护士双签收闭环！" : "医技报告已审核发布并生成国标 OFD 归档");
	}

	[HttpPost("requests/{reqId}/critical-ack")]
	public async Task<ApiResponse<MtcServiceRequest>> AcknowledgeCriticalValue(string reqId, [FromBody] CriticalValueAckRequestDto dto)
	{
		if (dto == null)
			return ApiResponse<MtcServiceRequest>.Fail(400, "危急值签收内容不能为空");
		MtcServiceRequest req = await _requestRepo.GetByIdAsync(reqId);
		if (req == null)
		{
			return ApiResponse<MtcServiceRequest>.Fail(404, "医技申请单不存在");
		}
		if (req.IsCriticalValue != 1)
		{
			return ApiResponse<MtcServiceRequest>.Fail(400, "该医技报告非危急值报告，无需执行危急值签收");
		}
		if (string.IsNullOrWhiteSpace(_session?.UserId))
			return ApiResponse<MtcServiceRequest>.Fail(401, "危急值签收必须使用本人登录身份");
		bool isDoctor = HasAnyRole("ROLE-OPD-DOC", "ROLE-IPD-RESIDENT", "ROLE-IPD-ATTENDING", "OPD_DOC", "DOCTOR", "PHYSICIAN", "ATTENDING", "RESIDENT");
		bool isNurse = HasAnyRole("ROLE-WARD-NURSE", "ROLE-OPD-NURSE", "WARD_NURSE", "OPD_NURSE", "NURSE", "NURSING");
		if (!isDoctor && !isNurse)
			return ApiResponse<MtcServiceRequest>.Fail(403, "危急值签收仅限临床医师或责任护士");

		var operatorId = _session.UserId;
		if (!string.IsNullOrWhiteSpace(dto.DoctorId)
			&& !string.Equals(dto.DoctorId.Trim(), operatorId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<MtcServiceRequest>.Fail(403, "医师签收工号必须与当前登录身份一致");
		if (!string.IsNullOrWhiteSpace(dto.NurseId)
			&& !string.Equals(dto.NurseId.Trim(), operatorId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<MtcServiceRequest>.Fail(403, "护士签收工号必须与当前登录身份一致");
		if (string.IsNullOrWhiteSpace(dto.DoctorId) && string.IsNullOrWhiteSpace(dto.NurseId))
			return ApiResponse<MtcServiceRequest>.Fail(400, "危急值签收必须由当前登录的医师或护士执行");

		if (req.Status != 3)
			return ApiResponse<MtcServiceRequest>.Fail(409, "危急值报告尚未完成审核发布，禁止签收");
		if (isDoctor)
		{
			if (!string.IsNullOrWhiteSpace(req.CriticalAckDocId)
				&& !string.Equals(req.CriticalAckDocId, operatorId, StringComparison.OrdinalIgnoreCase))
				return ApiResponse<MtcServiceRequest>.Fail(409, "医师签收已由其他经治医师完成，禁止覆盖签名");
			req.CriticalAckDocId = operatorId;
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(req.CriticalAckNurseId)
				&& !string.Equals(req.CriticalAckNurseId, operatorId, StringComparison.OrdinalIgnoreCase))
				return ApiResponse<MtcServiceRequest>.Fail(409, "护士签收已由其他责任护士完成，禁止覆盖签名");
			req.CriticalAckNurseId = operatorId;
		}
		if (!string.IsNullOrWhiteSpace(req.CriticalAckDocId)
			&& string.Equals(req.CriticalAckDocId, req.CriticalAckNurseId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<MtcServiceRequest>.Fail(400, "危急值双签收必须由不同的医师和护士分别完成");
		using (var conn = _connFactory.CreateConnection())
		{
			var updated = await conn.ExecuteAsync(
				"""
				UPDATE mtc_service_requests
				SET critical_ack_doc_id = @DocId, critical_ack_nurse_id = @NurseId,
				    updated_at_ms = @Now, updated_by = @OperatorId, version_no = version_no + 1
				WHERE id = @Id AND is_critical_value = 1 AND status = 3
				  AND (critical_ack_doc_id IS NULL OR critical_ack_doc_id = @DocId)
				  AND (critical_ack_nurse_id IS NULL OR critical_ack_nurse_id = @NurseId)
				""",
				new
				{
					req.Id,
					DocId = req.CriticalAckDocId,
					NurseId = req.CriticalAckNurseId,
					Now = SystemTime.UtcNowMs,
					OperatorId = operatorId
				});
			if (updated != 1)
				return ApiResponse<MtcServiceRequest>.Fail(409, "危急值签收状态已变更，请刷新后重试");
		}
		bool isDualCompleted = !string.IsNullOrWhiteSpace(req.CriticalAckDocId) && !string.IsNullOrWhiteSpace(req.CriticalAckNurseId);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("MTC_CRITICAL_VALUE_ACK", "MtcServiceRequest", req.Id, $"危急值签收更新：医师={req.CriticalAckDocId ?? "待签"}, 护士={req.CriticalAckNurseId ?? "待签"}, 处置={dto.ClinicalActionNote ?? "已处置"}", operatorId, _session.DisplayName);
		}
		return ApiResponse<MtcServiceRequest>.Success(req, isDualCompleted ? "危急值【医师+护士】双签收闭环已完成！" : "危急值单方签收已记录，请继续完成医护双签收闭环");
	}

	[HttpPost("requests/{reqId}/cancel")]
	public async Task<ApiResponse<bool>> CancelRequest(string reqId)
	{
		var authError = RequirePrescribingRole();
		if (authError != null)
			return ApiResponse<bool>.Fail(403, authError, data: false);
		MtcServiceRequest req = await _requestRepo.GetByIdAsync(reqId);
		if (req == null)
		{
			return ApiResponse<bool>.Fail(404, "医技申请单不存在", data: false);
		}
		if (req.IsFeeConfirmed == 1)
		{
			return ApiResponse<bool>.Fail(400, "医技单已扫码核收确费（is_fee_confirmed=1），禁止直接撤单退费！需医技科室出具冲正退检证明", data: false);
		}
		if (req.Status == 4)
			return ApiResponse<bool>.Success(data: true, "医技申请单已撤销（幂等返回）");
		if (req.Status != 0)
			return ApiResponse<bool>.Fail(409, "仅有待核收状态的医技申请可以撤销", data: false);
		using (var conn = _connFactory.CreateConnection())
		{
			var cancelled = await conn.ExecuteAsync(
				"""
				UPDATE mtc_service_requests
				SET status = 4, updated_at_ms = @Now, updated_by = @OperatorId, version_no = version_no + 1
				WHERE id = @Id AND is_fee_confirmed = 0 AND status = 0
				""",
				new { req.Id, Now = SystemTime.UtcNowMs, OperatorId = CurrentUserId });
			if (cancelled != 1)
				return ApiResponse<bool>.Fail(409, "医技申请单已核收确费或已被撤销，不能重复撤单", data: false);
		}
		req.Status = 4;
		await RecordAuditAsync("MTC_REQUEST_CANCEL", req, "撤销未确费医技申请");
		return ApiResponse<bool>.Success(data: true, "医技申请单撤销成功");
	}

	[HttpPost("surgeries/safety-check")]
	public async Task<ApiResponse<MtcSurgerySchedule>> RecordSurgerySafetyCheck([FromBody] SurgeryCheckDto req)
	{
		var authError = RequireRoles("ROLE-WARD-NURSE", "ROLE-OPD-NURSE", "ROLE-OR-NURSE", "WARD_NURSE", "OPD_NURSE", "ROLE-IPD-ATTENDING");
		if (authError != null)
			return ApiResponse<MtcSurgerySchedule>.Fail(403, authError);
		if (req == null)
			return ApiResponse<MtcSurgerySchedule>.Fail(400, "手术安全核查内容不能为空");
		MtcSurgerySchedule surg = await _surgeryRepo.GetByIdAsync(req.ScheduleId);
		if (surg == null)
		{
			return ApiResponse<MtcSurgerySchedule>.Fail(404, "手术排班不存在");
		}
		if (surg.Status >= 2)
			return ApiResponse<MtcSurgerySchedule>.Fail(409, "手术已结束，禁止修改安全核查记录");
		bool isTeamMember = IsAdmin()
			|| string.Equals(surg.CirculatingNurseId, CurrentUserId, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(surg.MainSurgeonId, CurrentUserId, StringComparison.OrdinalIgnoreCase);
		if (!isTeamMember)
			return ApiResponse<MtcSurgerySchedule>.Fail(403, "仅该台手术的主刀医师或巡回护士可以记录安全核查");
		surg.SignInOk = (short)(req.SignInOk ? 1 : 0);
		surg.TimeOutOk = (short)(req.TimeOutOk ? 1 : 0);
		surg.SignOutOk = (short)(req.SignOutOk ? 1 : 0);
		surg.InstrumentCountBalanced = (short)(req.InstrumentCountBalanced ? 1 : 0);
		if (surg.Status == 0 && surg.SignInOk == 1 && surg.TimeOutOk == 1)
		{
			surg.Status = 1;
		}
		using (var conn = _connFactory.CreateConnection())
		{
			var updated = await conn.ExecuteAsync(
				"""
				UPDATE mtc_surgery_schedules
				SET sign_in_ok = @SignInOk, time_out_ok = @TimeOutOk, sign_out_ok = @SignOutOk,
				    instrument_count_balanced = @InstrumentCountBalanced, status = @Status,
				    updated_at_ms = @Now, updated_by = @OperatorId, version_no = version_no + 1
				WHERE id = @Id AND status < 2
				""",
				new
				{
					surg.Id,
					surg.SignInOk,
					surg.TimeOutOk,
					surg.SignOutOk,
					surg.InstrumentCountBalanced,
					surg.Status,
					Now = SystemTime.UtcNowMs,
					OperatorId = CurrentUserId
				});
			if (updated != 1)
				return ApiResponse<MtcSurgerySchedule>.Fail(409, "手术状态已变更，安全核查记录未保存，请刷新后重试");
		}
		await RecordAuditAsync("MTC_SURGERY_SAFETY_CHECK", surg, "记录 WHO 手术三方安全核查");
		return ApiResponse<MtcSurgerySchedule>.Success(surg, "手术三方安全核查记录已保存");
	}

	[HttpPost("surgeries/{scheduleId}/finish")]
	public async Task<ApiResponse<MtcSurgerySchedule>> FinishSurgery(string scheduleId)
	{
		var authError = RequireRoles("ROLE-WARD-NURSE", "ROLE-OPD-NURSE", "ROLE-OR-NURSE", "WARD_NURSE", "OPD_NURSE", "ROLE-IPD-ATTENDING");
		if (authError != null)
			return ApiResponse<MtcSurgerySchedule>.Fail(403, authError);
		MtcSurgerySchedule surg = await _surgeryRepo.GetByIdAsync(scheduleId);
		if (surg == null)
		{
			return ApiResponse<MtcSurgerySchedule>.Fail(404, "手术排班不存在");
		}
		if (surg.SignInOk != 1 || surg.TimeOutOk != 1 || surg.SignOutOk != 1 || surg.InstrumentCountBalanced != 1)
		{
			return ApiResponse<MtcSurgerySchedule>.Fail(400, "WHO 三方安全核查（Sign-in/Time-out/Sign-out）未全部通过或器械清点不平衡，严禁结束手术转出！");
		}
		if (surg.Status == 2)
			return ApiResponse<MtcSurgerySchedule>.Success(surg, "手术已结束（幂等返回）");
		if (surg.Status != 1)
			return ApiResponse<MtcSurgerySchedule>.Fail(409, "手术尚未进入 Time-out 核查状态，禁止结束手术");
		bool isTeamMember = IsAdmin()
			|| string.Equals(surg.CirculatingNurseId, CurrentUserId, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(surg.MainSurgeonId, CurrentUserId, StringComparison.OrdinalIgnoreCase);
		if (!isTeamMember)
			return ApiResponse<MtcSurgerySchedule>.Fail(403, "仅该台手术的主刀医师或巡回护士可以结束手术");
		using (var conn = _connFactory.CreateConnection())
		{
			var updated = await conn.ExecuteAsync(
				"""
				UPDATE mtc_surgery_schedules
				SET status = 2, updated_at_ms = @Now, updated_by = @OperatorId, version_no = version_no + 1
				WHERE id = @Id AND status = 1
				""",
				new { surg.Id, Now = SystemTime.UtcNowMs, OperatorId = CurrentUserId });
			if (updated != 1)
				return ApiResponse<MtcSurgerySchedule>.Fail(409, "手术状态已变更，禁止重复结束手术");
		}
		surg.Status = 2;
		await RecordAuditAsync("MTC_SURGERY_FINISH", surg, "手术结束并完成 WHO 三方安全核查");
		return ApiResponse<MtcSurgerySchedule>.Success(surg, "手术已顺利完成并准予转入 PACU 复苏");
	}

	[HttpPost("blood/dual-dispense")]
	public async Task<ApiResponse<MtcBloodRequest>> DualCheckBloodDispense([FromBody] DualCheckBloodDispenseDto req)
	{
		if (req == null)
			return ApiResponse<MtcBloodRequest>.Fail(400, "输血发血核对内容不能为空");
		var authError = RequireRoles("ROLE-MEDTECH", "ROLE-MEDTECH-STAFF", "MEDTECH", "ROLE-PHARMACIST", "PHARMACIST");
		if (authError != null)
			return ApiResponse<MtcBloodRequest>.Fail(403, authError);
		MtcBloodRequest item = await _bloodRepo.GetByIdAsync(req.RequestId);
		if (item == null)
		{
			return ApiResponse<MtcBloodRequest>.Fail(404, "输血申请不存在");
		}
		if (string.IsNullOrWhiteSpace(req.DispenserId) || string.IsNullOrWhiteSpace(req.DualCheckDispenserId))
		{
			return ApiResponse<MtcBloodRequest>.Fail(400, "输血发血必须具备双人电子签名核验");
		}
		if (!string.Equals(req.DispenserId.Trim(), CurrentUserId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<MtcBloodRequest>.Fail(403, "发血主核对人工号必须与当前登录身份一致");
		if (string.Equals(req.DispenserId.Trim(), req.DualCheckDispenserId.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return ApiResponse<MtcBloodRequest>.Fail(400, "输血安全红线：发血主核对人与第二复核人严禁为同一人！");
		}
		if (string.IsNullOrWhiteSpace(req.BagBarcode))
			return ApiResponse<MtcBloodRequest>.Fail(400, "血袋条码不能为空");
		using (var conn = _connFactory.CreateConnection())
		{
			var dualCheckUser = await conn.QuerySingleOrDefaultAsync<BloodDualCheckUserRow>(
				"SELECT id, is_active FROM sys_users WHERE id = @Id LIMIT 1",
				new { Id = req.DualCheckDispenserId.Trim() });
			if (dualCheckUser == null || dualCheckUser.IsActive != 1)
				return ApiResponse<MtcBloodRequest>.Fail(400, "第二复核人不属于有效在岗人员");
		}
		if (item.Status == 2)
			return ApiResponse<MtcBloodRequest>.Fail(409, "该输血申请已发血，禁止重复发血");
		if (item.Status != 0 && item.Status != 1)
			return ApiResponse<MtcBloodRequest>.Fail(409, "该输血申请当前状态不可发血");
		item.DispensedById = req.DispenserId.Trim();
		item.DualCheckDispenserId = req.DualCheckDispenserId.Trim();
		item.BagBarcode = req.BagBarcode.Trim();
		item.Status = 2;
		using (var conn = _connFactory.CreateConnection())
		{
			var updated = await conn.ExecuteAsync(
				"""
				UPDATE mtc_blood_requests
				SET dispensed_by_id = @DispenserId, dual_check_dispenser_id = @DualCheckId,
				    bag_barcode = @BagBarcode, status = 2, updated_at_ms = @Now,
				    updated_by = @OperatorId, version_no = version_no + 1
				WHERE id = @Id AND status IN (0, 1) AND empty_bag_returned = 0
				""",
				new
				{
					item.Id,
					DispenserId = item.DispensedById,
					DualCheckId = item.DualCheckDispenserId,
					item.BagBarcode,
					Now = SystemTime.UtcNowMs,
					OperatorId = CurrentUserId
				});
			if (updated != 1)
				return ApiResponse<MtcBloodRequest>.Fail(409, "输血申请已被其他人处理，禁止重复发血");
		}
		await RecordAuditAsync("MTC_BLOOD_DISPENSE", item, $"双人核对发血：血袋 {item.BagBarcode}，复核人 {item.DualCheckDispenserId}");
		return ApiResponse<MtcBloodRequest>.Success(item, "双人扫码核对发血完成");
	}

	[HttpPost("blood/{requestId}/return-bag")]
	public async Task<ApiResponse<MtcBloodRequest>> ReturnEmptyBloodBag(string requestId, [FromQuery] string? operatorId = null)
	{
		var authError = RequireRoles("ROLE-MEDTECH", "ROLE-MEDTECH-STAFF", "MEDTECH", "ROLE-PHARMACIST", "PHARMACIST");
		if (authError != null)
			return ApiResponse<MtcBloodRequest>.Fail(403, authError);
		MtcBloodRequest item = await _bloodRepo.GetByIdAsync(requestId);
		if (item == null)
		{
			return ApiResponse<MtcBloodRequest>.Fail(404, "输血申请不存在");
		}
		if (!string.IsNullOrWhiteSpace(operatorId)
			&& !string.Equals(operatorId.Trim(), CurrentUserId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<MtcBloodRequest>.Fail(403, "操作人工号必须与当前登录身份一致");
		if (item.EmptyBagReturned == 1)
			return ApiResponse<MtcBloodRequest>.Fail(409, "空血袋已回收核销，禁止重复操作");
		if (item.Status != 2)
			return ApiResponse<MtcBloodRequest>.Fail(409, "仅已发血且处于输注状态的申请可回收空血袋");
		using (var conn = _connFactory.CreateConnection())
		{
			var updated = await conn.ExecuteAsync(
				"""
				UPDATE mtc_blood_requests
				SET empty_bag_returned = 1, status = 4, updated_at_ms = @Now,
				    updated_by = @OperatorId, version_no = version_no + 1
				WHERE id = @Id AND status = 2 AND empty_bag_returned = 0
				""",
				new { item.Id, Now = SystemTime.UtcNowMs, OperatorId = CurrentUserId });
			if (updated != 1)
				return ApiResponse<MtcBloodRequest>.Fail(409, "输血申请状态已变更，禁止重复回收空血袋");
		}
		item.EmptyBagReturned = 1;
		item.Status = 4;
		item.UpdatedBy = CurrentUserId;
		await RecordAuditAsync("MTC_BLOOD_RETURN_BAG", item, $"空血袋回收核销：{item.BagBarcode}");
		return ApiResponse<MtcBloodRequest>.Success(item, "输血完毕空血袋已扫码回收核销，输血闭环完成");
	}

	private string CurrentUserId => _session?.UserId?.Trim() ?? string.Empty;

	private bool IsAdmin() =>
		_session != null && !string.IsNullOrWhiteSpace(_session.UserId)
		&& (RoleCode.IsAdmin(_session.RoleCode) || RoleCode.HasRole(_session.Roles, RoleCode.Admin));

	private bool HasAnyRole(params string[] allowed) =>
		_session != null && RoleCode.HasAnyRole(_session.Roles, allowed);

	private string? RequireRoles(params string[] allowed)
	{
		if (string.IsNullOrWhiteSpace(CurrentUserId))
			return "请先登录并获得医技操作权限";
		if (IsAdmin())
			return null;
		return HasAnyRole(allowed) ? null : "当前登录角色无权执行该医技操作";
	}

	private string? RequirePrescribingRole() =>
		RequireRoles("ROLE-OPD-DOC", "ROLE-IPD-RESIDENT", "ROLE-IPD-ATTENDING",
			"OPD_DOC", "IPD_RESIDENT", "IPD_ATTENDING", "DOCTOR", "PHYSICIAN");

	private async Task RecordAuditAsync(string eventType, MtcServiceRequest item, string detail, CancellationToken ct = default)
	{
		if (_auditTrail != null)
			await _auditTrail.RecordAuditAsync(eventType, nameof(MtcServiceRequest), item.Id, detail,
				CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
	}

	private async Task RecordAuditAsync(string eventType, MtcSurgerySchedule item, string detail, CancellationToken ct = default)
	{
		if (_auditTrail != null)
			await _auditTrail.RecordAuditAsync(eventType, nameof(MtcSurgerySchedule), item.Id, detail,
				CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
	}

	private async Task RecordAuditAsync(string eventType, MtcBloodRequest item, string detail, CancellationToken ct = default)
	{
		if (_auditTrail != null)
			await _auditTrail.RecordAuditAsync(eventType, nameof(MtcBloodRequest), item.Id, detail,
				CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
	}

	private sealed class SourceVisitRow
	{
		public string Id { get; set; } = "";
		public string PatientId { get; set; } = "";
		public string OpdDeptId { get; set; } = "";
		public string DoctorId { get; set; } = "";
		public int Status { get; set; }
	}

	private sealed class SourceEncounterRow
	{
		public string Id { get; set; } = "";
		public string PatientId { get; set; } = "";
		public string AdmitDeptId { get; set; } = "";
		public string AdmitWardId { get; set; } = "";
		public string AttendingDoctorId { get; set; } = "";
		public int Status { get; set; }
	}

	private sealed class RegistrationPaymentRow
	{
		public string AppointmentId { get; set; } = "";
		public string VisitId { get; set; } = "";
		public string? PaymentId { get; set; }
		public decimal PaidAmount { get; set; }
		public int PaymentStatus { get; set; }
	}

	private sealed class IpdPresenceRow
	{
		public string Id { get; set; } = "";
		public int Status { get; set; }
	}

	private sealed class BloodDualCheckUserRow
	{
		public string Id { get; set; } = "";
		public short IsActive { get; set; }
	}
}
