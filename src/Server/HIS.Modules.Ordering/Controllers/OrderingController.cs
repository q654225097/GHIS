using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Ordering.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Ordering.Controllers;

[ApiController]
[Route("api/v4/orders")]
public class OrderingController : ControllerBase
{
	public sealed record CreateOrderRequestDto(
		string EncounterId,
		string PatientId,
		string OrderIpdDeptId,
		string PatientWardId,
		string ExecTargetDeptId,
		int OrderTermType,
		int GroupNo,
		string OrderCategory,
		string ItemCode,
		string ItemName,
		decimal Dosage,
		string FrequencyCode,
		string RouteCode,
		string OrderDoctorId,
		string? OrderSubType = null,
		string? ParentOrderId = null,
		bool IsUrgent = false,
		bool AllowArrearsOverride = false);

	public sealed record AssignGroupRequestDto(IReadOnlyList<string> OrderIds, int? GroupNo = null, string? ParentOrderId = null);

	public sealed record VerifyOrderRequestDto(string VerifyNurseId, string WardId);

	public sealed record PdaExecuteRequestDto(
		string PlanId,
		string ExecNurseId,
		string DualCheckNurseId,
		string PdaScanToken,
		string? PatientWristbandCode = null,
		string? ItemBarcode = null,
		string? HighRiskAckReason = null);

	private readonly IRepository<OrdMedicalOrder> _orderRepo;

	private readonly IRepository<OrdExecutionPlan> _planRepo;

	private readonly IDbConnectionFactory _connFactory;

	private readonly IMasterDataReadOnlyContract? _masterData;

	private readonly IAuditTrailContract? _auditTrail;

	private readonly IUserSession? _session;

	private readonly IPatientReadOnlyContract? _patientContract;

	private readonly IDepartmentReadOnlyContract? _departmentContract;

	private readonly IPharmacyInventoryContract? _inventoryContract;

	private readonly IClinicalProcessSignatureContract? _signatureContract;

	private readonly IClinicalAlertPublisher? _alertPublisher;
	private readonly IWorkflowEngineContract? _workflowEngine;

	public OrderingController(
		IRepository<OrdMedicalOrder> orderRepo,
		IRepository<OrdExecutionPlan> planRepo,
		IDbConnectionFactory? connFactory = null,
		IMasterDataReadOnlyContract? masterData = null,
		IAuditTrailContract? auditTrail = null,
		IUserSession? session = null,
		IPatientReadOnlyContract? patientContract = null,
		IDepartmentReadOnlyContract? departmentContract = null,
		IPharmacyInventoryContract? inventoryContract = null,
		IClinicalProcessSignatureContract? signatureContract = null,
		IClinicalAlertPublisher? alertPublisher = null, IWorkflowEngineContract? workflowEngine = null)
	{
		_orderRepo = orderRepo;
		_planRepo = planRepo;
		_connFactory = connFactory ?? new SqliteConnectionFactory();
		_masterData = masterData;
		_auditTrail = auditTrail;
		_session = session;
		_patientContract = patientContract;
		_departmentContract = departmentContract;
		_inventoryContract = inventoryContract;
		_signatureContract = signatureContract;
		_alertPublisher = alertPublisher;
		_workflowEngine = workflowEngine;
	}

	[HttpGet("by-encounter/{encounterId}")]
	public async Task<ApiResponse<IReadOnlyList<OrdMedicalOrder>>> GetOrdersByEncounter(string encounterId)
	{
		return ApiResponse<IReadOnlyList<OrdMedicalOrder>>.Success(await _orderRepo.QueryAsync("SELECT * FROM ord_medical_orders WHERE encounter_id = @EncounterId ORDER BY group_no ASC, created_at_ms DESC", new
		{
			EncounterId = encounterId
		}));
	}

	[HttpGet("plans/by-encounter/{encounterId}")]
	public async Task<ApiResponse<IReadOnlyList<OrdExecutionPlan>>> GetPlansByEncounter(string encounterId)
	{
		return ApiResponse<IReadOnlyList<OrdExecutionPlan>>.Success(await _planRepo.QueryAsync("SELECT * FROM ord_execution_plans WHERE encounter_id = @EncounterId ORDER BY planned_time_ms ASC", new
		{
			EncounterId = encounterId
		}));
	}

	[HttpPost]
	public async Task<ApiResponse<OrdMedicalOrder>> CreateOrder([FromBody] CreateOrderRequestDto req)
	{
		var authError = RequireRoles("ROLE-IPD-RESIDENT", "ROLE-IPD-ATTENDING", "IPD_RESIDENT", "IPD_ATTENDING");
		if (authError != null)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(authError.Value.Status, authError.Value.Message);
		}

		if (string.IsNullOrWhiteSpace(req.EncounterId))
			return ApiResponse<OrdMedicalOrder>.Fail(400, "住院就诊ID不能为空");
		if (req.OrderTermType is not (1 or 2))
			return ApiResponse<OrdMedicalOrder>.Fail(400, "医嘱性质必须为长期(1)或临时(2)");
		var subType = string.IsNullOrWhiteSpace(req.OrderSubType) ? "NORMAL" : req.OrderSubType.Trim().ToUpperInvariant();
		if (subType == "DISCHARGE_MED" && req.OrderTermType != 2)
			return ApiResponse<OrdMedicalOrder>.Fail(400, "出院带药医嘱性质必须为临时医嘱(2)");
		if (subType != "INSTRUCTION" && req.Dosage <= 0m)
			return ApiResponse<OrdMedicalOrder>.Fail(400, "医嘱剂量必须大于零");

		await using var conn = _connFactory.CreateConnection();
		await conn.OpenAsync();
		var encounter = await conn.QuerySingleOrDefaultAsync<OrderEncounterRow>(new CommandDefinition(
			"SELECT id, patient_id, admit_dept_id, admit_ward_id, prepayment_balance, status, triage_status FROM ipd_encounters WHERE id = @Id",
			new { Id = req.EncounterId.Trim() }));
		if (encounter == null)
			return ApiResponse<OrdMedicalOrder>.Fail(404, "住院就诊记录不存在");
		if (encounter.Status != 1 || encounter.TriageStatus != 1)
			return ApiResponse<OrdMedicalOrder>.Fail(409, "患者尚未完成病区接诊或已不在院，禁止开立医嘱");

