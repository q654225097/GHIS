using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.Modules.Inventory.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inventory.Controllers;

public partial class InventoryController
{
	public sealed class SupplierDto
	{
		public SupplierDto() { }
		public SupplierDto(string id, string supplierCode, string supplierName, string licenseNo, int licenseExpiryInt, string? contactName, string? contactPhone, int isColdChainQualified, int isActive)
		{
			Id = id; SupplierCode = supplierCode; SupplierName = supplierName; LicenseNo = licenseNo;
			LicenseExpiryInt = licenseExpiryInt; ContactName = contactName; ContactPhone = contactPhone;
			IsColdChainQualified = isColdChainQualified; IsActive = isActive;
		}
		public string Id { get; set; } = string.Empty;
		public string SupplierCode { get; set; } = string.Empty;
		public string SupplierName { get; set; } = string.Empty;
		public string LicenseNo { get; set; } = string.Empty;
		public int LicenseExpiryInt { get; set; }
		public string? ContactName { get; set; }
		public string? ContactPhone { get; set; }
		public int IsColdChainQualified { get; set; }
		public int IsActive { get; set; }
	}

	public sealed record UpsertSupplierRequestDto(
		string? Id,
		string SupplierCode,
		string SupplierName,
		string LicenseNo,
		int LicenseExpiryInt,
		string? ContactName = null,
		string? ContactPhone = null,
		bool IsColdChainQualified = true,
		bool IsActive = true,
		string? Uscc = null,
		string? EntityType = null,
		string? GspCertNo = null,
		int? GspExpiryInt = null,
		string? BankAccount = null,
		string? BankName = null);

	public sealed record CreatePurchaseOrderLineDto(
		string DrugId,
		int RequestedQty,
		decimal? UnitCostPrice = null);

	public sealed record CreatePurchaseOrderRequestDto(
		string WarehouseId,
		string SupplierId,
		IReadOnlyList<CreatePurchaseOrderLineDto> Lines,
		bool AutoApprove = false,
		string? Remark = null);

	public sealed class PurchaseOrderLineViewDto
	{
		public PurchaseOrderLineViewDto() { }
		public PurchaseOrderLineViewDto(string id, string poId, string drugId, string drugCode, string drugName, string? spec, int requestedQty, int receivedQty, decimal unitCostPrice)
		{
			Id = id; PoId = poId; DrugId = drugId; DrugCode = drugCode; DrugName = drugName; Spec = spec;
			RequestedQty = requestedQty; ReceivedQty = receivedQty; UnitCostPrice = unitCostPrice;
		}
		public string Id { get; set; } = string.Empty;
		public string PoId { get; set; } = string.Empty;
		public string DrugId { get; set; } = string.Empty;
		public string DrugCode { get; set; } = string.Empty;
		public string DrugName { get; set; } = string.Empty;
		public string? Spec { get; set; }
		public int RequestedQty { get; set; }
		public int ReceivedQty { get; set; }
		public decimal UnitCostPrice { get; set; }
	}

	public sealed class PurchaseOrderViewDto
	{
		public PurchaseOrderViewDto() { }
		public PurchaseOrderViewDto(string id, string poNo, string warehouseId, string supplierId, string supplierName, string status, decimal totalEstimatedAmount, string? applicantId, string? approvedBy, long? approvedAtMs, string? remark, long createdAtMs, IReadOnlyList<PurchaseOrderLineViewDto> lines)
		{
			Id = id; PoNo = poNo; WarehouseId = warehouseId; SupplierId = supplierId; SupplierName = supplierName;
			Status = status; TotalEstimatedAmount = totalEstimatedAmount; ApplicantId = applicantId; ApprovedBy = approvedBy;
			ApprovedAtMs = approvedAtMs; Remark = remark; CreatedAtMs = createdAtMs; Lines = lines;
		}
		public string Id { get; set; } = string.Empty;
		public string PoNo { get; set; } = string.Empty;
		public string WarehouseId { get; set; } = string.Empty;
		public string SupplierId { get; set; } = string.Empty;
		public string SupplierName { get; set; } = string.Empty;
		public string Status { get; set; } = string.Empty;
		public decimal TotalEstimatedAmount { get; set; }
		public string? ApplicantId { get; set; }
		public string? ApprovedBy { get; set; }
		public long? ApprovedAtMs { get; set; }
		public string? Remark { get; set; }
		public long CreatedAtMs { get; set; }
		public IReadOnlyList<PurchaseOrderLineViewDto> Lines { get; set; } = Array.Empty<PurchaseOrderLineViewDto>();
	}

