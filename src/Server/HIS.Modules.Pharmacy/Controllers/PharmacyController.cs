using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Pharmacy.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Pharmacy.Controllers;

[ApiController]
[Route("api/v4/pharmacy")]
public class PharmacyController : ControllerBase
{
	public sealed record DispensePrescriptionRequestDto(string PharmacyId, string PrescriptionId, string PatientId, string PharmacistId, string? RequestKey = null);

	public sealed record PharmacyReadyNotificationDto(string DispenseOrderId, string DispenseNo, string PrescriptionId, string PrescriptionNo, string VisitId, string PatientId, string? PatientName, string? MedicalRecordNo, string TargetPharmacyId, decimal TotalAmount, int PrescriptionStatus, int DispenseOrderStatus, string OrderStateText, bool CanDispense, bool IsOrderClosed);

	public sealed record AuthorizedPharmacyDto(string Id, string DeptName, string DeptCategory);

	private sealed class RxItemRow
	{
		public string Id { get; set; } = string.Empty;

		public string DrugId { get; set; } = string.Empty;

		public string DrugNameSnapshot { get; set; } = string.Empty;

		public int TotalQuantity { get; set; }

		public int SkinTestFlag { get; set; }
	}

	private sealed class DispenseLineRow
	{
		public string Id { get; set; } = string.Empty;
		public string DispenseOrderId { get; set; } = string.Empty;
		public string? MedicalOrderId { get; set; }
		public string DrugId { get; set; } = string.Empty;
		public string DrugCode { get; set; } = string.Empty;
		public string DrugName { get; set; } = string.Empty;
		public int Quantity { get; set; }
		public decimal UnitPrice { get; set; }
		public decimal UnitCostPrice { get; set; }
		public long CreatedAtMs { get; set; }
	}

	private sealed class PrescriptionItemSnapshotRow
	{
		public string DrugId { get; set; } = string.Empty;
		public string DrugCode { get; set; } = string.Empty;
		public string DrugName { get; set; } = string.Empty;
		public decimal Price { get; set; }
		public decimal CostPrice { get; set; }
	}

	private sealed class WardOrderRow
	{
		public string Id { get; set; } = string.Empty;
		public string OrderNo { get; set; } = string.Empty;
		public string EncounterId { get; set; } = string.Empty;
		public string PatientId { get; set; } = string.Empty;
		public string ExecTargetDeptId { get; set; } = string.Empty;
		public int OrderTermType { get; set; }
		public string DrugId { get; set; } = string.Empty;
		public string ItemCode { get; set; } = string.Empty;
		public string ItemName { get; set; } = string.Empty;
		public decimal Dosage { get; set; }
		public string FrequencyCode { get; set; } = string.Empty;
		public string RouteCode { get; set; } = string.Empty;
		public long? PlanStartAtMs { get; set; }
		public string AdmissionNo { get; set; } = string.Empty;
		public string Mrn { get; set; } = string.Empty;
		public string PatientName { get; set; } = string.Empty;
		public string WardId { get; set; } = string.Empty;
		public string WardName { get; set; } = string.Empty;
		public decimal UnitDosage { get; set; }
		public string UnitName { get; set; } = string.Empty;
		public int ClaimedQuantity { get; set; }
	}

	public sealed record WardReadyOrderDto(
		string MedicalOrderId, string OrderNo, string EncounterId, string AdmissionNo,
		string PatientId, string PatientName, string WardId, string WardName,
		string DrugId, string DrugCode, string DrugName, decimal Dosage,
		string FrequencyCode, string RouteCode, int OrderTermType, string TargetPharmacyId,
		long? PlanStartAtMs);

	public sealed record WardDispenseLineRequestDto(string MedicalOrderId, int Quantity);

	public sealed record PharmacyPrescriptionItemDto(
		string Id, string PrescriptionId, int GroupNo, string DrugId,
		string DrugNameSnapshot, decimal DoseOnce, string DoseUnit,
		string FrequencyCode, string RouteCode, int Days, int TotalQuantity,
		decimal UnitPrice, int SkinTestFlag);

	public sealed record WardDispenseRequestDto(string PharmacyId, string EncounterId,
		IReadOnlyList<WardDispenseLineRequestDto> Lines, string? RequestKey = null);

	private sealed class RxHeaderRow
	{
		public string Id { get; set; } = string.Empty;

		public string PrescriptionNo { get; set; } = string.Empty;

		public string VisitId { get; set; } = string.Empty;

		public string PatientId { get; set; } = string.Empty;

		public string? PatientName { get; set; }

		public string? MedicalRecordNo { get; set; }

		public string TargetPharmacyId { get; set; } = string.Empty;

		public decimal TotalAmount { get; set; }

		public int Status { get; set; }

		public string? PaymentOrderId { get; set; }

		public string? ReviewerPharmacistId { get; set; }
	}

	public sealed record ConfirmDrugReturnRequestDto(string DispenseOrderId, string PharmacistId, string Reason, bool IsPackagingIntact = true, bool IsColdChainQualified = true, string? RequestKey = null);

	private readonly IRepository<PhmDispenseOrder> _dispenseRepo;

	private readonly IDbConnectionFactory _connFactory;

	private readonly IRepository<PhmDispenseOrderLine> _dispenseLineRepo;

	private readonly IPharmacyInventoryContract? _inventoryContract;

	private readonly IAuditTrailContract? _auditTrail;

	private readonly IUserSession? _userSession;

	private readonly IPatientReadOnlyContract? _patientContract;

	private readonly IIdempotencyContract? _idempotency;

	public PharmacyController(
		IRepository<PhmDispenseOrder> dispenseRepo,
		IRepository<PhmDispenseOrderLine> dispenseLineRepo,
		IDbConnectionFactory connFactory,
		IPharmacyInventoryContract? inventoryContract = null,
		IAuditTrailContract? auditTrail = null,
		IUserSession? userSession = null,
		IPatientReadOnlyContract? patientContract = null,
		IIdempotencyContract? idempotency = null)
	{
		_dispenseRepo = dispenseRepo;
		_dispenseLineRepo = dispenseLineRepo;
		_connFactory = connFactory;
		_inventoryContract = inventoryContract;
		_auditTrail = auditTrail;
		_userSession = userSession;
		_patientContract = patientContract;
		_idempotency = idempotency;
	}