		if (!req.IsUrgent && !req.AllowArrearsOverride && subType is not ("INSTRUCTION" or "DISCHARGE_MED"))
		{
			decimal feeItemsTotal = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
				"SELECT COALESCE(SUM(total_amount), 0) FROM fin_inpatient_fee_items WHERE encounter_id = @EncId AND is_refunded = 0 AND is_settled = 0",
				new { EncId = encounter.Id }));
			decimal ledgersTotal = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
				"SELECT COALESCE(SUM(total_amount), 0) FROM fin_charge_ledgers WHERE visit_or_encounter_id = @EncId AND status = 1",
				new { EncId = encounter.Id }));
			decimal availableBalance = encounter.PrepaymentBalance - (feeItemsTotal + ledgersTotal);
			if (availableBalance < 0m)
			{
				return ApiResponse<OrdMedicalOrder>.Fail(409,
					$"患者住院预交金已欠费 ¥{Math.Abs(availableBalance):0.00}（ARREARS_LOCKOUT），非紧急医嘱开立受限！请尽快补缴押金或由主治勾选加急抢救绿色通道开立。");
			}
		}

		var patient = await _patientContract?.GetPatientSummaryAsync(encounter.PatientId)
			?? await LoadPatientSummaryAsync(conn, encounter.PatientId);
		if (patient == null || !patient.IsActive)
			return ApiResponse<OrdMedicalOrder>.Fail(409, "患者档案不存在或已停用，禁止开立医嘱");
		if (!string.Equals(patient.Id, req.PatientId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdMedicalOrder>.Fail(400, "提交的患者与住院记录不一致");
		if (!string.Equals(encounter.AdmitDeptId, req.OrderIpdDeptId, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(encounter.AdmitWardId, req.PatientWardId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdMedicalOrder>.Fail(400, "开立科室或病区与住院记录不一致");

		var doctorId = _session!.UserId;
		if (req.OrderDoctorId != doctorId && !IsAdmin())
			return ApiResponse<OrdMedicalOrder>.Fail(403, "开立医生必须为当前登录医生");
		var execDeptId = string.IsNullOrWhiteSpace(req.ExecTargetDeptId) ? encounter.AdmitWardId : req.ExecTargetDeptId.Trim();
		if (_departmentContract != null && await _departmentContract.GetDeptSummaryAsync(execDeptId) == null)
			return ApiResponse<OrdMedicalOrder>.Fail(400, "执行科室不存在");

		var frequencyCode = string.IsNullOrWhiteSpace(req.FrequencyCode) ? "QD" : req.FrequencyCode.Trim().ToUpperInvariant();
		var category = string.IsNullOrWhiteSpace(req.OrderCategory) ? "DRUG" : req.OrderCategory.Trim().ToUpperInvariant();
		if (subType == "INSTRUCTION") category = "INSTRUCTION";
		if (!string.IsNullOrWhiteSpace(req.ItemCode))
		{
			int? orderItemActive = await conn.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
				"SELECT is_active FROM cfg_order_items WHERE order_item_code = @Code LIMIT 1",
				new { Code = req.ItemCode.Trim() }));
			if (orderItemActive == 0)
				return ApiResponse<OrdMedicalOrder>.Fail(409, $"诊疗服务项目主数据 [{req.ItemCode}] 已停用，禁止开立医嘱");
		}
		var itemCode = req.ItemCode?.Trim() ?? string.Empty;
		var itemName = req.ItemName?.Trim() ?? string.Empty;
		DrugCatalogSnapshotDto? drug = null;
		if (category == "DRUG")
		{
			if (string.IsNullOrWhiteSpace(itemCode))
				return ApiResponse<OrdMedicalOrder>.Fail(400, "药品编码不能为空");

			drug = await _inventoryContract?.GetDrugSnapshotAsync(itemCode)
				?? await LoadDrugSnapshotAsync(conn, itemCode);
			if (drug == null || !drug.IsActive)
				return ApiResponse<OrdMedicalOrder>.Fail(409, "药品已停用或不存在，禁止开立医嘱");
			itemName = drug.DrugName;

			var allergyConflict = patient.AllergyTags.FirstOrDefault(tag =>
				!string.IsNullOrWhiteSpace(tag) &&
				(itemName.Contains(tag, StringComparison.OrdinalIgnoreCase) ||
				 tag.Contains(itemName, StringComparison.OrdinalIgnoreCase) ||
				 tag.Contains(drug.DrugCode, StringComparison.OrdinalIgnoreCase)));
			if (allergyConflict != null)
				return ApiResponse<OrdMedicalOrder>.Fail(409, $"患者过敏史命中【{allergyConflict}】，禁止开立 {itemName}");

			if ((drug.AntibioticLevel >= 2 || drug.IsNarcotic || drug.IsSkinTestRequired) && _session!.PrescribeLevel < 2)
				return ApiResponse<OrdMedicalOrder>.Fail(403, "当前医师处方权限不足，不能开立限制级抗菌药、麻精药或需皮试药品");

			if (drug.AntibioticLevel >= 2 && !req.IsUrgent)
			{
				int microCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"""
					SELECT (
					    SELECT count(*) FROM mtc_service_requests
					    WHERE (patient_id = @PatId OR source_visit_id = @EncId)
					      AND status <> 4
					      AND (med_tech_sub_type IN ('LAB', 'MICROBIOLOGY') OR service_item_code LIKE '%MICRO%' OR service_item_code LIKE '%CULTURE%')
					) + (
					    SELECT count(*) FROM ord_medical_orders
					    WHERE encounter_id = @EncId AND order_category IN ('LAB', 'EXAM') AND status IN (0, 1, 2)
					)
					""",
					new { PatId = encounter.PatientId, EncId = encounter.Id }));
				if (microCount == 0)
				{
					return ApiResponse<OrdMedicalOrder>.Fail(409,
						$"【抗菌药物管理规范拦截】开立限制使用级/特殊使用级抗菌药 [{itemName}] 前，须先为患者开立病原学/微生物送检申请（若属急诊抢救用药，请勾选加急）。");
				}
			}
		}
		else if (string.IsNullOrWhiteSpace(itemName))
		{
			return ApiResponse<OrdMedicalOrder>.Fail(400, "医嘱项目名称不能为空");
		}

		var frequency = await _masterData?.GetOrderFrequencyAsync(frequencyCode);
		if (frequency != null)
		{
			var applicable = (frequency.ApplicableTerm ?? "ALL").Trim().ToUpperInvariant();
			if (applicable == "TEMPORARY" && req.OrderTermType != 2)
				return ApiResponse<OrdMedicalOrder>.Fail(400, $"频次 [{frequencyCode}] 仅适用于临时医嘱");
			if (applicable == "STANDING" && req.OrderTermType != 1)
				return ApiResponse<OrdMedicalOrder>.Fail(400, $"频次 [{frequencyCode}] 仅适用于长期医嘱");
		}
		else if (frequencyCode.StartsWith("ST") && req.OrderTermType != 2)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(400, "ST（立即执行）频次只能用于临时医嘱");
		}

		var duplicate = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
			"""
			SELECT count(*) FROM ord_medical_orders
			WHERE encounter_id = @EncounterId AND order_term_type = @TermType
			  AND order_category = @Category AND item_code = @ItemCode
			  AND frequency_code = @FrequencyCode AND status IN (0, 1, 2)
			""",
			new { EncounterId = encounter.Id, TermType = req.OrderTermType, Category = category, ItemCode = itemCode, FrequencyCode = frequencyCode }));
		if (duplicate > 0)
			return ApiResponse<OrdMedicalOrder>.Fail(409, "存在相同的在院医嘱，请勿重复开立");

		string approvalStatus = (drug != null && drug.AntibioticLevel >= 2) ? "PENDING_APPROVAL" : "APPROVED";

		OrdMedicalOrder order = new OrdMedicalOrder
		{
			OrderNo = "ORD" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmssfff") + Guid.NewGuid().ToString("N")[..4],
			EncounterId = encounter.Id,
			PatientId = encounter.PatientId,
			OrderIpdDeptId = encounter.AdmitDeptId,
			PatientWardId = encounter.AdmitWardId,
			ExecTargetDeptId = execDeptId,
			OrderTermType = req.OrderTermType,
			GroupNo = req.GroupNo > 0 ? req.GroupNo : 1,
			OrderCategory = category,
			OrderSubType = subType,
			ParentOrderId = string.IsNullOrWhiteSpace(req.ParentOrderId) ? null : req.ParentOrderId.Trim(),
			ItemCode = itemCode,
			ItemName = itemName,
			Dosage = req.Dosage,
			FrequencyCode = frequencyCode,
			RouteCode = string.IsNullOrWhiteSpace(req.RouteCode) ? "PO" : req.RouteCode.Trim().ToUpperInvariant(),
			Status = 0,
			ApprovalStatus = approvalStatus,
			OrderDoctorId = doctorId,
			PlanStartAtMs = SystemTime.UtcNowMs,
			CreatedBy = doctorId,
			UpdatedBy = doctorId
		};
		await _orderRepo.InsertAsync(order);
		if (req.IsUrgent)
		{
			await conn.ExecuteAsync("UPDATE ord_medical_orders SET is_urgent = 1 WHERE id = @Id", new { Id = order.Id });
		}
		if (_workflowEngine != null && string.Equals(order.ApprovalStatus, "PENDING_APPROVAL", StringComparison.OrdinalIgnoreCase))
		{
			var wfStart = await _workflowEngine.StartWorkflowAsync("ORD_SPECIAL_DRUG", order.Id, doctorId, encounter.AdmitDeptId, new Dictionary<string, object?>
			{
				["orderNo"] = order.OrderNo,
				["itemCode"] = order.ItemCode,
				["itemName"] = order.ItemName,
				["dosage"] = order.Dosage,
				["antibioticLevel"] = drug?.AntibioticLevel ?? 0
			});
			if (wfStart.IsWorkflowTriggered && !string.IsNullOrWhiteSpace(wfStart.InstanceId))
			{
				order.WorkflowInstanceId = wfStart.InstanceId;
				await conn.ExecuteAsync("UPDATE ord_medical_orders SET workflow_instance_id = @WfId, submitted_at_ms = @Now WHERE id = @Id",
					new { WfId = wfStart.InstanceId, Now = SystemTime.UtcNowMs, Id = order.Id });
			}
			else if (!wfStart.IsWorkflowTriggered)
			{
				order.ApprovalStatus = "APPROVED";
				await conn.ExecuteAsync("UPDATE ord_medical_orders SET approval_status = 'APPROVED' WHERE id = @Id", new { Id = order.Id });
			}
		}
		await RecordAuditAsync("ORD_CREATE", order, $"住院医嘱开立：{order.ItemName} {order.Dosage} {order.FrequencyCode}");
		return ApiResponse<OrdMedicalOrder>.Success(order, "医嘱开立成功，等待病区护士校对");
	}

	[HttpPost("{orderId}/verify")]
	public async Task<ApiResponse<OrdMedicalOrder>> VerifyOrder(string orderId, [FromBody] VerifyOrderRequestDto req)
	{
		var authError = RequireRoles("ROLE-WARD-NURSE", "WARD_NURSE");
		if (authError != null)
			return ApiResponse<OrdMedicalOrder>.Fail(authError.Value.Status, authError.Value.Message);
		OrdMedicalOrder order = await _orderRepo.GetByIdAsync(orderId);
		if (order != null && string.Equals(order.ApprovalStatus, "PENDING_APPROVAL", StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdMedicalOrder>.Fail(409, $"医嘱 [{order.OrderNo}] 尚未通过特殊药品/越权审批流，禁止护士校对执行");
		if (order != null && string.Equals(order.ApprovalStatus, "REJECTED", StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdMedicalOrder>.Fail(409, $"医嘱 [{order.OrderNo}] 审批已被驳回，禁止校对执行");
		if (order == null)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(404, "医嘱不存在");
		}
		if (order.Status != 0)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(409, "医嘱已校对、停止或作废，禁止重复校对分解");
		}
		if (!string.IsNullOrWhiteSpace(order.ApprovalStatus) && !string.Equals(order.ApprovalStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
		{
			return ApiResponse<OrdMedicalOrder>.Fail(409, $"特殊级药品/越权医嘱尚未审批通过 (当前状态: {order.ApprovalStatus})，护士禁止校对执行！");
		}
		if (!IsAdmin() && !string.Equals(req.VerifyNurseId, _session!.UserId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdMedicalOrder>.Fail(403, "校对护士必须为当前登录护士");
		var wardId = string.IsNullOrWhiteSpace(req.WardId) ? _session?.ActiveWardId : req.WardId.Trim();
		if (!IsAdmin() && !string.Equals(wardId, order.PatientWardId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdMedicalOrder>.Fail(403, "只能校对本责任病区患者的住院医嘱");
		var nurseId = IsAdmin() ? (!string.IsNullOrWhiteSpace(req.VerifyNurseId) ? req.VerifyNurseId : _session!.UserId) : _session!.UserId;

		IReadOnlyList<long> plannedTimestampsMs = await ResolvePlannedTimestampsMsAsync(order.FrequencyCode, order.OrderTermType);
		await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			await using var txConn = _connFactory.CreateConnection();
			var affected = await txConn.ExecuteAsync(new CommandDefinition(
				"UPDATE ord_medical_orders SET status = 1, verify_nurse_id = @NurseId, updated_at_ms = @Now, updated_by = @NurseId, version_no = version_no + 1 WHERE id = @Id AND status = 0",
				new { Id = order.Id, NurseId = nurseId, Now = SystemTime.UtcNowMs }));
			if (affected != 1)
				throw new OrderConflictException("医嘱已被其他护士校对，禁止重复分解");
			order.Status = 1;
			order.VerifyNurseId = nurseId;
			if (order.OrderSubType != "DISCHARGE_MED")
			{
				foreach (long plannedMs in plannedTimestampsMs)
				{
					OrdExecutionPlan plan = new OrdExecutionPlan
					{
						OrderId = order.Id,
						EncounterId = order.EncounterId,
						WardId = wardId,
						PlannedTimeMs = plannedMs,
						ExecStatus = 0
					};
					await _planRepo.InsertAsync(plan, ct);
				}
			}

			// Group cascading verify: verify sibling orders in the same group atomically
			if (order.GroupNo > 0)
			{
				var siblings = (await _orderRepo.QueryAsync(
					"SELECT * FROM ord_medical_orders WHERE encounter_id = @EncId AND group_no = @Grp AND order_term_type = @Term AND status = 0 AND id != @Id",
					new { EncId = order.EncounterId, Grp = order.GroupNo, Term = order.OrderTermType, Id = order.Id }, ct)).ToList();

				foreach (var sib in siblings)
				{
					await txConn.ExecuteAsync(new CommandDefinition(
						"UPDATE ord_medical_orders SET status = 1, verify_nurse_id = @NurseId, updated_at_ms = @Now, updated_by = @NurseId, version_no = version_no + 1 WHERE id = @Id AND status = 0",
						new { Id = sib.Id, NurseId = nurseId, Now = SystemTime.UtcNowMs }));
					if (sib.OrderSubType != "DISCHARGE_MED")
					{
						var sibPlans = await ResolvePlannedTimestampsMsAsync(sib.FrequencyCode, sib.OrderTermType);
						foreach (long plannedMs in sibPlans)
						{
							OrdExecutionPlan plan = new OrdExecutionPlan
							{
								OrderId = sib.Id,
								EncounterId = sib.EncounterId,
								WardId = wardId,
								PlannedTimeMs = plannedMs,
								ExecStatus = 0
							};
							await _planRepo.InsertAsync(plan, ct);
						}
					}
					else
					{
						await EnsureDischargeMedicationSyncedAsync(txConn, sib, nurseId);
					}
				}
			}
			if (order.OrderSubType == "DISCHARGE_MED")
			{
				await EnsureDischargeMedicationSyncedAsync(txConn, order, nurseId);
			}
		});
		await RecordAuditAsync("ORD_VERIFY", order, $"病区护士校对并分解 {(order.OrderSubType == "DISCHARGE_MED" ? 0 : plannedTimestampsMs.Count)} 条执行计划");
		return ApiResponse<OrdMedicalOrder>.Success(order, $"病区护士校对成功，已按频次 [{order.FrequencyCode}] 自动联动处理成组医嘱执行计划");
	}

	private static async Task EnsureDischargeMedicationSyncedAsync(System.Data.Common.DbConnection conn, OrdMedicalOrder order, string actorId)
	{
		long now = SystemTime.UtcNowMs;
		string patName = await conn.QuerySingleOrDefaultAsync<string>(
			"SELECT COALESCE(full_name, @PatId) FROM pat_patients WHERE id = @PatId", new { PatId = order.PatientId }) ?? order.PatientId;
		decimal unitPrice = await conn.QuerySingleOrDefaultAsync<decimal>(
			"SELECT COALESCE(price, 0) FROM inv_drugs WHERE id = @Code OR drug_code = @Code LIMIT 1", new { Code = order.ItemCode });
		string docName = await conn.QuerySingleOrDefaultAsync<string>(
			"SELECT COALESCE(display_name, @DocId) FROM sys_users WHERE id = @DocId", new { DocId = order.OrderDoctorId }) ?? order.OrderDoctorId;

		await conn.ExecuteAsync("""
			INSERT OR IGNORE INTO ipd_discharge_medications
			    (id, rx_no, encounter_id, patient_name, medications_summary, total_amount, prescribed_by,
			     status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    (@Id, @RxNo, @EncId, @PatName, @Summary, @Amt, @DocName,
			     'PRESCRIBED', 1, @Now, @Actor, @Now, @Actor, 1)
			""", new
		{
			Id = order.Id,
			RxNo = order.OrderNo,
			EncId = order.EncounterId,
			PatName = patName,
			Summary = $"{order.ItemName} × {order.Dosage} ({order.FrequencyCode}/{order.RouteCode})",
			Amt = Math.Round(unitPrice * Math.Max(1m, order.Dosage), 2),
			DocName = docName,
			Now = now,
			Actor = actorId
		});
	}

	[HttpPost("group")]
	public async Task<ApiResponse<object>> AssignOrderGroup([FromBody] AssignGroupRequestDto req)
	{
		var authError = RequireRoles("ROLE-IPD-RESIDENT", "ROLE-IPD-ATTENDING", "IPD_RESIDENT", "IPD_ATTENDING");
		if (authError != null)
			return ApiResponse<object>.Fail(authError.Value.Status, authError.Value.Message);
		if (req?.OrderIds == null || req.OrderIds.Count < 2)
			return ApiResponse<object>.Fail(400, "成组医嘱至少需要勾选 2 条医嘱");

		await using var conn = _connFactory.CreateConnection();
		await conn.OpenAsync();
		var ids = req.OrderIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
		var orders = (await conn.QueryAsync<OrdMedicalOrder>(
			"SELECT * FROM ord_medical_orders WHERE id IN @Ids", new { Ids = ids })).ToList();
		if (orders.Count != ids.Count)
			return ApiResponse<object>.Fail(404, "部分选中医嘱不存在于数据库");
		var encId = orders[0].EncounterId;
		if (orders.Any(o => o.EncounterId != encId))
			return ApiResponse<object>.Fail(400, "仅同一次住院的医嘱允许成组");
		int groupNo = req.GroupNo is > 0 ? req.GroupNo.Value : (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000);
		string parentId = string.IsNullOrWhiteSpace(req.ParentOrderId) ? orders[0].Id : req.ParentOrderId.Trim();
		long now = SystemTime.UtcNowMs;
		string actor = _session?.UserId ?? "SYSTEM";
		await conn.ExecuteAsync(
			"UPDATE ord_medical_orders SET group_no = @Grp, parent_order_id = @ParentId, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id IN @Ids",
			new { Grp = groupNo, ParentId = parentId, Now = now, Actor = actor, Ids = ids });
		return ApiResponse<object>.Success(new { GroupNo = groupNo, ParentOrderId = parentId, Count = ids.Count }, $"已完成 {ids.Count} 条医嘱服务端持久化成组 (组号 {groupNo})");
	}

	[HttpPost("ungroup")]
	public async Task<ApiResponse<object>> UngroupOrders([FromBody] AssignGroupRequestDto req)
	{
		var authError = RequireRoles("ROLE-IPD-RESIDENT", "ROLE-IPD-ATTENDING", "IPD_RESIDENT", "IPD_ATTENDING");
		if (authError != null)
			return ApiResponse<object>.Fail(authError.Value.Status, authError.Value.Message);
		if (req?.OrderIds == null || req.OrderIds.Count == 0)
			return ApiResponse<object>.Fail(400, "请选择要解组的医嘱");
		await using var conn = _connFactory.CreateConnection();
		await conn.OpenAsync();
		var ids = req.OrderIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
		var orders = (await conn.QueryAsync<OrdMedicalOrder>(
			"SELECT * FROM ord_medical_orders WHERE id IN @Ids", new { Ids = ids })).ToList();
		if (orders.Count != ids.Count)
			return ApiResponse<object>.Fail(404, "部分选中医嘱不存在于数据库");
		var encId = orders[0].EncounterId;
		if (orders.Any(o => !string.Equals(o.EncounterId, encId, StringComparison.OrdinalIgnoreCase)))
			return ApiResponse<object>.Fail(400, "仅同一次住院的医嘱允许解组");
		if (orders.Any(o => o.Status != 0))
			return ApiResponse<object>.Fail(409, "仅待校对状态医嘱允许解除成组，已校对或执行中的医嘱禁止解组");
		long now = SystemTime.UtcNowMs;
		string actor = _session?.UserId ?? "SYSTEM";
		await conn.ExecuteAsync(
			"UPDATE ord_medical_orders SET group_no = 0, parent_order_id = NULL, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id IN @Ids AND status = 0",
			new { Now = now, Actor = actor, Ids = ids });
		return ApiResponse<object>.Success(new { Count = ids.Count }, $"已解除 {ids.Count} 条医嘱的成组关联");
	}

	[HttpPost("{orderId}/stop")]
	public async Task<ApiResponse<OrdMedicalOrder>> StopOrder(string orderId, [FromQuery] string doctorId)
	{
		var authError = RequireRoles("ROLE-IPD-RESIDENT", "ROLE-IPD-ATTENDING", "IPD_RESIDENT", "IPD_ATTENDING");
		if (authError != null)
			return ApiResponse<OrdMedicalOrder>.Fail(authError.Value.Status, authError.Value.Message);
		OrdMedicalOrder order = await _orderRepo.GetByIdAsync(orderId);
		if (order == null)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(404, "医嘱不存在");
		}
		if (order.Status == 4)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(400, "已作废的医嘱不能执行停止操作");
		}
		bool isCreator = string.Equals(order.OrderDoctorId, doctorId, StringComparison.OrdinalIgnoreCase);
		bool isSameDept = !string.IsNullOrWhiteSpace(_session?.ActiveDeptId) &&
		                  string.Equals(order.OrderIpdDeptId, _session.ActiveDeptId, StringComparison.OrdinalIgnoreCase);
		bool isSameWard = !string.IsNullOrWhiteSpace(_session?.ActiveWardId) &&
		                  string.Equals(order.PatientWardId, _session.ActiveWardId, StringComparison.OrdinalIgnoreCase);
		if (!IsAdmin() && !isCreator && !isSameDept && !isSameWard)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(403, "只能由本医嘱开立医师、同科室值班医师或同病区医师执行停止操作");
		}
		long nowMs = SystemTime.UtcNowMs;
		bool cascadeGroup = order.GroupNo > 0;
		await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			await using var conn = _connFactory.CreateConnection();
			// Cascade stop all orders in the group ONLY when group_no > 0; ungrouped (group_no == 0) stops only this order
			int affected = cascadeGroup
				? await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE ord_medical_orders
					SET status = 3, plan_stop_at_ms = @Now, actual_stop_at_ms = @Now, stop_doctor_id = @DoctorId,
					    updated_at_ms = @Now, updated_by = @DoctorId, version_no = version_no + 1
					WHERE encounter_id = @EncId AND group_no = @Grp AND order_term_type = @Term AND status IN (0, 1, 2)
					""",
					new { EncId = order.EncounterId, Grp = order.GroupNo, Term = order.OrderTermType, Now = nowMs, DoctorId = doctorId }, cancellationToken: ct))
				: await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE ord_medical_orders
					SET status = 3, plan_stop_at_ms = @Now, actual_stop_at_ms = @Now, stop_doctor_id = @DoctorId,
					    updated_at_ms = @Now, updated_by = @DoctorId, version_no = version_no + 1
					WHERE id = @OrderId AND status IN (0, 1, 2)
					""",
					new { OrderId = order.Id, Now = nowMs, DoctorId = doctorId }, cancellationToken: ct));
			if (affected == 0)
				throw new OrderConflictException("医嘱状态已变更，停止失败");
			order.Status = 3;
			order.PlanStopAtMs = nowMs;
			order.ActualStopAtMs = nowMs;
			order.StopDoctorId = doctorId;
			order.UpdatedBy = doctorId;
			if (cascadeGroup)
			{
				await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE ord_execution_plans
					SET exec_status = 2, updated_at_ms = @Now, version_no = version_no + 1
					WHERE order_id IN (
					    SELECT id FROM ord_medical_orders
					    WHERE encounter_id = @EncId AND group_no = @Grp AND order_term_type = @Term
					) AND exec_status = 0
					""",
					new { EncId = order.EncounterId, Grp = order.GroupNo, Term = order.OrderTermType, Now = nowMs }, cancellationToken: ct));
			}
			else
			{
				await conn.ExecuteAsync(new CommandDefinition(
					"UPDATE ord_execution_plans SET exec_status = 2, updated_at_ms = @Now, version_no = version_no + 1 WHERE order_id = @OrderId AND exec_status = 0",
					new { OrderId = order.Id, Now = nowMs }, cancellationToken: ct));
			}
		});
		await RecordAuditAsync("ORD_STOP", order, $"医师下达停嘱指令，{(cascadeGroup ? "同组医嘱及" : "本条医嘱")}未执行护理分解计划已撤销");
		return ApiResponse<OrdMedicalOrder>.Success(order, "医嘱已下达停止指令，后续未执行的护理分解计划已自动撤销");
	}

	[HttpPost("{orderId}/discontinue")]
	[HttpPost("{orderId}/void")]
	public async Task<ApiResponse<OrdMedicalOrder>> DiscontinueOrder(string orderId, [FromQuery] string doctorId, [FromQuery] string? reason = null)
	{
		var authError = RequireRoles("ROLE-IPD-RESIDENT", "ROLE-IPD-ATTENDING", "IPD_RESIDENT", "IPD_ATTENDING");
		if (authError != null)
			return ApiResponse<OrdMedicalOrder>.Fail(authError.Value.Status, authError.Value.Message);
		OrdMedicalOrder order = await _orderRepo.GetByIdAsync(orderId);
		if (order == null)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(404, "医嘱不存在");
		}
		bool isCreator = string.Equals(order.OrderDoctorId, doctorId, StringComparison.OrdinalIgnoreCase);
		bool isSameDept = !string.IsNullOrWhiteSpace(_session?.ActiveDeptId) &&
		                  string.Equals(order.OrderIpdDeptId, _session.ActiveDeptId, StringComparison.OrdinalIgnoreCase);
		bool isSameWard = !string.IsNullOrWhiteSpace(_session?.ActiveWardId) &&
		                  string.Equals(order.PatientWardId, _session.ActiveWardId, StringComparison.OrdinalIgnoreCase);
		if (!IsAdmin() && !isCreator && !isSameDept && !isSameWard)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(403, "只能由本医嘱开立医师、同科室值班医师或同病区医师作废医嘱");
		}

		long nowMs = SystemTime.UtcNowMs;
		var voidReason = string.IsNullOrWhiteSpace(reason) ? "医生作废医嘱" : reason.Trim();
		bool cascadeGroup = order.GroupNo > 0;
		string? preCheckError = null;
		await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			await using var conn = _connFactory.CreateConnection();
			int executedCount = cascadeGroup
				? await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"""
					SELECT count(*) FROM ord_execution_plans
					WHERE order_id IN (
					    SELECT id FROM ord_medical_orders
					    WHERE encounter_id = @EncId AND group_no = @Grp AND order_term_type = @Term
					) AND exec_status = 1
					""",
					new { EncId = order.EncounterId, Grp = order.GroupNo, Term = order.OrderTermType }, cancellationToken: ct))
				: await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"SELECT count(*) FROM ord_execution_plans WHERE order_id = @OrderId AND exec_status = 1",
					new { OrderId = order.Id }, cancellationToken: ct));
			if (executedCount > 0)
			{
				preCheckError = "该组医嘱已有床旁执行记录，严禁作废！请走“停止医嘱”流程。";
				return;
			}

			int affected = cascadeGroup
				? await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE ord_medical_orders
					SET status = 4, plan_stop_at_ms = @Now, actual_stop_at_ms = @Now, stop_doctor_id = @DoctorId,
					    void_doctor_id = @DoctorId, void_reason = @Reason, void_at_ms = @Now,
					    updated_at_ms = @Now, updated_by = @DoctorId, version_no = version_no + 1
					WHERE encounter_id = @EncId AND group_no = @Grp AND order_term_type = @Term AND status IN (0, 1, 2)
					""",
					new { EncId = order.EncounterId, Grp = order.GroupNo, Term = order.OrderTermType, Now = nowMs, DoctorId = doctorId, Reason = voidReason }, cancellationToken: ct))
				: await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE ord_medical_orders
					SET status = 4, plan_stop_at_ms = @Now, actual_stop_at_ms = @Now, stop_doctor_id = @DoctorId,
					    void_doctor_id = @DoctorId, void_reason = @Reason, void_at_ms = @Now,
					    updated_at_ms = @Now, updated_by = @DoctorId, version_no = version_no + 1
					WHERE id = @OrderId AND status IN (0, 1, 2)
					""",
					new { OrderId = order.Id, Now = nowMs, DoctorId = doctorId, Reason = voidReason }, cancellationToken: ct));
			if (affected == 0)
				throw new OrderConflictException("医嘱已执行、停止或作废，无法作废");
			order.Status = 4;
			order.PlanStopAtMs = nowMs;
			order.ActualStopAtMs = nowMs;
			order.StopDoctorId = doctorId;
			order.VoidDoctorId = doctorId;
			order.VoidReason = voidReason;
			order.VoidAtMs = nowMs;
			order.UpdatedBy = doctorId;
			if (cascadeGroup)
			{
				await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE ord_execution_plans
					SET exec_status = 2, updated_at_ms = @Now, version_no = version_no + 1
					WHERE order_id IN (
					    SELECT id FROM ord_medical_orders
					    WHERE encounter_id = @EncId AND group_no = @Grp AND order_term_type = @Term
					) AND exec_status = 0
					""",
					new { EncId = order.EncounterId, Grp = order.GroupNo, Term = order.OrderTermType, Now = nowMs }, cancellationToken: ct));
			}
			else
			{
				await conn.ExecuteAsync(new CommandDefinition(
					"UPDATE ord_execution_plans SET exec_status = 2, updated_at_ms = @Now, version_no = version_no + 1 WHERE order_id = @OrderId AND exec_status = 0",
					new { OrderId = order.Id, Now = nowMs }, cancellationToken: ct));
			}
		});
		if (preCheckError != null)
		{
			return ApiResponse<OrdMedicalOrder>.Fail(400, preCheckError);
		}
		await RecordAuditAsync("ORD_DISCONTINUE", order, $"未经床旁执行的{(cascadeGroup ? "成组" : "")}医嘱已作废：{voidReason}");
		return ApiResponse<OrdMedicalOrder>.Success(order, "医嘱已作废，关联待执行计划已全部撤销");
	}

	[HttpPost("plans/pda-execute")]
	public async Task<ApiResponse<OrdExecutionPlan>> PdaExecutePlan([FromBody] PdaExecuteRequestDto req)
	{
		var authError = RequireRoles("ROLE-WARD-NURSE", "WARD_NURSE");
		if (authError != null)
			return ApiResponse<OrdExecutionPlan>.Fail(authError.Value.Status, authError.Value.Message);
		if (string.IsNullOrWhiteSpace(req.ExecNurseId) || string.IsNullOrWhiteSpace(req.DualCheckNurseId) ||
			string.IsNullOrWhiteSpace(req.PdaScanToken))
			return ApiResponse<OrdExecutionPlan>.Fail(400, "PDA 执行必须填写执行护士、复核护士及扫码令牌");
		if (!string.Equals(req.ExecNurseId.Trim(), _session!.UserId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdExecutionPlan>.Fail(403, "执行护士必须为当前登录护士");
		if (string.Equals(req.ExecNurseId.Trim(), req.DualCheckNurseId.Trim(), StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdExecutionPlan>.Fail(400, "执行护士与复核护士必须为两名不同人员");

		OrdExecutionPlan plan = await _planRepo.GetByIdAsync(req.PlanId);
		if (plan == null)
		{
			return ApiResponse<OrdExecutionPlan>.Fail(404, "医嘱执行计划不存在");
		}
		if (plan.ExecStatus == 1)
		{
			return ApiResponse<OrdExecutionPlan>.Fail(409, "该执行计划已完成扫码执行，请勿重复执行");
		}
		if (plan.ExecStatus == 2)
		{
			return ApiResponse<OrdExecutionPlan>.Fail(400, "该执行计划已随医嘱停止/作废而撤销，禁止执行");
		}
		OrdMedicalOrder? order = await _orderRepo.GetByIdAsync(plan.OrderId);
		if (order == null || order.Status is not (1 or 2))
			return ApiResponse<OrdExecutionPlan>.Fail(409, "医嘱未校对或已停止，禁止床旁执行");
		if (!string.Equals(plan.WardId, order.PatientWardId, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(order.PatientWardId, _session.ActiveWardId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<OrdExecutionPlan>.Fail(403, "只能执行本责任病区患者的医嘱");

		// Level 7: Dual-Barcode Hard Verification (Wristband + Drug / Specimen)
		if (!string.IsNullOrWhiteSpace(req.PatientWristbandCode))
		{
			var wristband = req.PatientWristbandCode.Trim();
			await using var connCheck = _connFactory.CreateConnection();
			var matchPatient = await connCheck.ExecuteScalarAsync<int>(new CommandDefinition(
				"""
				SELECT count(*) FROM pat_patients p
				LEFT JOIN ipd_encounters e ON e.patient_id = p.id
				WHERE (p.id = @PatientId OR e.id = @EncounterId)
				  AND (p.id = @Wristband OR p.medical_record_no = @Wristband OR e.admission_no = @Wristband
				       OR @Wristband = ('WB_' || p.medical_record_no) OR @Wristband = ('WB_' || e.admission_no))
				""",
				new { PatientId = order.PatientId, EncounterId = order.EncounterId, Wristband = wristband }));
			if (matchPatient == 0 && !wristband.Contains(order.PatientId, StringComparison.OrdinalIgnoreCase))
			{
				await RecordAuditAsync("PDA_CHECK_MISMATCH", order, $"【核验阻断】腕带条码 [{wristband}] 与当前患者不匹配！");
				return ApiResponse<OrdExecutionPlan>.Fail(409, $"【核验阻断】患者腕带条码 [{wristband}] 与当前医嘱患者不一致，禁止床旁执行！");
			}
		}

		if (!string.IsNullOrWhiteSpace(req.ItemBarcode))
		{
			var barcode = req.ItemBarcode.Trim();
			await using var connCheck = _connFactory.CreateConnection();
			var unitDose = await connCheck.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
				"SELECT patient_id, medical_order_id FROM phm_unit_dose_barcodes WHERE barcode = @Barcode",
				new { Barcode = barcode }));
			if (unitDose != null)
			{
				string? pId = (string?)unitDose.patient_id;
				if (!string.IsNullOrWhiteSpace(pId) && !string.Equals(pId, order.PatientId, StringComparison.OrdinalIgnoreCase))
				{
					await RecordAuditAsync("PDA_CHECK_MISMATCH", order, $"【核验阻断】药品单剂量条码 [{barcode}] 属于其他患者！");
					return ApiResponse<OrdExecutionPlan>.Fail(409, $"【核验阻断】药品单剂量条码 [{barcode}] 属于其他患者，存在严重给药差错风险，严禁执行！");
				}
			}
		}

		long nowMs = SystemTime.UtcNowMs;
		await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			await using var conn = _connFactory.CreateConnection();
			var affected = await conn.ExecuteAsync(new CommandDefinition(
				"""
				UPDATE ord_execution_plans
				SET exec_status = 1, exec_nurse_id = @ExecNurseId, dual_check_nurse_id = @DualCheckNurseId,
				    executed_at_ms = @Now, pda_scan_token = @Token,
				    patient_wristband_code = @Wristband, item_barcode = @ItemBarcode, scan_matched = 1,
				    updated_at_ms = @Now, updated_by = @ExecNurseId, version_no = version_no + 1
				WHERE id = @PlanId AND exec_status = 0
				""",
				new
				{
					PlanId = plan.Id,
					ExecNurseId = _session.UserId,
					DualCheckNurseId = req.DualCheckNurseId.Trim(),
					Now = nowMs,
					Token = req.PdaScanToken.Trim(),
					Wristband = req.PatientWristbandCode?.Trim() ?? string.Empty,
					ItemBarcode = req.ItemBarcode?.Trim() ?? string.Empty
				}));
			if (affected != 1)
				throw new OrderConflictException("该执行计划已被其他护士执行或已撤销");
			plan.ExecStatus = 1;
			plan.ExecNurseId = _session.UserId;
			plan.DualCheckNurseId = req.DualCheckNurseId.Trim();
			plan.ExecutedAtMs = nowMs;
			plan.PdaScanToken = req.PdaScanToken.Trim();
			plan.PatientWristbandCode = req.PatientWristbandCode?.Trim();
			plan.ItemBarcode = req.ItemBarcode?.Trim();
			plan.ScanMatched = 1;

			var remaining = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
				"SELECT count(*) FROM ord_execution_plans WHERE order_id = @OrderId AND exec_status = 0",
				new { OrderId = order.Id }));
			if (remaining == 0 && order.OrderTermType == 2)
			{
				await conn.ExecuteAsync(new CommandDefinition(
					"UPDATE ord_medical_orders SET status = 2, updated_at_ms = @Now, version_no = version_no + 1 WHERE id = @Id AND status = 1",
					new { Id = order.Id, Now = nowMs }));
				order.Status = 2;
			}
		});

		if (_signatureContract != null && _session != null)
		{
			try
			{
				await _signatureContract.SignClinicalRecordAsync("ORDER_PLAN", plan.Id, "EXECUTE", plan, _session, default);
			}
			catch { /* Dev trace */ }
		}

		if (_alertPublisher != null)
		{
			await _alertPublisher.PublishOrderChangedAsync(order.EncounterId, order.PatientWardId, order.Id, "EXECUTED");
		}

		await RecordAuditAsync("ORD_PDA_EXECUTE", order, $"PDA 双人扫码执行计划 {plan.Id}");
		return ApiResponse<OrdExecutionPlan>.Success(plan, "PDA 床旁双人双码核对执行完成");
	}

	[HttpGet("plans/{planId}/pre-exec-context")]
	public async Task<ApiResponse<object>> GetPreExecContext(string planId)
	{
		var plan = await _planRepo.GetByIdAsync(planId);
		if (plan == null) return ApiResponse<object>.Fail(404, "执行计划不存在");
		var order = await _orderRepo.GetByIdAsync(plan.OrderId);
		if (order == null) return ApiResponse<object>.Fail(404, "医嘱不存在");

		await using var conn = _connFactory.CreateConnection();
		var patient = await LoadPatientSummaryAsync(conn, order.PatientId);
		var encounter = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
			"SELECT admission_no, bed_code, admit_diagnosis FROM ipd_encounters WHERE id = @Id", new { Id = order.EncounterId }));

		var labItems = await conn.QueryAsync<dynamic>(new CommandDefinition(
			"""
			SELECT item_code, item_name, value_num, value_text, unit, abnormal_flag, reported_at_ms
			FROM mtc_lab_result_items
			WHERE patient_id = @PatientId
			ORDER BY reported_at_ms DESC LIMIT 10
			""",
			new { PatientId = order.PatientId }));

		var vitals = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
			"""
			SELECT temperature_c, pulse_bpm, heart_rate_bpm, resp_rate, sys_bp, dia_bp, spo2, blood_glucose, mews_score
			FROM nur_vital_signs
			WHERE patient_id = @PatientId
			ORDER BY measured_at_ms DESC LIMIT 1
			""",
			new { PatientId = order.PatientId }));

		var isHighAlert = false;
		string? highAlertGrade = null;
		var drug = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
			"SELECT is_high_alert, high_alert_grade FROM inv_drugs WHERE drug_code = @Code",
			new { Code = order.ItemCode }));
		if (drug != null)
		{
			isHighAlert = drug.is_high_alert == 1;
			highAlertGrade = (string?)drug.high_alert_grade;
		}

		return ApiResponse<object>.Success(new
		{
			PlanId = plan.Id,
			OrderId = order.Id,
			ItemName = order.ItemName,
			Dosage = order.Dosage,
			RouteCode = order.RouteCode,
			FrequencyCode = order.FrequencyCode,
			Patient = patient,
			AdmissionNo = (string?)encounter?.admission_no,
			BedCode = (string?)encounter?.bed_code,
			AdmitDiagnosis = (string?)encounter?.admit_diagnosis,
			IsHighAlert = isHighAlert,
			HighAlertGrade = highAlertGrade,
			RecentLabs = labItems,
			LatestVitals = vitals
		});
	}

	private (int Status, string Message)? RequireRoles(params string[] allowed)
	{
		if (_session == null || string.IsNullOrWhiteSpace(_session.UserId))
			return (401, "请先登录并获得住院医嘱权限");
		if (IsAdmin())
			return null;
		if (RoleCode.HasAnyRole(_session.Roles, allowed))
			return null;
		return (403, "当前登录角色无权执行该住院医嘱操作");
	}

	private bool IsAdmin() =>
		_session != null &&
		(RoleCode.IsAdmin(_session.RoleCode) || RoleCode.HasRole(_session.Roles, RoleCode.Admin));

	private async Task RecordAuditAsync(string eventType, OrdMedicalOrder order, string detail)
	{
		if (_auditTrail != null)
			await _auditTrail.RecordAuditAsync(eventType, nameof(OrdMedicalOrder), order.Id, detail,
				_session?.UserId, _session?.DisplayName, _session?.ActiveDeptId);
	}

	private static async Task<PatientSummaryDto?> LoadPatientSummaryAsync(System.Data.Common.DbConnection conn, string patientId)
	{
		var row = await conn.QuerySingleOrDefaultAsync<OrderPatientRow>(new CommandDefinition(
			"SELECT id, medical_record_no, full_name, gender, allergy_tags_json, is_active FROM pat_patients WHERE id = @Id",
			new { Id = patientId }));
		if (row == null)
			return null;
		List<string> allergies;
		try
		{
			allergies = string.IsNullOrWhiteSpace(row.AllergyTagsJson)
				? new List<string>()
				: System.Text.Json.JsonSerializer.Deserialize<List<string>>(row.AllergyTagsJson) ?? new List<string>();
		}
		catch (System.Text.Json.JsonException)
		{
			allergies = new List<string> { row.AllergyTagsJson };
		}
		return new PatientSummaryDto(row.Id, row.MedicalRecordNo ?? "", row.FullName ?? "", row.Gender ?? "",
			0, 0, "", allergies, false, row.IsActive == 1);
	}

	private static async Task<DrugCatalogSnapshotDto?> LoadDrugSnapshotAsync(System.Data.Common.DbConnection conn, string drugIdOrCode)
	{
		var row = await conn.QuerySingleOrDefaultAsync<OrderDrugRow>(new CommandDefinition(
			"""
			SELECT id, drug_code, drug_name, spec, unit_name, price, antibiotic_level, is_narcotic,
			       is_skin_test_required, is_active
			FROM inv_drugs WHERE id = @Key OR drug_code = @Key LIMIT 1
			""",
			new { Key = drugIdOrCode }));
		return row == null ? null : new DrugCatalogSnapshotDto(
			row.Id, row.DrugCode, row.DrugName, row.Spec ?? "", row.UnitName ?? "",
			row.Price, row.AntibioticLevel, row.IsNarcotic == 1, row.IsSkinTestRequired == 1, row.IsActive == 1);
	}

	private sealed class OrderEncounterRow
	{
		public string Id { get; set; } = "";
		public string PatientId { get; set; } = "";
		public string AdmitDeptId { get; set; } = "";
		public string AdmitWardId { get; set; } = "";
		public decimal PrepaymentBalance { get; set; }
		public int Status { get; set; }
		public int TriageStatus { get; set; }
	}

	private sealed class OrderPatientRow
	{
		public string Id { get; set; } = "";
		public string? MedicalRecordNo { get; set; }
		public string? FullName { get; set; }
		public string? Gender { get; set; }
		public string? AllergyTagsJson { get; set; }
		public short IsActive { get; set; }
	}

	private sealed class OrderDrugRow
	{
		public string Id { get; set; } = "";
		public string DrugCode { get; set; } = "";
		public string DrugName { get; set; } = "";
		public string? Spec { get; set; }
		public string? UnitName { get; set; }
		public decimal Price { get; set; }
		public int AntibioticLevel { get; set; }
		public short IsNarcotic { get; set; }
		public short IsSkinTestRequired { get; set; }
		public short IsActive { get; set; }
	}

	private sealed class OrderConflictException(string message) : Exception(message);

	private async Task<IReadOnlyList<long>> ResolvePlannedTimestampsMsAsync(string? frequencyCode, int orderTermType)
	{
		DateTimeOffset baseShanghaiDate = SystemTime.ShanghaiNow.Date;
		if (_masterData != null && !string.IsNullOrWhiteSpace(frequencyCode))
		{
			OrderFrequencySnapshotDto freqDef = await _masterData.GetOrderFrequencyAsync(frequencyCode);
			if (freqDef != null && freqDef.ParsedTimePoints.Count > 0)
			{
				List<long> parsedMs = new List<long>();
				foreach (string tp in freqDef.ParsedTimePoints)
				{
					if (!string.IsNullOrWhiteSpace(tp) &&
						TimeOnly.TryParseExact(tp.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly))
					{
						parsedMs.Add(new DateTimeOffset(baseShanghaiDate.Date.Add(timeOnly.ToTimeSpan()), SystemTime.ShanghaiOffset).ToUnixTimeMilliseconds());
					}
				}
				if (parsedMs.Count > 0)
				{
					return parsedMs;
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(frequencyCode))
		{
			await using var ruleConn = _connFactory.CreateConnection();
			string? ruleExecTimes = await ruleConn.QuerySingleOrDefaultAsync<string?>(
				"SELECT default_exec_times FROM mdm_medication_rules WHERE UPPER(frequency_code) = UPPER(@Freq) AND is_active = 1 AND default_exec_times IS NOT NULL LIMIT 1",
				new { Freq = frequencyCode.Trim() });
			if (!string.IsNullOrWhiteSpace(ruleExecTimes))
			{
				var ruleMs = ruleExecTimes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Select(tp => TimeOnly.TryParseExact(tp, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : (TimeOnly?)null)
					.Where(t => t.HasValue)
					.Select(t => new DateTimeOffset(baseShanghaiDate.Date.Add(t!.Value.ToTimeSpan()), SystemTime.ShanghaiOffset).ToUnixTimeMilliseconds())
					.ToList();
				if (ruleMs.Count > 0)
					return ruleMs;
			}
		}
		IReadOnlyList<int> offsets = ResolvePlanHourOffsets(frequencyCode, orderTermType);
		DateTimeOffset now = SystemTime.ShanghaiNow;
		return offsets.Select((int h) => now.AddHours(h).ToUnixTimeMilliseconds()).ToList();
	}

	private static IReadOnlyList<int> ResolvePlanHourOffsets(string? frequencyCode, int orderTermType)
	{
		if (orderTermType == 2)
		{
			return new int[1];
		}
		string text = (frequencyCode ?? "QD").Trim().ToUpperInvariant();
		if (text.StartsWith("BID") || text.StartsWith("Q12H"))
		{
			return new int[2] { 2, 10 };
		}
		if (text.StartsWith("TID") || text.StartsWith("Q8H"))
		{
			return new int[3] { 2, 8, 16 };
		}
		if (text.StartsWith("QID") || text.StartsWith("Q6H"))
		{
			return new int[4] { 2, 6, 12, 18 };
		}
		if (text.StartsWith("ST"))
		{
			return new int[1];
		}
		return new int[1] { 2 };
	}
}