	public sealed record GspAcceptanceRequestDto(
		string? PoId,
		string WarehouseId,
		string DrugId,
		string BatchNo,
		int ExpiryDateInt,
		int Quantity,
		string ReceiverId,
		string CheckerId,
		bool AppearanceOk = true,
		bool PackageIntact = true,
		bool LabelConsistent = true,
		decimal? ColdChainTemp = null,
		decimal? MaxAllowedColdChainTemp = null,
		string? Remark = null);

	public sealed class GspAcceptanceViewDto
	{
		public GspAcceptanceViewDto() { }
		public GspAcceptanceViewDto(string id, string acceptanceNo, string? poId, string batchId, string warehouseId, string drugId, string batchNo, int quantity, int appearanceOk, int packageIntact, int labelConsistent, decimal? coldChainTemp, string receiverId, string checkerId, long acceptedAtMs, string conclusion, string? remark)
		{
			Id = id; AcceptanceNo = acceptanceNo; PoId = poId; BatchId = batchId; WarehouseId = warehouseId; DrugId = drugId;
			BatchNo = batchNo; Quantity = quantity; AppearanceOk = appearanceOk; PackageIntact = packageIntact; LabelConsistent = labelConsistent;
			ColdChainTemp = coldChainTemp; ReceiverId = receiverId; CheckerId = checkerId; AcceptedAtMs = acceptedAtMs;
			Conclusion = conclusion; Remark = remark;
		}
		public string Id { get; set; } = string.Empty;
		public string AcceptanceNo { get; set; } = string.Empty;
		public string? PoId { get; set; }
		public string BatchId { get; set; } = string.Empty;
		public string WarehouseId { get; set; } = string.Empty;
		public string DrugId { get; set; } = string.Empty;
		public string BatchNo { get; set; } = string.Empty;
		public int Quantity { get; set; }
		public int AppearanceOk { get; set; }
		public int PackageIntact { get; set; }
		public int LabelConsistent { get; set; }
		public decimal? ColdChainTemp { get; set; }
		public string ReceiverId { get; set; } = string.Empty;
		public string CheckerId { get; set; } = string.Empty;
		public long AcceptedAtMs { get; set; }
		public string Conclusion { get; set; } = string.Empty;
		public string? Remark { get; set; }
	}

	public sealed record AutoFreezeExpiryRequestDto(
		string? WarehouseOrPharmacyId = null,
		int NearExpiryDays = 0);