	private bool IsPharmacist()
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
			return false;
		if (_userSession.IsAdmin || RoleCode.IsAdmin(_userSession.RoleCode) || RoleCode.HasRole(_userSession.Roles, RoleCode.Admin))
			return true;
		return RoleCode.HasAnyRole(_userSession.Roles, RoleCode.Pharmacist, "PHARMACY", "PHM");
	}

	private bool IsAdminUser()
		=> _userSession != null &&
		   (_userSession.IsAdmin || RoleCode.IsAdmin(_userSession.RoleCode) ||
		    RoleCode.HasRole(_userSession.Roles, RoleCode.Admin));

	private async Task<bool> IsAuthorizedPharmacyAsync(string? pharmacyId, CancellationToken ct = default)
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(pharmacyId))
			return false;
		if (IsAdminUser())
			return true;
		using var conn = _connFactory.CreateConnection();
		return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
			"""
			SELECT count(*) FROM sys_user_dept_posts
			WHERE user_id = @UserId AND dept_id = @DeptId AND dept_category = 'PHARMACY'
			""",
			new { UserId = _userSession.UserId, DeptId = pharmacyId }, cancellationToken: ct)) > 0;
	}

	[HttpGet("authorized-pharmacies")]
	public async Task<ApiResponse<IReadOnlyList<AuthorizedPharmacyDto>>> GetAuthorizedPharmacies()
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
			return ApiResponse<IReadOnlyList<AuthorizedPharmacyDto>>.Fail(401, "登录会话已失效，请重新登录");
		using var conn = _connFactory.CreateConnection();
		var rows = (await conn.QueryAsync<AuthorizedPharmacyDto>(
			"""
			SELECT d.id AS Id, d.dept_name AS DeptName, d.dept_category AS DeptCategory
			FROM sys_departments d
			WHERE d.dept_category = 'PHARMACY' AND d.is_active = 1
			  AND (@IsAdmin = 1 OR EXISTS (
			      SELECT 1 FROM sys_user_dept_posts p
			      WHERE p.user_id = @UserId AND p.dept_id = d.id
			        AND p.dept_category = 'PHARMACY'))
			ORDER BY d.dept_code, d.dept_name
			""",
			new { IsAdmin = IsAdminUser() ? 1 : 0, UserId = _userSession.UserId }))
			.ToList();
		return ApiResponse<IReadOnlyList<AuthorizedPharmacyDto>>.Success(rows);
	}

	private static string NormalizeAllergyToken(string value)
		=> new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

	private static string? FindAllergyConflict(IReadOnlyList<string> allergyTags, string drugName)
	{
		var drugToken = NormalizeAllergyToken(drugName);
		if (drugToken.Length == 0)
			return null;
		foreach (var tag in allergyTags)
		{
			if (string.IsNullOrWhiteSpace(tag))
				continue;
			var tagToken = NormalizeAllergyToken(tag
				.Replace("过敏史", "", StringComparison.OrdinalIgnoreCase)
				.Replace("过敏", "", StringComparison.OrdinalIgnoreCase)
				.Replace("皮试阳性", "", StringComparison.OrdinalIgnoreCase));
			if (tagToken.Length < 2)
				continue;
			if (drugToken.Contains(tagToken, StringComparison.Ordinal) ||
				tagToken.Contains(drugToken, StringComparison.Ordinal) ||
				(tagToken.Contains("penicillin", StringComparison.Ordinal) &&
				 (drugToken.Contains("penicillin", StringComparison.Ordinal) ||
				  drugToken.Contains("amoxicillin", StringComparison.Ordinal))) ||
				(tagToken.Contains("青霉素", StringComparison.Ordinal) &&
				 (drugToken.Contains("青霉素", StringComparison.Ordinal) ||
				  drugToken.Contains("阿莫西林", StringComparison.Ordinal))))
			{
				return tag;
			}
		}
		return null;
	}

	[HttpGet("ready-queue")]
	public async Task<ApiResponse<IReadOnlyList<PharmacyReadyNotificationDto>>> GetReadyToDispenseQueue([FromQuery] string? pharmacyId = null, [FromQuery] bool includeClosed = true)
	{
		if (!IsPharmacist())
			return ApiResponse<IReadOnlyList<PharmacyReadyNotificationDto>>.Fail(403, "仅药师可以查看门诊发药队列");
		if (!string.IsNullOrWhiteSpace(pharmacyId) &&
			!await IsAuthorizedPharmacyAsync(pharmacyId, HttpContext.RequestAborted))
			return ApiResponse<IReadOnlyList<PharmacyReadyNotificationDto>>.Fail(403, "无权查看该药房的门诊发药队列");
		using DbConnection conn = _connFactory.CreateConnection();
		List<RxHeaderRow> prescriptions = (await conn.QueryAsync<RxHeaderRow>(
			"""
			SELECT rx.id AS Id, rx.prescription_no AS PrescriptionNo, rx.visit_id AS VisitId,
			       rx.patient_id AS PatientId, p.full_name AS PatientName,
			       p.medical_record_no AS MedicalRecordNo,
			       rx.target_pharmacy_id AS TargetPharmacyId, rx.total_amount AS TotalAmount,
			       rx.status AS Status
			FROM opd_prescriptions rx
			LEFT JOIN pat_patients p ON p.id = rx.patient_id
			WHERE (@PharmacyId IS NULL OR rx.target_pharmacy_id = @PharmacyId)
			  AND rx.status IN (1, 2, 3, 4, 5, 6)
			ORDER BY rx.updated_at_ms DESC
			""", new
		{
			PharmacyId = pharmacyId
		})).ToList();
		Dictionary<string, PhmDispenseOrder> dispenseOrders = (await _dispenseRepo.QueryAsync("SELECT * FROM phm_dispense_orders WHERE (@PharmacyId IS NULL OR pharmacy_id = @PharmacyId) ORDER BY updated_at_ms DESC", new
		{
			PharmacyId = pharmacyId
		})).Where((PhmDispenseOrder d) => !string.IsNullOrWhiteSpace(d.PrescriptionId)).GroupBy((PhmDispenseOrder d) => d.PrescriptionId, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, PhmDispenseOrder> g) => g.Key, (IGrouping<string, PhmDispenseOrder> g) => g.First(), StringComparer.OrdinalIgnoreCase);

		var skinTestMap = (await conn.QueryAsync<(string rxId, int skinTest)>(
			"SELECT prescription_id AS rxId, skin_test_flag AS skinTest FROM opd_prescription_items WHERE is_deleted = 0 AND skin_test_flag IN (1, 2, 4)"))
			.GroupBy(x => x.rxId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.Max(x => x.skinTest), StringComparer.OrdinalIgnoreCase);

		List<PharmacyReadyNotificationDto> result = new List<PharmacyReadyNotificationDto>();
		foreach (RxHeaderRow rx in prescriptions)
		{
			dispenseOrders.TryGetValue(rx.Id, out var dsp);
			int dspStatus = dsp?.Status ?? ((rx.Status != 3) ? ((rx.Status == 4) ? 1 : (-1)) : 0);
			int status = rx.Status;
			bool flag = (uint)(status - 4) <= 1u;
			bool flag2 = flag;
			bool flag3 = flag2;
			if (!flag3)
			{
				bool flag4 = (uint)(dspStatus - 1) <= 2u;
				flag3 = flag4;
			}
			bool isClosed = flag3;
			if (!(!includeClosed & isClosed))
			{
				bool canDispense = rx.Status == 3 && !isClosed;
				int status2 = rx.Status;
				if (1 == 0)
				{
				}
				string text = status2 switch
				{
					1 => "待收费，收费前禁止发药",
					2 => "审方或皮试阳性锁定，禁止发药",
					3 => "已收费，通知药房待配药",
					4 => "已发药并关闭订单，防止二次发药",
					5 => "已退药入库，允许财务退费",
					6 => "待药师审方，禁止发药",
					_ => "未知状态", 
				};
				if (skinTestMap.TryGetValue(rx.Id, out int stFlag))
				{
					canDispense = false;
					text = (stFlag == 4) ? "皮试阳性(+)强阻断，处方锁定，严禁发药" : "需皮试药品尚未录入阴性结果，禁止发药";
				}
				if (1 == 0)
				{
				}
				result.Add(new PharmacyReadyNotificationDto(OrderStateText: text, DispenseOrderId: dsp?.Id ?? string.Empty, DispenseNo: dsp?.DispenseNo ?? ("WAIT-" + rx.PrescriptionNo), PrescriptionId: rx.Id, PrescriptionNo: rx.PrescriptionNo, VisitId: rx.VisitId, PatientId: rx.PatientId, PatientName: rx.PatientName, MedicalRecordNo: rx.MedicalRecordNo, TargetPharmacyId: rx.TargetPharmacyId, TotalAmount: rx.TotalAmount, PrescriptionStatus: rx.Status, DispenseOrderStatus: dspStatus, CanDispense: canDispense, IsOrderClosed: isClosed));
				dsp = null;
			}
		}
		return ApiResponse<IReadOnlyList<PharmacyReadyNotificationDto>>.Success(result);
	}

	[HttpGet("prescriptions/{prescriptionId}/items")]
	[RequireRole(RoleCode.Pharmacist, "PHARMACY", "PHM")]
	public async Task<ApiResponse<IReadOnlyList<PharmacyPrescriptionItemDto>>> GetDispensePrescriptionItems(
		string prescriptionId, [FromQuery] string? pharmacyId = null)
	{
		if (!IsPharmacist())
			return ApiResponse<IReadOnlyList<PharmacyPrescriptionItemDto>>.Fail(403, "仅药师可以查看待发药处方明细");
		if (string.IsNullOrWhiteSpace(pharmacyId))
			return ApiResponse<IReadOnlyList<PharmacyPrescriptionItemDto>>.Fail(400, "请选择发药药房");
		if (!await IsAuthorizedPharmacyAsync(pharmacyId, HttpContext.RequestAborted))
			return ApiResponse<IReadOnlyList<PharmacyPrescriptionItemDto>>.Fail(403, "无权查看该药房的门诊发药处方");

		using var conn = _connFactory.CreateConnection();
		var prescription = await conn.QuerySingleOrDefaultAsync<RxHeaderRow>(new CommandDefinition(
			"""
			SELECT id AS Id, prescription_no AS PrescriptionNo, visit_id AS VisitId,
			       patient_id AS PatientId, target_pharmacy_id AS TargetPharmacyId,
			       total_amount AS TotalAmount, status AS Status,
			       payment_order_id AS PaymentOrderId,
			       reviewer_pharmacist_id AS ReviewerPharmacistId
			FROM opd_prescriptions WHERE id = @PrescriptionId
			""", new { PrescriptionId = prescriptionId }, cancellationToken: HttpContext.RequestAborted));
		if (prescription == null)
			return ApiResponse<IReadOnlyList<PharmacyPrescriptionItemDto>>.Fail(404, "处方不存在");
		if (!string.Equals(prescription.TargetPharmacyId, pharmacyId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<IReadOnlyList<PharmacyPrescriptionItemDto>>.Fail(403, "处方不属于当前药房");

		var items = await conn.QueryAsync<PharmacyPrescriptionItemDto>(new CommandDefinition(
			"""
			SELECT id AS Id, prescription_id AS PrescriptionId, group_no AS GroupNo,
			       drug_id AS DrugId, drug_name_snapshot AS DrugNameSnapshot,
			       dose_once AS DoseOnce, dose_unit AS DoseUnit,
			       frequency_code AS FrequencyCode, route_code AS RouteCode,
			       days AS Days, total_quantity AS TotalQuantity, unit_price AS UnitPrice,
			       skin_test_flag AS SkinTestFlag
			FROM opd_prescription_items
			WHERE prescription_id = @PrescriptionId AND is_deleted = 0
			ORDER BY group_no, created_at_ms
			""", new { PrescriptionId = prescriptionId },
			cancellationToken: HttpContext.RequestAborted));
		return ApiResponse<IReadOnlyList<PharmacyPrescriptionItemDto>>.Success(items.ToList());
	}

	[HttpPost("dispense")]
	[RequireRole(RoleCode.Pharmacist, "PHARMACY", "PHM")]
	public async Task<ApiResponse<PhmDispenseOrder>> DispensePrescription([FromBody] DispensePrescriptionRequestDto req)
	{
		if (req == null || string.IsNullOrWhiteSpace(req.PrescriptionId))
			return ApiResponse<PhmDispenseOrder>.Fail(400, "发药必须指定处方 ID");
		if (!IsPharmacist())
			return ApiResponse<PhmDispenseOrder>.Fail(403, "仅药师可以完成药房发药");

		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			string idemKey = "PHM_DISPENSE_" + req.RequestKey.Trim();
			var (isDup, cachedJson, isConflict) = await _idempotency.TryAcquireAsync(idemKey, JsonSerializer.Serialize(req));
			if (isConflict)
			{
				return ApiResponse<PhmDispenseOrder>.Fail(409, "请求幂等键冲突：已被不同的发药请求使用");
			}
			if (isDup && !string.IsNullOrWhiteSpace(cachedJson))
			{
				try
				{
					var cached = JsonSerializer.Deserialize<PhmDispenseOrder>(cachedJson);
					if (cached != null)
					{
						return ApiResponse<PhmDispenseOrder>.Success(cached, "幂等命中：已返回原发药单（防重复发药）");
					}
				}
				catch { }
			}
		}

		var pharmacistId = _userSession!.UserId;
		var actorPharmacyId = string.IsNullOrWhiteSpace(req.PharmacyId)
			? _userSession.ActiveDeptId
			: req.PharmacyId.Trim();
		if (string.IsNullOrWhiteSpace(actorPharmacyId))
			return ApiResponse<PhmDispenseOrder>.Fail(403, "请选择发药药房");
		if (!await IsAuthorizedPharmacyAsync(actorPharmacyId, HttpContext.RequestAborted))
			return ApiResponse<PhmDispenseOrder>.Fail(403, "当前账号未被授权在该药房发药");

		var existingCompletedDispense = await _dispenseRepo.QuerySingleOrDefaultAsync(
			"SELECT * FROM phm_dispense_orders WHERE prescription_id = @RxId AND status IN (1, 2, 3) LIMIT 1",
			new { RxId = req.PrescriptionId });
		if (existingCompletedDispense != null)
			return ApiResponse<PhmDispenseOrder>.Fail(409,
				"该处方已完成发药并关闭订单（发药单号：" + existingCompletedDispense.DispenseNo + "），严禁二次重复发药！",
				existingCompletedDispense);

		string dispenseNo = "DSP" + DateTime.UtcNow.ToString("yyMMddHHmmssfff");
		string? outcomeError;
		PhmDispenseOrder? order;
		try
		{
			(outcomeError, order) = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
			{
				long nowMs = SystemTime.UtcNowMs;
				using var conn = _connFactory.CreateConnection();
				var tx = _connFactory.CurrentTransaction;
				var rx = await conn.QuerySingleOrDefaultAsync<RxHeaderRow>(new CommandDefinition(
					"""
					SELECT id AS Id, prescription_no AS PrescriptionNo, visit_id AS VisitId,
					       patient_id AS PatientId, target_pharmacy_id AS TargetPharmacyId,
					       total_amount AS TotalAmount, status AS Status,
					       payment_order_id AS PaymentOrderId,
					       reviewer_pharmacist_id AS ReviewerPharmacistId
					FROM opd_prescriptions WHERE id = @RxId
					""", new { RxId = req.PrescriptionId }, tx, cancellationToken: ct));
				if (rx == null)
					return ("处方不存在", null);
				if (rx.Status != 3)
					return ("该处方尚未完成收费员收费结算，严禁发药", null);
				if (string.IsNullOrWhiteSpace(rx.PaymentOrderId) ||
					string.IsNullOrWhiteSpace(rx.ReviewerPharmacistId))
					return ("处方缺少药师审方或收费单关联，禁止发药", null);
				if (!string.Equals(rx.TargetPharmacyId, actorPharmacyId, StringComparison.OrdinalIgnoreCase))
					return ("登录药房与处方目标药房不一致，禁止发药", null);
				if (!string.IsNullOrWhiteSpace(req.PatientId) &&
					!string.Equals(req.PatientId, rx.PatientId, StringComparison.OrdinalIgnoreCase))
					return ("发药患者与处方患者不一致", null);

				var paymentValid = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"""
					SELECT count(*) FROM fin_payment_orders
					WHERE id = @PaymentId AND patient_id = @PatientId
					  AND visit_or_encounter_id = @VisitId AND status = 1 AND total_amount > 0
					""",
					new { PaymentId = rx.PaymentOrderId, PatientId = rx.PatientId, VisitId = rx.VisitId },
					tx, cancellationToken: ct));
				if (paymentValid != 1)
					return ("处方关联的收费单无效、已退费或不属于当前就诊", null);

				var hasPositiveSkinTest = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"SELECT count(*) FROM opd_prescription_items WHERE prescription_id = @RxId AND skin_test_flag = 4 AND is_deleted = 0",
					new { RxId = req.PrescriptionId }, tx, cancellationToken: ct));
				if (hasPositiveSkinTest > 0)
					return ("皮试结果为阳性(+)，处方已锁定，严禁发药", null);
				var hasPendingSkinTest = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"SELECT count(*) FROM opd_prescription_items WHERE prescription_id = @RxId AND skin_test_flag IN (1, 2) AND is_deleted = 0",
					new { RxId = req.PrescriptionId }, tx, cancellationToken: ct));
				if (hasPendingSkinTest > 0)
					return ("处方包含需皮试药品且尚未录入皮试阴性双签结果，严禁发药", null);

				var items = (await conn.QueryAsync<RxItemRow>(new CommandDefinition(
					"""
					SELECT id, drug_id, drug_name_snapshot, total_quantity, skin_test_flag
					FROM opd_prescription_items
					WHERE prescription_id = @RxId AND is_deleted = 0
					ORDER BY created_at_ms
					""", new { RxId = req.PrescriptionId }, tx, cancellationToken: ct))).ToList();
				if (items.Count == 0)
					return ("处方没有有效药品明细，禁止发药", null);
				if (_inventoryContract == null || _patientContract == null)
					return ("药品库存或患者过敏档案服务不可用，禁止发药", null);
				var patient = await _patientContract.GetPatientSummaryAsync(rx.PatientId, ct);
				if (patient == null || !patient.IsActive)
					return ("患者档案不存在或已停用，禁止发药", null);
				foreach (var item in items)
				{
					var conflict = FindAllergyConflict(patient.AllergyTags, item.DrugNameSnapshot);
					if (conflict != null)
						return ($"患者存在 [{conflict}] 过敏记录，禁止发药 [{item.DrugNameSnapshot}]", null);
				}
				// Check the complete aggregate demand before any batch is changed. The same
				// drug can occur on several prescription lines and FEFO spans batches.
				var outpatientDemand = items
					.GroupBy(item => item.DrugId, StringComparer.OrdinalIgnoreCase)
					.Select(group => new StockDemand(
						group.Key, group.First().DrugNameSnapshot, group.Sum(item => item.TotalQuantity)))
					.ToList();
				var outpatientStockError = await CheckAvailableStockAsync(
					conn, tx, actorPharmacyId, outpatientDemand, "药房", ct);
				if (outpatientStockError != null)
					return (outpatientStockError, null);

				var claimed = await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE opd_prescriptions
					SET status = 4, updated_at_ms = @Now, updated_by = @PharmacistId,
					    version_no = version_no + 1
					WHERE id = @RxId AND status = 3 AND payment_order_id = @PaymentId
					""",
					new { RxId = rx.Id, PaymentId = rx.PaymentOrderId, Now = nowMs, PharmacistId = pharmacistId },
					tx, cancellationToken: ct));
				if (claimed != 1)
					return ("处方状态已变更，发药已取消", null);

				foreach (var item in items)
				{
					var deducted = await _inventoryContract.DeductFefoStockAsync(
						actorPharmacyId, item.DrugId, item.TotalQuantity, dispenseNo,
						pharmacistId, ct, rx.PaymentOrderId);
					if (!deducted.IsSuccess)
						throw new InvalidOperationException(deducted.Message);
				}

				var existingOrder = await conn.QuerySingleOrDefaultAsync<PhmDispenseOrder>(new CommandDefinition(
					"SELECT * FROM phm_dispense_orders WHERE prescription_id = @RxId LIMIT 1",
					new { RxId = rx.Id }, tx, cancellationToken: ct));
				var closedMetadata = JsonSerializer.Serialize(new
				{
					orderClosed = true,
					closedAtMs = nowMs,
					closedBy = pharmacistId,
					prescriptionId = rx.Id,
					paymentId = rx.PaymentOrderId,
					preventSecondaryDispense = true
				});
				PhmDispenseOrder dsp = existingOrder ?? new PhmDispenseOrder
				{
					DispenseNo = dispenseNo,
					PharmacyId = actorPharmacyId,
					PrescriptionId = rx.Id,
					PatientId = rx.PatientId,
					Status = 0,
					CreatedBy = pharmacistId
				};
				dsp.DispenseNo = dispenseNo;
				dsp.PharmacyId = actorPharmacyId;
				dsp.PatientId = rx.PatientId;
				dsp.Status = 1;
				dsp.DispensedById = pharmacistId;
				dsp.DispensedAtMs = nowMs;
				dsp.DrugReturnConfirmed = 0;
				dsp.ExtDataJson = closedMetadata;
				dsp.UpdatedBy = pharmacistId;
				if (existingOrder == null)
					await _dispenseRepo.InsertAsync(dsp, ct);
				else
					await _dispenseRepo.UpdateAsync(dsp, ct);

				var existingLines = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"SELECT count(*) FROM phm_dispense_order_lines WHERE dispense_order_id = @Id",
					new { Id = dsp.Id }, tx, cancellationToken: ct));
				if (existingLines == 0)
				{
					foreach (var item in items)
					{
						var drug = await conn.QuerySingleOrDefaultAsync<PrescriptionItemSnapshotRow>(new CommandDefinition(
							"SELECT id AS DrugId, drug_code AS DrugCode, drug_name AS DrugName, price AS Price, cost_price AS CostPrice " +
							"FROM inv_drugs WHERE id = @DrugId LIMIT 1",
							new { DrugId = item.DrugId }, tx, cancellationToken: ct));
						var line = new PhmDispenseOrderLine
						{
							DispenseOrderId = dsp.Id,
							DrugId = item.DrugId,
							DrugCode = drug?.DrugCode ?? string.Empty,
							DrugName = string.IsNullOrWhiteSpace(drug?.DrugName) ? item.DrugNameSnapshot : drug!.DrugName,
							Quantity = item.TotalQuantity,
							UnitPrice = drug?.Price ?? 0m,
							UnitCostPrice = drug?.CostPrice ?? 0m,
							CreatedBy = pharmacistId,
							UpdatedBy = pharmacistId
						};
						await _dispenseLineRepo.InsertAsync(line, ct);
					}
				}

				await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE ops_outbox_messages
					SET status = 1, updated_at_ms = @Now, version_no = version_no + 1
					WHERE event_type = 'PHM_READY_TO_DISPENSE'
					  AND (json_extract(ext_data_json, '$.prescriptionId') = @RxId
					       OR json_extract(payload_json, '$.PrescriptionId') = @RxId
					       OR json_extract(payload_json, '$.prescriptionId') = @RxId)
					""",
					new { RxId = rx.Id, Now = nowMs }, tx, cancellationToken: ct));
				return ((string?)null, dsp);
			});
		}
		catch (InvalidOperationException ex)
		{
			return ApiResponse<PhmDispenseOrder>.Fail(400, ex.Message);
		}

		if (outcomeError != null || order == null)
			return ApiResponse<PhmDispenseOrder>.Fail(400, outcomeError ?? "发药失败");
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("PHM_DISPENSE_AND_CLOSE", "PhmDispenseOrder", order.Id, $"药房 [{actorPharmacyId}] 完成处方 [{req.PrescriptionId}] 发药并关闭订单（防二次发药锁止），发药单号 {order.DispenseNo}", pharmacistId, null, actorPharmacyId);
		}
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			await _idempotency.SaveResponseAsync("PHM_DISPENSE_" + req.RequestKey.Trim(), JsonSerializer.Serialize(order));
		}
		return ApiResponse<PhmDispenseOrder>.Success(order, "药房审方通过，已按 FEFO 批次完成发药并自动关闭处方订单（严防二次发药）");
	}

	[HttpPost("confirm-return")]
	[RequireRole(RoleCode.Pharmacist, "PHARMACY", "PHM")]
	public async Task<ApiResponse<PhmDispenseOrder>> ConfirmDrugReturn([FromBody] ConfirmDrugReturnRequestDto req)
	{
		if (req == null || string.IsNullOrWhiteSpace(req.DispenseOrderId))
			return ApiResponse<PhmDispenseOrder>.Fail(400, "必须指定发药单 ID");
		if (!IsPharmacist())
			return ApiResponse<PhmDispenseOrder>.Fail(403, "仅药师可以办理退药验收入库");
		if (!req.IsPackagingIntact || !req.IsColdChainQualified)
			return ApiResponse<PhmDispenseOrder>.Fail(400, "退药GSP质量核验不通过（药品最小包装已拆封损毁或冷链脱离超标），药房严禁办理退药入库！");
		if (string.IsNullOrWhiteSpace(req.Reason))
			return ApiResponse<PhmDispenseOrder>.Fail(400, "退药原因不能为空");

		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			string idemKey = "PHM_RETURN_" + req.RequestKey.Trim();
			var (isDup, cachedJson, isConflict) = await _idempotency.TryAcquireAsync(idemKey, JsonSerializer.Serialize(req));
			if (isConflict)
			{
				return ApiResponse<PhmDispenseOrder>.Fail(409, "请求幂等键冲突：已被不同的退药请求使用");
			}
			if (isDup && !string.IsNullOrWhiteSpace(cachedJson))
			{
				try
				{
					var cached = JsonSerializer.Deserialize<PhmDispenseOrder>(cachedJson);
					if (cached != null)
					{
						return ApiResponse<PhmDispenseOrder>.Success(cached, "幂等命中：已返回原退药单（防重复退药）");
					}
				}
				catch { }
			}
		}

		var pharmacistId = _userSession!.UserId;
		if (!IsPharmacist())
			return ApiResponse<PhmDispenseOrder>.Fail(403, "仅药师可以办理退药验收入库");
		PhmDispenseOrder? order;
		string? error;
		try
		{
			(error, order) = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
			{
				using var conn = _connFactory.CreateConnection();
				var tx = _connFactory.CurrentTransaction;
				var loaded = await conn.QuerySingleOrDefaultAsync<PhmDispenseOrder>(new CommandDefinition(
					"""
					SELECT * FROM phm_dispense_orders
					WHERE id = @Id OR dispense_no = @Id OR prescription_id = @Id
					ORDER BY CASE WHEN id = @Id THEN 0 WHEN dispense_no = @Id THEN 1 ELSE 2 END
					LIMIT 1
					""", new { Id = req.DispenseOrderId }, tx, cancellationToken: ct));
				if (loaded == null)
					return ("发药单不存在", (PhmDispenseOrder?)null);
				if (!await IsAuthorizedPharmacyAsync(loaded.PharmacyId, ct))
					return ("当前账号未被授权在该发药药房办理退药入库", (PhmDispenseOrder?)null);
				if (loaded.Status == 2 && loaded.DrugReturnConfirmed == 1)
					return ((string?)null, loaded);
				if (loaded.Status != 1 || loaded.DrugReturnConfirmed != 0)
					return ("仅已发药且未退药的发药单可以办理验收入库", (PhmDispenseOrder?)null);
				if (_inventoryContract == null)
					return ("库存服务不可用，不能确认退药入库", (PhmDispenseOrder?)null);

				var nowMs = SystemTime.UtcNowMs;
				var claimed = await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE phm_dispense_orders
					SET status = 2, drug_return_confirmed = 1, updated_at_ms = @Now,
					    updated_by = @PharmacistId, version_no = version_no + 1
					WHERE id = @Id AND status = 1 AND drug_return_confirmed = 0
					""",
					new { Id = loaded.Id, Now = nowMs, PharmacistId = pharmacistId },
					tx, cancellationToken: ct));
				if (claimed != 1)
					return ("发药单已被其他操作员处理，退药已取消", (PhmDispenseOrder?)null);

				loaded.VersionNo++;
				loaded.DrugReturnConfirmed = 1;
				loaded.Status = 2;
				loaded.ExtDataJson = JsonSerializer.Serialize(new
				{
					returnReason = req.Reason,
					returnedBy = pharmacistId,
					returnedAtMs = nowMs,
					isPackagingIntact = req.IsPackagingIntact,
					isColdChainQualified = req.IsColdChainQualified,
					prescriptionId = loaded.PrescriptionId
				});
				loaded.UpdatedBy = pharmacistId;
				await _dispenseRepo.UpdateAsync(loaded, ct);

				var restored = await _inventoryContract.RestoreStockFromReturnAsync(
					loaded.PharmacyId, loaded.DispenseNo, pharmacistId, ct);
				if (!restored.IsSuccess)
					throw new InvalidOperationException(restored.Message);
				if (!string.IsNullOrWhiteSpace(loaded.PrescriptionId))
				{
					var rxUpdated = await conn.ExecuteAsync(new CommandDefinition(
						"""
						UPDATE opd_prescriptions
						SET status = 5, updated_at_ms = @Now, updated_by = @PharmacistId,
						    version_no = version_no + 1
						WHERE id = @RxId AND status = 4
						""",
						new { RxId = loaded.PrescriptionId, Now = nowMs, PharmacistId = pharmacistId },
						tx, cancellationToken: ct));
					if (rxUpdated != 1)
						return ("处方已不处于已发药状态，退药已取消", (PhmDispenseOrder?)null);
				}
				return ((string?)null, loaded);
			});
		}
		catch (InvalidOperationException ex)
		{
			return ApiResponse<PhmDispenseOrder>.Fail(400, ex.Message);
		}
		if (error != null || order == null)
			return ApiResponse<PhmDispenseOrder>.Fail(409, error ?? "退药入库失败");
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("PHM_RETURN_CONFIRM", "PhmDispenseOrder", order.Id, "药师 [" + pharmacistId + "] 完成退药扫码验收入库（原批次库存回补完成），原因：" + req.Reason, pharmacistId, null, order.PharmacyId);
		}
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			await _idempotency.SaveResponseAsync("PHM_RETURN_" + req.RequestKey.Trim(), JsonSerializer.Serialize(order));
		}
		return ApiResponse<PhmDispenseOrder>.Success(order, "药房已完成扫码验收入库并回补原批次库存（drug_return_confirmed=1），准许财务窗口办理退费");
	}

	[HttpGet("dispense/{id}")]
	public async Task<ApiResponse<PhmDispenseOrder>> GetDispenseOrder(string id)
	{
		PhmDispenseOrder order = await _dispenseRepo.GetByIdAsync(id);
		if (order == null)
			return ApiResponse<PhmDispenseOrder>.Fail(404, "发药单不存在");
		if (!await IsAuthorizedPharmacyAsync(order.PharmacyId, HttpContext.RequestAborted))
			return ApiResponse<PhmDispenseOrder>.Fail(403, "无权查看该药房的发药单");
		return ApiResponse<PhmDispenseOrder>.Success(order);
	}

	[HttpGet("dispense-list")]
	public async Task<ApiResponse<IReadOnlyList<PhmDispenseOrder>>> GetDispenseOrders([FromQuery] string? pharmacyId = null, [FromQuery] int? status = null)
	{
		if (!string.IsNullOrWhiteSpace(pharmacyId) &&
			!await IsAuthorizedPharmacyAsync(pharmacyId, HttpContext.RequestAborted))
			return ApiResponse<IReadOnlyList<PhmDispenseOrder>>.Fail(403, "无权查看该药房的发药记录");
		return ApiResponse<IReadOnlyList<PhmDispenseOrder>>.Success(await _dispenseRepo.QueryAsync("SELECT * FROM phm_dispense_orders\nWHERE (@PharmacyId IS NULL OR pharmacy_id = @PharmacyId)\n  AND (@Status IS NULL OR status = @Status)\nORDER BY created_at_ms DESC", new
		{
			PharmacyId = pharmacyId,
			Status = status
		}));
	}

	[HttpGet("dispense/{id}/lines")]
	public async Task<ApiResponse<IReadOnlyList<PhmDispenseOrderLine>>> GetDispenseOrderLines(string id)
	{
		var order = await _dispenseRepo.QuerySingleOrDefaultAsync(
			"SELECT * FROM phm_dispense_orders WHERE id = @Id OR dispense_no = @Id LIMIT 1", new { Id = id });
		if (order == null)
			return ApiResponse<IReadOnlyList<PhmDispenseOrderLine>>.Fail(404, "发药单不存在");
		if (!await IsAuthorizedPharmacyAsync(order.PharmacyId, HttpContext.RequestAborted))
			return ApiResponse<IReadOnlyList<PhmDispenseOrderLine>>.Fail(403, "无权查看该药房的发药明细");
		return ApiResponse<IReadOnlyList<PhmDispenseOrderLine>>.Success(
			await _dispenseLineRepo.QueryAsync(
				"SELECT * FROM phm_dispense_order_lines WHERE dispense_order_id = @Id ORDER BY created_at_ms, drug_name",
				new { Id = order.Id }));
	}

	[HttpGet("ward-ready-queue")]
	[RequireRole(RoleCode.Pharmacist, "PHARMACY", "PHM")]
	public async Task<ApiResponse<IReadOnlyList<WardReadyOrderDto>>> GetWardReadyQueue(
		[FromQuery] string? pharmacyId = null, [FromQuery] string? encounterId = null)
	{
		if (!IsPharmacist())
			return ApiResponse<IReadOnlyList<WardReadyOrderDto>>.Fail(403, "仅药师可以查看住院发药队列");
		var targetPharmacyId = string.IsNullOrWhiteSpace(pharmacyId) ? _userSession!.ActiveDeptId : pharmacyId.Trim();
		if (string.IsNullOrWhiteSpace(targetPharmacyId))
			return ApiResponse<IReadOnlyList<WardReadyOrderDto>>.Fail(400, "请选择发药药房");
		if (!await IsAuthorizedPharmacyAsync(targetPharmacyId, HttpContext.RequestAborted))
			return ApiResponse<IReadOnlyList<WardReadyOrderDto>>.Fail(403, "无权查看该药房的住院发药队列");

		using var conn = _connFactory.CreateConnection();
		var rows = await conn.QueryAsync<WardOrderRow>(new CommandDefinition(
			"""
			SELECT o.id AS Id, o.order_no AS OrderNo, o.encounter_id AS EncounterId,
			       o.patient_id AS PatientId, o.exec_target_dept_id AS ExecTargetDeptId,
			       o.order_term_type AS OrderTermType, COALESCE(d.id, o.item_code) AS DrugId,
			       o.item_code AS ItemCode,
			       o.item_name AS ItemName, o.dosage AS Dosage, o.frequency_code AS FrequencyCode,
			       o.route_code AS RouteCode, o.plan_start_at_ms AS PlanStartAtMs,
			       e.admission_no AS AdmissionNo, p.medical_record_no AS Mrn, p.full_name AS PatientName,
			       e.admit_ward_id AS WardId, COALESCE(w.dept_name, e.admit_ward_id) AS WardName,
			       COALESCE(d.price, 0) AS UnitDosage, COALESCE(d.unit_name, '') AS UnitName,
			       COALESCE((SELECT SUM(l.quantity) FROM phm_dispense_order_lines l
			                 WHERE l.medical_order_id = o.id), 0) AS ClaimedQuantity
			FROM ord_medical_orders o
			INNER JOIN ipd_encounters e ON e.id = o.encounter_id
			INNER JOIN pat_patients p ON p.id = o.patient_id
			LEFT JOIN sys_departments w ON w.id = e.admit_ward_id
			LEFT JOIN inv_drugs d ON d.id = o.item_code OR d.drug_code = o.item_code
			WHERE o.order_category = 'DRUG' AND o.status = 1
			  AND e.status = 1 AND e.triage_status = 1
			  AND o.exec_target_dept_id = @PharmacyId
			  AND (@EncounterId IS NULL OR o.encounter_id = @EncounterId)
			  AND NOT EXISTS (
			      SELECT 1 FROM phm_dispense_order_lines l
			      WHERE l.medical_order_id = o.id
			  )
			ORDER BY o.plan_start_at_ms ASC, o.group_no ASC, o.order_no ASC
			""",
			new { PharmacyId = targetPharmacyId, EncounterId = encounterId },
			cancellationToken: HttpContext.RequestAborted));

		var result = rows
			.Select(row => new WardReadyOrderDto(
				row.Id, row.OrderNo, row.EncounterId, row.AdmissionNo,
				row.PatientId, row.PatientName, row.WardId, row.WardName,
				row.DrugId, row.ItemCode, row.ItemName, row.Dosage,
				row.FrequencyCode, row.RouteCode, row.OrderTermType, targetPharmacyId,
				row.PlanStartAtMs))
			.ToList();
		return ApiResponse<IReadOnlyList<WardReadyOrderDto>>.Success(result);
	}

	[HttpPost("ward-dispense")]
	[RequireRole(RoleCode.Pharmacist, "PHARMACY", "PHM")]
	public async Task<ApiResponse<PhmDispenseOrder>> WardDispense([FromBody] WardDispenseRequestDto req)
	{
		if (req == null || string.IsNullOrWhiteSpace(req.EncounterId))
			return ApiResponse<PhmDispenseOrder>.Fail(400, "住院发药必须指定住院就诊 ID");
		if (req.Lines == null || req.Lines.Count == 0)
			return ApiResponse<PhmDispenseOrder>.Fail(400, "请至少选择一条住院药品医嘱");
		if (!IsPharmacist())
			return ApiResponse<PhmDispenseOrder>.Fail(403, "仅药师可以完成住院发药");

		var pharmacistId = _userSession!.UserId;
		var pharmacyId = string.IsNullOrWhiteSpace(req.PharmacyId) ? _userSession.ActiveDeptId : req.PharmacyId.Trim();
		if (string.IsNullOrWhiteSpace(pharmacyId))
			return ApiResponse<PhmDispenseOrder>.Fail(403, "请选择住院发药药房");
		if (!await IsAuthorizedPharmacyAsync(pharmacyId, HttpContext.RequestAborted))
			return ApiResponse<PhmDispenseOrder>.Fail(403, "当前账号未被授权在该药房完成住院发药");
		if (_inventoryContract == null || _patientContract == null)
			return ApiResponse<PhmDispenseOrder>.Fail(400, "药品库存或患者过敏档案服务不可用，禁止发药");

		var normalizedLines = req.Lines
			.Where(line => line != null)
			.GroupBy(line => line.MedicalOrderId?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			.Select(group => new WardDispenseLineRequestDto(group.Key, group.Sum(line => line.Quantity)))
			.ToList();
		if (normalizedLines.Any(line => string.IsNullOrWhiteSpace(line.MedicalOrderId) || line.Quantity <= 0))
			return ApiResponse<PhmDispenseOrder>.Fail(400, "每条住院医嘱必须填写大于 0 的发药数量");

		var requestKey = string.IsNullOrWhiteSpace(req.RequestKey)
			? null
			: "PHM_WARD_" + req.RequestKey.Trim();
		if (requestKey != null && _idempotency != null)
		{
			var (isDuplicate, cachedJson, isConflict) =
				await _idempotency.TryAcquireAsync(requestKey, JsonSerializer.Serialize(req));
			if (isConflict)
				return ApiResponse<PhmDispenseOrder>.Fail(409, "请求幂等键冲突：已被不同的住院发药请求使用");
			if (isDuplicate && !string.IsNullOrWhiteSpace(cachedJson))
			{
				try
				{
					var cached = JsonSerializer.Deserialize<PhmDispenseOrder>(cachedJson);
					if (cached != null)
						return ApiResponse<PhmDispenseOrder>.Success(cached, "幂等命中：已返回原住院发药单");
				}
				catch (JsonException)
				{
				}
			}
		}

		var dispenseNo = "DSPW" + DateTime.UtcNow.ToString("yyMMddHHmmssfff");
		PhmDispenseOrder? order = null;
		string? error = null;
		try
		{
			(order, error) = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
			{
				using var conn = _connFactory.CreateConnection();
				var tx = _connFactory.CurrentTransaction;
				var nowMs = SystemTime.UtcNowMs;
				var encounter = await conn.QuerySingleOrDefaultAsync<EncounterForDispenseRow>(new CommandDefinition(
					"SELECT e.id AS Id, e.patient_id AS PatientId, e.admission_no AS AdmissionNo, " +
					"e.status AS Status, e.triage_status AS TriageStatus, p.full_name AS PatientName " +
					"FROM ipd_encounters e LEFT JOIN pat_patients p ON p.id = e.patient_id WHERE e.id = @Id",
					new { Id = req.EncounterId.Trim() }, tx, cancellationToken: ct));
				if (encounter == null)
					return ((PhmDispenseOrder?)null, "住院记录不存在");
				if (encounter.Status != 1 || encounter.TriageStatus != 1)
					return ((PhmDispenseOrder?)null, "患者尚未完成病区接诊或已不在院，禁止发药");

				var medicalOrderIds = normalizedLines.Select(line => line.MedicalOrderId).ToList();
				var orders = (await conn.QueryAsync<WardOrderRow>(new CommandDefinition(
					"""
					SELECT o.id AS Id, o.order_no AS OrderNo, o.encounter_id AS EncounterId,
					       o.patient_id AS PatientId, o.exec_target_dept_id AS ExecTargetDeptId,
					       o.order_term_type AS OrderTermType, COALESCE(d.id, o.item_code) AS DrugId,
					       o.item_code AS ItemCode,
					       o.item_name AS ItemName, o.dosage AS Dosage, o.frequency_code AS FrequencyCode,
					       o.route_code AS RouteCode, o.plan_start_at_ms AS PlanStartAtMs,
					       e.admission_no AS AdmissionNo, p.medical_record_no AS Mrn, p.full_name AS PatientName,
					       e.admit_ward_id AS WardId, COALESCE(w.dept_name, e.admit_ward_id) AS WardName,
					       COALESCE(d.price, 0) AS UnitDosage, COALESCE(d.unit_name, '') AS UnitName,
					       0 AS ClaimedQuantity
					FROM ord_medical_orders o
					INNER JOIN ipd_encounters e ON e.id = o.encounter_id
					INNER JOIN pat_patients p ON p.id = o.patient_id
					LEFT JOIN sys_departments w ON w.id = e.admit_ward_id
					LEFT JOIN inv_drugs d ON d.id = o.item_code OR d.drug_code = o.item_code
					WHERE o.id IN @MedicalOrderIds
					""",
					new { MedicalOrderIds = medicalOrderIds }, tx, cancellationToken: ct))).ToList();
				if (orders.Count != medicalOrderIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
					return ((PhmDispenseOrder?)null, "部分住院医嘱不存在，发药已取消");
				if (orders.Any(o => !string.Equals(o.EncounterId, encounter.Id, StringComparison.OrdinalIgnoreCase)))
					return ((PhmDispenseOrder?)null, "住院医嘱不属于当前患者住院记录，禁止发药");
				var nonDrug = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"SELECT count(*) FROM ord_medical_orders WHERE id IN @Ids AND (order_category <> 'DRUG' OR status <> 1)",
					new { Ids = medicalOrderIds }, tx, cancellationToken: ct));
				if (nonDrug > 0)
					return ((PhmDispenseOrder?)null, "仅已校对且未停止的住院药品医嘱可发药");
				if (orders.Any(o => !string.Equals(o.ExecTargetDeptId, pharmacyId, StringComparison.OrdinalIgnoreCase)))
					return ((PhmDispenseOrder?)null, "医嘱目标药房与当前药房不一致，禁止发药");

				var alreadyClaimed = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"SELECT count(*) FROM phm_dispense_order_lines WHERE medical_order_id IN @Ids",
					new { Ids = medicalOrderIds }, tx, cancellationToken: ct));
				if (alreadyClaimed > 0)
				{
					var existingWard = await conn.QuerySingleOrDefaultAsync<PhmDispenseOrder>(new CommandDefinition(
						"SELECT dos.* FROM phm_dispense_orders dos INNER JOIN phm_dispense_order_lines l ON l.dispense_order_id = dos.id " +
						"WHERE l.medical_order_id IN @Ids ORDER BY dos.created_at_ms DESC LIMIT 1",
						new { Ids = medicalOrderIds }, tx, cancellationToken: ct));
					return (existingWard, "该住院医嘱已完成发药并关闭订单" +
						(existingWard == null ? "" : "（发药单号：" + existingWard.DispenseNo + "），严禁二次发药")
						+ "|409");
				}

				var patient = await _patientContract.GetPatientSummaryAsync(encounter.PatientId, ct);
				if (patient == null || !patient.IsActive)
					return ((PhmDispenseOrder?)null, "患者档案不存在或已停用，禁止发药");

				var drugSnapshots = new Dictionary<string, PrescriptionItemSnapshotRow>(StringComparer.OrdinalIgnoreCase);
				foreach (var line in normalizedLines)
				{
					var medicalOrder = orders.Single(o => string.Equals(o.Id, line.MedicalOrderId, StringComparison.OrdinalIgnoreCase));
					var drug = await conn.QuerySingleOrDefaultAsync<PrescriptionItemSnapshotRow>(new CommandDefinition(
						"SELECT id AS DrugId, drug_code AS DrugCode, drug_name AS DrugName, price AS Price, cost_price AS CostPrice " +
						"FROM inv_drugs WHERE id = @Id OR drug_code = @Id LIMIT 1",
						new { Id = medicalOrder.ItemCode }, tx, cancellationToken: ct));
					if (drug == null)
						return ((PhmDispenseOrder?)null, $"药品 [{medicalOrder.ItemName}] 基础档案不存在，禁止发药");
					var conflict = FindAllergyConflict(patient.AllergyTags, drug.DrugName);
					if (conflict != null)
						return ((PhmDispenseOrder?)null, $"患者存在 [{conflict}] 过敏记录，禁止发药 [{drug.DrugName}]");
					drugSnapshots[medicalOrder.Id] = drug;
				}

				var wardDemand = normalizedLines
					.GroupBy(line => drugSnapshots[line.MedicalOrderId].DrugId, StringComparer.OrdinalIgnoreCase)
					.Select(group => new StockDemand(
						group.Key, drugSnapshots[group.First().MedicalOrderId].DrugName,
						group.Sum(line => line.Quantity)))
					.ToList();
				var wardStockError = await CheckAvailableStockAsync(
					conn, tx, pharmacyId, wardDemand, "住院中心药房", ct);
				if (wardStockError != null)
					return ((PhmDispenseOrder?)null, wardStockError);

				// Claim each medical order before changing stock. The guard insert is the
				// transaction's authority against two pharmacists dispensing the same order.
				var claimToken = dispenseNo + "|" + encounter.Id;
				foreach (var line in normalizedLines)
				{
					var claimed = await conn.ExecuteAsync(new CommandDefinition(
						"""
						INSERT INTO phm_dispense_order_lines
						    (id, dispense_order_id, medical_order_id, drug_id, drug_code, drug_name,
						     quantity, unit_price, unit_cost_price, schema_version,
						     created_at_ms, created_by, updated_at_ms, updated_by, version_no)
						VALUES
						    (@Id, @ClaimToken, @MedicalOrderId, @DrugId, @DrugCode, @DrugName,
						     @Quantity, @UnitPrice, @UnitCostPrice, 1,
						     @Now, @OperatorId, @Now, @OperatorId, 1)
						""",
						new
						{
							Id = Guid.NewGuid().ToString("D"),
							ClaimToken = claimToken,
							MedicalOrderId = line.MedicalOrderId,
							DrugId = drugSnapshots[line.MedicalOrderId].DrugId,
							DrugCode = drugSnapshots[line.MedicalOrderId].DrugCode,
							DrugName = drugSnapshots[line.MedicalOrderId].DrugName,
							Quantity = line.Quantity,
							UnitPrice = drugSnapshots[line.MedicalOrderId].Price,
							UnitCostPrice = drugSnapshots[line.MedicalOrderId].CostPrice,
							Now = nowMs,
							OperatorId = pharmacistId
						}, tx, cancellationToken: ct));
					if (claimed != 1)
						return ((PhmDispenseOrder?)null, "住院医嘱已被其他药师发药，严禁二次重复发药|409");
				}

				var dispenseOrder = new PhmDispenseOrder
				{
					DispenseNo = dispenseNo,
					PharmacyId = pharmacyId,
					PrescriptionId = null,
					WardSummaryId = encounter.Id,
					PatientId = encounter.PatientId,
					Status = 1,
					DispensedById = pharmacistId,
					DispensedAtMs = nowMs,
					DrugReturnConfirmed = 0,
					ExtDataJson = JsonSerializer.Serialize(new
					{
						dispenseScene = "IPD",
						encounterId = encounter.Id,
						admissionNo = encounter.AdmissionNo,
						orderClosed = true,
						medicalOrderIds
					}),
					CreatedBy = pharmacistId,
					UpdatedBy = pharmacistId
				};
				await _dispenseRepo.InsertAsync(dispenseOrder, ct);

				foreach (var line in normalizedLines)
				{
					var drug = drugSnapshots[line.MedicalOrderId];
					var deducted = await _inventoryContract.DeductFefoStockAsync(
						pharmacyId, drug.DrugId, line.Quantity, dispenseNo, pharmacistId, ct);
					if (!deducted.IsSuccess)
						throw new InvalidOperationException(deducted.Message);
					var linked = await conn.ExecuteAsync(new CommandDefinition(
						"UPDATE phm_dispense_order_lines SET dispense_order_id = @DispenseOrderId, updated_at_ms = @Now " +
						"WHERE dispense_order_id = @ClaimToken AND medical_order_id = @MedicalOrderId",
						new
						{
							DispenseOrderId = dispenseOrder.Id,
							ClaimToken = claimToken,
							MedicalOrderId = line.MedicalOrderId,
							Now = nowMs
						},
						tx, cancellationToken: ct));
					if (linked != 1)
						throw new InvalidOperationException("住院发药单关联失败，禁止发药");
				}
				return (dispenseOrder, (string?)null);
			});
		}
		catch (InvalidOperationException ex)
		{
			return ApiResponse<PhmDispenseOrder>.Fail(400, ex.Message);
		}
		catch (DbException ex) when (ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
		{
			return ApiResponse<PhmDispenseOrder>.Fail(409, "该住院医嘱已被其他药师发药，严禁二次重复发药");
		}

		if (!string.IsNullOrWhiteSpace(error))
		{
			var parts = error.Split('|', 2);
			return ApiResponse<PhmDispenseOrder>.Fail(parts.Length > 1 ? 409 : 400, parts[0], order);
		}
		if (order == null)
			return ApiResponse<PhmDispenseOrder>.Fail(400, "住院发药失败");
		if (_auditTrail != null)
			await _auditTrail.RecordAuditAsync("PHM_WARD_DISPENSE", "PhmDispenseOrder", order.Id,
				$"住院中心药房 [{pharmacyId}] 为住院记录 [{req.EncounterId}] 完成 {normalizedLines.Count} 条医嘱发药，" +
				$"发药单号 {order.DispenseNo}", pharmacistId, _userSession.DisplayName, pharmacyId);
		if (requestKey != null && _idempotency != null)
			await _idempotency.SaveResponseAsync(requestKey, JsonSerializer.Serialize(order));
		return ApiResponse<PhmDispenseOrder>.Success(order,
			"住院药品已按 FEFO 完成发药，医嘱已锁定，防二次发药");
	}

	private static async Task<string?> CheckAvailableStockAsync(
		DbConnection conn, DbTransaction? tx, string pharmacyId,
		IReadOnlyList<StockDemand> demands, string pharmacyLabel, CancellationToken ct)
	{
		foreach (var demand in demands)
		{
			var available = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
				"""
				SELECT COALESCE(SUM(quantity), 0) FROM inv_drug_batches
				WHERE warehouse_or_pharmacy_id = @PharmacyId AND drug_id = @DrugId
				  AND is_frozen = 0 AND expiry_date_int >= @Today AND quantity > 0
				""",
				new { PharmacyId = pharmacyId, DrugId = demand.DrugId, Today = SystemTime.TodayInt },
				tx, cancellationToken: ct));
			if (available < demand.Quantity)
				return $"{pharmacyLabel} [{pharmacyId}] 药品 [{demand.DrugName}] 可用库存 {available}，" +
					$"本次需要 {demand.Quantity}，库存不足，禁止发药";
		}
		return null;
	}

	private sealed record StockDemand(string DrugId, string DrugName, int Quantity);

	private sealed class EncounterForDispenseRow
	{
		public string Id { get; set; } = string.Empty;
		public string PatientId { get; set; } = string.Empty;
		public string AdmissionNo { get; set; } = string.Empty;
		public string PatientName { get; set; } = string.Empty;
		public int Status { get; set; }
		public int TriageStatus { get; set; }
	}
}