	[HttpGet("suppliers")]
	public async Task<ApiResponse<IReadOnlyList<SupplierDto>>> GetSuppliers([FromQuery] bool activeOnly = true)
	{
		await using var conn = _connFactory.CreateConnection();
		var rows = (await conn.QueryAsync<SupplierDto>(@"
SELECT id AS Id, supplier_code AS SupplierCode, supplier_name AS SupplierName,
       license_no AS LicenseNo, license_expiry_int AS LicenseExpiryInt,
       contact_name AS ContactName, contact_phone AS ContactPhone,
       is_cold_chain_qualified AS IsColdChainQualified, is_active AS IsActive
FROM inv_suppliers
WHERE (@ActiveOnly = 0 OR is_active = 1)
ORDER BY supplier_code ASC", new { ActiveOnly = activeOnly ? 1 : 0 })).ToList();
		return ApiResponse<IReadOnlyList<SupplierDto>>.Success(rows);
	}

	[HttpPost("suppliers")]
	public async Task<ApiResponse<SupplierDto>> UpsertSupplier([FromBody] UpsertSupplierRequestDto req)
	{
		if (!IsInventoryOperator())
			return ApiResponse<SupplierDto>.Fail(403, "仅药库药师或系统管理员可以维护GSP供应商资质");
		if (req == null || string.IsNullOrWhiteSpace(req.SupplierCode) || string.IsNullOrWhiteSpace(req.SupplierName) || string.IsNullOrWhiteSpace(req.LicenseNo))
			return ApiResponse<SupplierDto>.Fail(400, "供应商编码、名称及GSP许可证号不能为空");

		if (!string.IsNullOrWhiteSpace(req.Uscc) && req.Uscc.Trim().Length != 18)
			return ApiResponse<SupplierDto>.Fail(400, "统一社会信用代码须符合 GB 32100-2015（18位）规范");

		string op = CurrentUserId();
		long now = SystemTime.UtcNowMs;
		string code = req.SupplierCode.Trim();
		await using var conn = _connFactory.CreateConnection();
		var existingId = await conn.ExecuteScalarAsync<string?>(
			"SELECT id FROM inv_suppliers WHERE id = @Id OR supplier_code = @Code LIMIT 1",
			new { Id = req.Id ?? string.Empty, Code = code });

		string id = existingId ?? ("SUP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant());
		if (existingId != null)
		{
			await conn.ExecuteAsync(@"
UPDATE inv_suppliers
SET supplier_code = @Code, supplier_name = @Name, license_no = @LicenseNo,
    license_expiry_int = @ExpInt, contact_name = @ContactName, contact_phone = @ContactPhone,
    uscc = COALESCE(@Uscc, uscc), entity_type = COALESCE(@EntityType, entity_type),
    gsp_cert_no = COALESCE(@GspCertNo, gsp_cert_no), gsp_expiry_int = COALESCE(@GspExpInt, gsp_expiry_int),
    bank_account = COALESCE(@BankAcc, bank_account), bank_name = COALESCE(@BankName, bank_name),
    is_cold_chain_qualified = @Cold, is_active = @Active,
    updated_at_ms = @Now, updated_by = @Op, version_no = version_no + 1
WHERE id = @Id", new
			{
				Id = id,
				Code = code,
				Name = req.SupplierName.Trim(),
				LicenseNo = req.LicenseNo.Trim(),
				ExpInt = req.LicenseExpiryInt,
				ContactName = req.ContactName?.Trim(),
				ContactPhone = req.ContactPhone?.Trim(),
				Uscc = string.IsNullOrWhiteSpace(req.Uscc) ? null : req.Uscc.Trim().ToUpperInvariant(),
				EntityType = string.IsNullOrWhiteSpace(req.EntityType) ? null : req.EntityType.Trim(),
				GspCertNo = string.IsNullOrWhiteSpace(req.GspCertNo) ? null : req.GspCertNo.Trim(),
				GspExpInt = req.GspExpiryInt,
				BankAcc = string.IsNullOrWhiteSpace(req.BankAccount) ? null : req.BankAccount.Trim(),
				BankName = string.IsNullOrWhiteSpace(req.BankName) ? null : req.BankName.Trim(),
				Cold = req.IsColdChainQualified ? 1 : 0,
				Active = req.IsActive ? 1 : 0,
				Now = now,
				Op = op
			});
		}
		else
		{
			await conn.ExecuteAsync(@"
INSERT INTO inv_suppliers
    (id, supplier_code, supplier_name, license_no, license_expiry_int, contact_name, contact_phone,
     uscc, entity_type, gsp_cert_no, gsp_expiry_int, bank_account, bank_name,
     is_cold_chain_qualified, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    (@Id, @Code, @Name, @LicenseNo, @ExpInt, @ContactName, @ContactPhone,
     @Uscc, @EntityType, @GspCertNo, @GspExpInt, @BankAcc, @BankName,
     @Cold, @Active, 1, @Now, @Op, @Now, @Op, 1)", new
			{
				Id = id,
				Code = code,
				Name = req.SupplierName.Trim(),
				LicenseNo = req.LicenseNo.Trim(),
				ExpInt = req.LicenseExpiryInt,
				ContactName = req.ContactName?.Trim(),
				ContactPhone = req.ContactPhone?.Trim(),
				Uscc = string.IsNullOrWhiteSpace(req.Uscc) ? null : req.Uscc.Trim().ToUpperInvariant(),
				EntityType = string.IsNullOrWhiteSpace(req.EntityType) ? "DRUG_DISTRIBUTOR" : req.EntityType.Trim(),
				GspCertNo = string.IsNullOrWhiteSpace(req.GspCertNo) ? null : req.GspCertNo.Trim(),
				GspExpInt = req.GspExpiryInt,
				BankAcc = string.IsNullOrWhiteSpace(req.BankAccount) ? null : req.BankAccount.Trim(),
				BankName = string.IsNullOrWhiteSpace(req.BankName) ? null : req.BankName.Trim(),
				Cold = req.IsColdChainQualified ? 1 : 0,
				Active = req.IsActive ? 1 : 0,
				Now = now,
				Op = op
			});
		}

		var saved = await conn.QuerySingleAsync<SupplierDto>(@"
SELECT id AS Id, supplier_code AS SupplierCode, supplier_name AS SupplierName,
       license_no AS LicenseNo, license_expiry_int AS LicenseExpiryInt,
       contact_name AS ContactName, contact_phone AS ContactPhone,
       is_cold_chain_qualified AS IsColdChainQualified, is_active AS IsActive
FROM inv_suppliers WHERE id = @Id", new { Id = id });
		return ApiResponse<SupplierDto>.Success(saved, "GSP合格供应商资质档案已保存");
	}

	[HttpGet("purchase-orders")]
	public async Task<ApiResponse<IReadOnlyList<PurchaseOrderViewDto>>> GetPurchaseOrders(
		[FromQuery] string? warehouseId = null,
		[FromQuery] string? status = null)
	{
		await using var conn = _connFactory.CreateConnection();
		var headers = (await conn.QueryAsync<dynamic>(@"
SELECT id, po_no, warehouse_id, supplier_id, supplier_name, status, total_estimated_amount,
       applicant_id, approved_by, approved_at_ms, remark, created_at_ms
FROM inv_purchase_orders
WHERE (@WarehouseId IS NULL OR warehouse_id = @WarehouseId)
  AND (@Status IS NULL OR status = @Status)
ORDER BY created_at_ms DESC LIMIT 100",
			new { WarehouseId = warehouseId, Status = status })).ToList();

		if (headers.Count == 0)
			return ApiResponse<IReadOnlyList<PurchaseOrderViewDto>>.Success(Array.Empty<PurchaseOrderViewDto>());

		var poIds = headers.Select(h => (string)h.id).ToArray();
		var allLines = (await conn.QueryAsync<PurchaseOrderLineViewDto>(@"
SELECT id AS Id, po_id AS PoId, drug_id AS DrugId, drug_code AS DrugCode, drug_name AS DrugName,
       spec AS Spec, requested_qty AS RequestedQty, received_qty AS ReceivedQty, unit_cost_price AS UnitCostPrice
FROM inv_purchase_order_lines
WHERE po_id IN @PoIds
ORDER BY created_at_ms ASC", new { PoIds = poIds })).ToList();

		var linesByPo = allLines.GroupBy(l => l.PoId).ToDictionary(g => g.Key, g => (IReadOnlyList<PurchaseOrderLineViewDto>)g.ToList());
		var result = headers.Select(h =>
		{
			string poId = (string)h.id;
			linesByPo.TryGetValue(poId, out var poLines);
			return new PurchaseOrderViewDto(
				poId,
				(string)h.po_no,
				(string)h.warehouse_id,
				(string)h.supplier_id,
				(string)h.supplier_name,
				(string)h.status,
				Convert.ToDecimal(h.total_estimated_amount),
				(string?)h.applicant_id,
				(string?)h.approved_by,
				(long?)h.approved_at_ms,
				(string?)h.remark,
				Convert.ToInt64(h.created_at_ms),
				poLines ?? Array.Empty<PurchaseOrderLineViewDto>());
		}).ToList();

		return ApiResponse<IReadOnlyList<PurchaseOrderViewDto>>.Success(result);
	}

	[HttpPost("purchase-orders")]
	public async Task<ApiResponse<PurchaseOrderViewDto>> CreatePurchaseOrder([FromBody] CreatePurchaseOrderRequestDto req)
	{
		if (!IsInventoryOperator())
			return ApiResponse<PurchaseOrderViewDto>.Fail(403, "仅药库药师或系统管理员可以制定采购订单");
		if (req == null || string.IsNullOrWhiteSpace(req.WarehouseId) || string.IsNullOrWhiteSpace(req.SupplierId) || req.Lines == null || req.Lines.Count == 0)
			return ApiResponse<PurchaseOrderViewDto>.Fail(400, "库房、供应商及采购药品明细不能为空");

		await using var conn = _connFactory.CreateConnection();
		var supplier = await conn.QuerySingleOrDefaultAsync<SupplierDto>(@"
SELECT id AS Id, supplier_code AS SupplierCode, supplier_name AS SupplierName,
       license_no AS LicenseNo, license_expiry_int AS LicenseExpiryInt,
       contact_name AS ContactName, contact_phone AS ContactPhone,
       is_cold_chain_qualified AS IsColdChainQualified, is_active AS IsActive
FROM inv_suppliers WHERE id = @Id OR supplier_code = @Id LIMIT 1", new { Id = req.SupplierId.Trim() });

		if (supplier == null || supplier.IsActive != 1)
			return ApiResponse<PurchaseOrderViewDto>.Fail(404, "未找到有效的合格供应商档案");
		if (supplier.LicenseExpiryInt < SystemTime.TodayInt)
			return ApiResponse<PurchaseOrderViewDto>.Fail(409, $"供应商 [{supplier.SupplierName}] GSP经营许可证已过期（效期至 {supplier.LicenseExpiryInt}），禁止采购");

		string op = CurrentUserId();
		long now = SystemTime.UtcNowMs;
		string poId = Guid.NewGuid().ToString("D");
		string poNo = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";

		var preparedLines = new List<PurchaseOrderLineViewDto>();
		decimal totalEst = 0m;
		foreach (var lineReq in req.Lines)
		{
			if (lineReq.RequestedQty <= 0)
				return ApiResponse<PurchaseOrderViewDto>.Fail(400, "采购数量必须大于 0");
			var drug = await _drugRepo.GetByIdAsync(lineReq.DrugId)
				?? await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new { Code = lineReq.DrugId });
			if (drug == null)
				return ApiResponse<PurchaseOrderViewDto>.Fail(404, $"药品 [{lineReq.DrugId}] 不存在");

			decimal unitCost = lineReq.UnitCostPrice ?? drug.CostPrice;
			totalEst += unitCost * lineReq.RequestedQty;
			preparedLines.Add(new PurchaseOrderLineViewDto(
				Guid.NewGuid().ToString("D"),
				poId,
				drug.Id,
				drug.DrugCode,
				drug.DrugName,
				drug.Spec,
				lineReq.RequestedQty,
				0,
				unitCost));
		}

		string status = req.AutoApprove ? "APPROVED" : "DRAFT";
		string? approvedBy = req.AutoApprove ? op : null;
		long? approvedAt = req.AutoApprove ? now : null;

		await _connFactory.ExecuteInTransactionAsync(async ct =>
		{
			var tx = _connFactory.CurrentTransaction;
			await using var txConn = _connFactory.CreateConnection();
			await txConn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO inv_purchase_orders
    (id, po_no, warehouse_id, supplier_id, supplier_name, status, total_estimated_amount,
     applicant_id, approved_by, approved_at_ms, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    (@Id, @PoNo, @WarehouseId, @SupplierId, @SupplierName, @Status, @TotalAmount,
     @ApplicantId, @ApprovedBy, @ApprovedAt, @Remark, 1, @Now, @Op, @Now, @Op, 1)",
				new
				{
					Id = poId,
					PoNo = poNo,
					WarehouseId = req.WarehouseId.Trim(),
					SupplierId = supplier.Id,
					SupplierName = supplier.SupplierName,
					Status = status,
					TotalAmount = totalEst,
					ApplicantId = op,
					ApprovedBy = approvedBy,
					ApprovedAt = approvedAt,
					Remark = req.Remark?.Trim(),
					Now = now,
					Op = op
				}, tx, cancellationToken: ct));

			foreach (var l in preparedLines)
			{
				await txConn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO inv_purchase_order_lines
    (id, po_id, drug_id, drug_code, drug_name, spec, requested_qty, received_qty, unit_cost_price, created_at_ms)
VALUES
    (@Id, @PoId, @DrugId, @DrugCode, @DrugName, @Spec, @RequestedQty, 0, @UnitCostPrice, @Now)",
					new
					{
						l.Id,
						l.PoId,
						l.DrugId,
						l.DrugCode,
						l.DrugName,
						l.Spec,
						l.RequestedQty,
						l.UnitCostPrice,
						Now = now
					}, tx, cancellationToken: ct));
			}
			return 0;
		});

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("INV_PO_CREATE", "inv_purchase_orders", poId,
				$"创建采购订单 [{poNo}] 供应商 [{supplier.SupplierName}] 金额 ¥{totalEst:0.00} 状态={status}",
				op, _userSession?.DisplayName, req.WarehouseId.Trim());
		}

		var result = new PurchaseOrderViewDto(
			poId, poNo, req.WarehouseId.Trim(), supplier.Id, supplier.SupplierName,
			status, totalEst, op, approvedBy, approvedAt, req.Remark?.Trim(), now, preparedLines);
		return ApiResponse<PurchaseOrderViewDto>.Success(result, $"采购订单 [{poNo}] 已创建");
	}

	[HttpPost("purchase-orders/{poId}/approve")]
	public async Task<ApiResponse<bool>> ApprovePurchaseOrder(string poId)
	{
		if (!IsInventoryOperator())
			return ApiResponse<bool>.Fail(403, "仅药库药师或系统管理员可以审批采购订单");

		string op = CurrentUserId();
		long now = SystemTime.UtcNowMs;
		await using var conn = _connFactory.CreateConnection();
		int rows = await conn.ExecuteAsync(@"
UPDATE inv_purchase_orders
SET status = 'APPROVED', approved_by = @Op, approved_at_ms = @Now,
    updated_at_ms = @Now, updated_by = @Op, version_no = version_no + 1
WHERE (id = @PoId OR po_no = @PoId) AND status = 'DRAFT'", new { PoId = poId.Trim(), Op = op, Now = now });

		if (rows == 0)
			return ApiResponse<bool>.Fail(409, "采购订单不存在或当前非草稿状态");

		return ApiResponse<bool>.Success(true, "采购订单已审批生效，可执行到货GSP双人验收");
	}

	[HttpGet("gsp-acceptances")]
	public async Task<ApiResponse<IReadOnlyList<GspAcceptanceViewDto>>> GetGspAcceptances([FromQuery] string? warehouseId = null)
	{
		await using var conn = _connFactory.CreateConnection();
		var rows = (await conn.QueryAsync<GspAcceptanceViewDto>(@"
SELECT id AS Id, acceptance_no AS AcceptanceNo, po_id AS PoId, batch_id AS BatchId,
       warehouse_id AS WarehouseId, drug_id AS DrugId, batch_no AS BatchNo, quantity AS Quantity,
       appearance_ok AS AppearanceOk, package_intact AS PackageIntact, label_consistent AS LabelConsistent,
       cold_chain_temp AS ColdChainTemp, receiver_id AS ReceiverId, checker_id AS CheckerId,
       accepted_at_ms AS AcceptedAtMs, conclusion AS Conclusion, remark AS Remark
FROM inv_gsp_acceptances
WHERE (@WarehouseId IS NULL OR warehouse_id = @WarehouseId)
ORDER BY accepted_at_ms DESC LIMIT 100", new { WarehouseId = warehouseId })).ToList();
		return ApiResponse<IReadOnlyList<GspAcceptanceViewDto>>.Success(rows);
	}

	[HttpPost("gsp-acceptance")]
	public async Task<ApiResponse<GspAcceptanceViewDto>> AcceptDeliveryWithGsp([FromBody] GspAcceptanceRequestDto req)
	{
		if (!IsInventoryOperator())
			return ApiResponse<GspAcceptanceViewDto>.Fail(403, "仅药库验收员或系统管理员可以执行GSP验收");
		if (req == null || string.IsNullOrWhiteSpace(req.WarehouseId) || string.IsNullOrWhiteSpace(req.DrugId) || string.IsNullOrWhiteSpace(req.BatchNo))
			return ApiResponse<GspAcceptanceViewDto>.Fail(400, "验收库房、药品和批号不能为空");
		if (req.Quantity <= 0)
			return ApiResponse<GspAcceptanceViewDto>.Fail(400, "GSP到货验收数量必须大于 0");
		if (string.IsNullOrWhiteSpace(req.ReceiverId) || string.IsNullOrWhiteSpace(req.CheckerId))
			return ApiResponse<GspAcceptanceViewDto>.Fail(400, "GSP规范要求必须填写验收员与复核员双人签名");
		if (string.Equals(req.ReceiverId.Trim(), req.CheckerId.Trim(), StringComparison.OrdinalIgnoreCase))
			return ApiResponse<GspAcceptanceViewDto>.Fail(400, "GSP到货验收必须由两名不同人员双人核对，收货人与复核人不能为同一人");

		var drug = await _drugRepo.GetByIdAsync(req.DrugId.Trim())
			?? await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new { Code = req.DrugId.Trim() });
		if (drug == null)
			return ApiResponse<GspAcceptanceViewDto>.Fail(404, $"药品 [{req.DrugId}] 不存在");

		string? supplierId = null;
		string? resolvedPoId = null;
		await using var conn = _connFactory.CreateConnection();
		if (!string.IsNullOrWhiteSpace(req.PoId))
		{
			var po = await conn.QuerySingleOrDefaultAsync<dynamic>(
				"SELECT id, supplier_id, status FROM inv_purchase_orders WHERE id = @PoId OR po_no = @PoId LIMIT 1",
				new { PoId = req.PoId.Trim() });
			if (po == null)
				return ApiResponse<GspAcceptanceViewDto>.Fail(404, $"采购订单 [{req.PoId}] 不存在");
			if (!string.Equals((string)po.status, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
			    !string.Equals((string)po.status, "PARTIAL_RECEIVED", StringComparison.OrdinalIgnoreCase))
			{
				return ApiResponse<GspAcceptanceViewDto>.Fail(409, $"采购订单状态为 [{po.status}]，仅已审批订单可办理GSP验收");
			}
			resolvedPoId = (string)po.id;
			supplierId = (string)po.supplier_id;
		}

		bool tempOutOfRange = req.ColdChainTemp.HasValue && req.MaxAllowedColdChainTemp.HasValue && req.ColdChainTemp.Value > req.MaxAllowedColdChainTemp.Value;
		bool isExpired = req.ExpiryDateInt > 0 && req.ExpiryDateInt < SystemTime.TodayInt;
		bool isQualified = req.AppearanceOk && req.PackageIntact && req.LabelConsistent && !tempOutOfRange && !isExpired;
		string conclusion = isQualified ? "QUALIFIED" : "QUARANTINE_REJECTED";

		string op = CurrentUserId();
		long now = SystemTime.UtcNowMs;
		string warehouseId = req.WarehouseId.Trim();
		string batchNo = req.BatchNo.Trim();
		string acceptanceId = Guid.NewGuid().ToString("D");
		string acceptanceNo = $"GSP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..3].ToUpperInvariant()}";
		string resultBatchId = string.Empty;

		await _connFactory.ExecuteInTransactionAsync(async ct =>
		{
			var existingBatch = await _batchRepo.QuerySingleOrDefaultAsync(
				"SELECT * FROM inv_drug_batches WHERE warehouse_or_pharmacy_id = @Wid AND drug_id = @DrugId AND batch_no = @BatchNo",
				new { Wid = warehouseId, DrugId = drug.Id, BatchNo = batchNo }, ct);

			if (existingBatch != null && isQualified)
			{
				existingBatch.Quantity += req.Quantity;
				existingBatch.UpdatedAtMs = now;
				existingBatch.UpdatedBy = op;
				await _batchRepo.UpdateAsync(existingBatch, ct);
				resultBatchId = existingBatch.Id;
			}
			else
			{
				string storedBatchNo = isQualified ? batchNo : $"{batchNo}-Q-{acceptanceNo[^3..]}";
				var newBatch = new InvDrugBatch
				{
					WarehouseOrPharmacyId = warehouseId,
					DrugId = drug.Id,
					BatchNo = storedBatchNo,
					ExpiryDateInt = req.ExpiryDateInt,
					Quantity = req.Quantity,
					IsFrozen = (short)(isQualified ? 0 : 1),
					InvoiceNo = isQualified ? acceptanceNo : $"REJECTED:{acceptanceNo}",
					CreatedBy = op,
					UpdatedBy = op
				};
				await _batchRepo.InsertAsync(newBatch, ct);
				resultBatchId = newBatch.Id;
			}

			var tx = _connFactory.CurrentTransaction;
			await using var txConn = _connFactory.CreateConnection();
			await txConn.ExecuteAsync(new CommandDefinition(
				"UPDATE inv_drug_batches SET supplier_id = COALESCE(@SupplierId, supplier_id), po_id = COALESCE(@PoId, po_id) WHERE id = @BatchId",
				new { SupplierId = supplierId, PoId = resolvedPoId, BatchId = resultBatchId }, tx, cancellationToken: ct));

			if (isQualified)
			{
				var batch = await _batchRepo.GetByIdAsync(resultBatchId, ct);
				await _ledgerRepo.InsertAsync(new InvStockLedger
				{
					WarehouseOrPharmacyId = warehouseId,
					DrugId = drug.Id,
					BatchId = resultBatchId,
					ChangeType = "IN_PURCHASE",
					ChangeQuantity = req.Quantity,
					BalanceAfter = batch?.Quantity ?? req.Quantity,
					ReferenceDocNo = acceptanceNo,
					UnitCostPrice = drug.CostPrice,
					UnitRetailPrice = drug.Price,
					CreatedBy = op,
					UpdatedBy = op
				}, ct);
			}

			await txConn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO inv_gsp_acceptances
    (id, acceptance_no, po_id, batch_id, warehouse_id, drug_id, batch_no, quantity,
     appearance_ok, package_intact, label_consistent, cold_chain_temp, receiver_id, checker_id,
     accepted_at_ms, conclusion, remark, created_at_ms, created_by)
VALUES
    (@Id, @No, @PoId, @BatchId, @Wid, @DrugId, @BatchNo, @Qty,
     @AppOk, @PkgOk, @LblOk, @Temp, @ReceiverId, @CheckerId,
     @Now, @Conclusion, @Remark, @Now, @Op)",
				new
				{
					Id = acceptanceId,
					No = acceptanceNo,
					PoId = resolvedPoId,
					BatchId = resultBatchId,
					Wid = warehouseId,
					DrugId = drug.Id,
					BatchNo = batchNo,
					Qty = req.Quantity,
					AppOk = req.AppearanceOk ? 1 : 0,
					PkgOk = req.PackageIntact ? 1 : 0,
					LblOk = req.LabelConsistent ? 1 : 0,
					Temp = req.ColdChainTemp,
					ReceiverId = req.ReceiverId.Trim(),
					CheckerId = req.CheckerId.Trim(),
					Now = now,
					Conclusion = conclusion,
					Remark = req.Remark?.Trim(),
					Op = op
				}, tx, cancellationToken: ct));

			if (!string.IsNullOrWhiteSpace(resolvedPoId) && isQualified)
			{
				await txConn.ExecuteAsync(new CommandDefinition(@"
UPDATE inv_purchase_order_lines
SET received_qty = received_qty + @Qty
WHERE po_id = @PoId AND drug_id = @DrugId",
					new { Qty = req.Quantity, PoId = resolvedPoId, DrugId = drug.Id }, tx, cancellationToken: ct));

				int unreceivedLines = await txConn.ExecuteScalarAsync<int>(new CommandDefinition(@"
SELECT COUNT(*) FROM inv_purchase_order_lines WHERE po_id = @PoId AND received_qty < requested_qty",
					new { PoId = resolvedPoId }, tx, cancellationToken: ct));
				await txConn.ExecuteAsync(new CommandDefinition(@"
UPDATE inv_purchase_orders SET status = @PoStatus, updated_at_ms = @Now, updated_by = @Op WHERE id = @PoId",
					new { PoStatus = unreceivedLines == 0 ? "COMPLETED" : "PARTIAL_RECEIVED", Now = now, Op = op, PoId = resolvedPoId },
					tx, cancellationToken: ct));
			}

			return 0;
		});

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("INV_GSP_ACCEPTANCE", "inv_gsp_acceptances", acceptanceId,
				$"GSP双人验收 [{acceptanceNo}] 药品={drug.DrugName} 批号={batchNo} 数量={req.Quantity} 结论={conclusion}",
				op, _userSession?.DisplayName, warehouseId);
		}

		var dto = new GspAcceptanceViewDto(
			acceptanceId, acceptanceNo, resolvedPoId, resultBatchId, warehouseId, drug.Id, batchNo,
			req.Quantity, req.AppearanceOk ? 1 : 0, req.PackageIntact ? 1 : 0, req.LabelConsistent ? 1 : 0,
			req.ColdChainTemp, req.ReceiverId.Trim(), req.CheckerId.Trim(), now, conclusion, req.Remark?.Trim());
		string msg = isQualified
			? $"GSP双人验收合格入库 [{acceptanceNo}]，批次 [{batchNo}] 已放行可用"
			: $"GSP验收判定不合格 [{acceptanceNo}]，批次 [{batchNo}] 已自动冻结隔离";
		return ApiResponse<GspAcceptanceViewDto>.Success(dto, msg);
	}

	[HttpPost("batches/auto-freeze-expired")]
	public async Task<ApiResponse<object>> AutoFreezeExpiredBatches([FromBody] AutoFreezeExpiryRequestDto? req = null)
	{
		if (!IsInventoryOperator())
			return ApiResponse<object>.Fail(403, "仅药库药师或系统管理员可以执行效期冻结巡检");

		int nearDays = Math.Clamp(req?.NearExpiryDays ?? 0, 0, 180);
		var shanghaiToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
		var cutoffDate = shanghaiToday.AddDays(nearDays);
		int cutoffInt = cutoffDate.Year * 10000 + cutoffDate.Month * 100 + cutoffDate.Day;
		string reason = nearDays > 0
			? $"近效期安全警戒({nearDays}天内到期，截止{cutoffInt})自动冻结禁发"
			: $"药品已过期(截止效期<{cutoffInt})GSP自动冻结锁定";
		string op = CurrentUserId();
		long now = SystemTime.UtcNowMs;

		await using var conn = _connFactory.CreateConnection();
		int affected = await conn.ExecuteAsync(@"
UPDATE inv_drug_batches
SET is_frozen = 1,
    updated_at_ms = @Now,
    updated_by = @Op,
    version_no = version_no + 1
WHERE is_frozen = 0
  AND quantity > 0
  AND expiry_date_int <= @CutoffInt
  AND (@Wid IS NULL OR warehouse_or_pharmacy_id = @Wid)",
			new
			{
				Now = now,
				Op = op,
				CutoffInt = cutoffInt,
				Wid = string.IsNullOrWhiteSpace(req?.WarehouseOrPharmacyId) ? null : req.WarehouseOrPharmacyId.Trim()
			});

		if (_auditTrail != null && affected > 0)
		{
			await _auditTrail.RecordAuditAsync("INV_EXPIRY_AUTO_FREEZE", "inv_drug_batches", req?.WarehouseOrPharmacyId ?? "ALL",
				$"{reason}：自动冻结 {affected} 个批次（阈值<{cutoffInt}）", op, _userSession?.DisplayName);
		}

		return ApiResponse<object>.Success(new { FrozenCount = affected, CutoffDateInt = cutoffInt },
			affected > 0 ? $"效期安全巡检完成，已自动冻结锁定 {affected} 个到期/近效期批次" : "效期安全巡检完成，当前无逾期未冻结库存批次");
	}
}
