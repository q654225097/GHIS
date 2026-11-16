using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Inventory.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inventory.Controllers;

[ApiController]
[Route("api/v4/inventory")]
public class InventoryController : ControllerBase
{
	public sealed record UpsertDrugRequestDto(string? Id, string DrugCode, string DrugName, string Spec, string UnitName, decimal Price, decimal CostPrice, int AntibioticLevel = 0, short IsNarcotic = 0, short IsSkinTestRequired = 0, short IsActive = 1, string? OperatorId = null, string? AdjustReason = null);

	public sealed record ToggleDrugActiveRequestDto(bool IsActive, string? OperatorId = null);

	public sealed record AdjustDrugPriceRequestDto(decimal NewRetailPrice, decimal NewCostPrice, string AdjustReason, string? OperatorId = null);

	public sealed record DrugStockSummaryDto(string WarehouseOrPharmacyId, string DrugId, string DrugCode, string DrugName, string Spec, string UnitName, decimal RetailPrice, decimal CostPrice, int TotalQuantity, int AvailableFefoQuantity, int FrozenQuantity, int BatchCount, int EarliestExpiryDateInt, decimal TotalCostAmount, decimal TotalRetailAmount);

	public sealed record StockInRequestDto(string WarehouseOrPharmacyId, string DrugId, string BatchNo, int ExpiryDateInt, int Quantity, string? InvoiceNo = null, decimal? ColdChainTempCelsius = null, decimal? MaxAllowedColdChainTempCelsius = null, string? OperatorId = null, string? RequestKey = null);

	public sealed record FreezeBatchRequestDto(bool IsFrozen, string? Reason = null, string? OperatorId = null);

	public sealed record StockTakeAdjustRequestDto(int ActualQuantity, string Reason, string? OperatorId = null);

	public sealed record DrugFinanceItemBreakdownDto(string DrugId, string DrugCode, string DrugName, string Spec, string UnitName, decimal CurrentCostPrice, decimal CurrentRetailPrice, int PurchasedQuantity, decimal PurchasedCostAmount, int DispensedQuantity, decimal DispensedSalesAmount, int ReturnedQuantity, decimal ReturnedSalesAmount, int CurrentStockQuantity, decimal CurrentStockCostValue, decimal CurrentStockRetailValue);

public sealed record DrugFinanceReconciliationReportDto(int TotalStockInEvents, int TotalPurchasedQuantity, decimal TotalProcurementCostAmount, decimal TotalPurchasedRetailValuation, int TotalDispenseEvents, int TotalDispensedQuantity, decimal TotalDispensedSalesAmount, decimal TotalDispensedCostAmount, int TotalReturnEvents, int TotalReturnedQuantity, decimal TotalReturnedSalesAmount, decimal NetDrugSalesRevenue, decimal NetDrugGrossMargin, int TotalPriceAdjustCount, decimal TotalPriceDiffImpactAmount, decimal CurrentTotalInventoryCostValue, decimal CurrentTotalInventoryRetailValue, IReadOnlyList<DrugFinanceItemBreakdownDto> ItemBreakdowns, int TotalTransferEvents = 0, int TotalStockAdjustmentEvents = 0);

	public sealed record CreateTransferOrIssueRequestDto(string SourceWarehouseId, string TargetWarehouseId, string DrugId, int Quantity, string TransferType = "INTER_WAREHOUSE", string? BatchId = null, string? ApplicantId = null, string? OperatorId = null, string? Remark = null, string? RequestKey = null);

	private readonly IRepository<InvDrug> _drugRepo;

	private readonly IRepository<InvDrugBatch> _batchRepo;

	private readonly IRepository<InvStockLedger> _ledgerRepo;

	private readonly IRepository<InvDrugPriceLog> _priceLogRepo;

	private readonly IRepository<InvTransferOrder> _transferRepo;

	private readonly IDbConnectionFactory _connFactory;

	private readonly IAuditTrailContract? _auditTrail;

	private readonly IUserSession? _userSession;

	private readonly IIdempotencyContract? _idempotency;

	public InventoryController(IRepository<InvDrug> drugRepo, IRepository<InvDrugBatch> batchRepo, IRepository<InvStockLedger> ledgerRepo, IDbConnectionFactory connFactory, IAuditTrailContract? auditTrail = null, IRepository<InvDrugPriceLog>? priceLogRepo = null, IRepository<InvTransferOrder>? transferRepo = null, IUserSession? userSession = null, IIdempotencyContract? idempotency = null)
	{
		_drugRepo = drugRepo;
		_batchRepo = batchRepo;
		_ledgerRepo = ledgerRepo;
		_connFactory = connFactory;
		_auditTrail = auditTrail;
		_priceLogRepo = priceLogRepo ?? new DapperRepository<InvDrugPriceLog>(connFactory, "inv_drug_price_logs");
		_transferRepo = transferRepo ?? new DapperRepository<InvTransferOrder>(connFactory, "inv_transfer_orders");
		_userSession = userSession;
		_idempotency = idempotency;
	}

	[HttpGet("drugs")]
	public async Task<ApiResponse<IReadOnlyList<InvDrug>>> GetDrugs([FromQuery] bool activeOnly = true, [FromQuery] string? keyword = null)
	{
		string sql = (activeOnly ? "SELECT * FROM inv_drugs WHERE is_active = 1 ORDER BY drug_code ASC" : "SELECT * FROM inv_drugs ORDER BY drug_code ASC");
		IReadOnlyList<InvDrug> drugs = await _drugRepo.QueryAsync(sql);
		if (!string.IsNullOrWhiteSpace(keyword))
		{
			string kw = keyword.Trim();
			drugs = drugs.Where((InvDrug d) => d.DrugCode.Contains(kw, StringComparison.OrdinalIgnoreCase) || d.DrugName.Contains(kw, StringComparison.OrdinalIgnoreCase) || d.Spec.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		return ApiResponse<IReadOnlyList<InvDrug>>.Success(drugs);
	}

	[HttpPost("drugs")]
	public async Task<ApiResponse<InvDrug>> UpsertDrug([FromBody] UpsertDrugRequestDto req)
	{
		if (!IsAdmin())
		{
			return ApiResponse<InvDrug>.Fail(403, "仅系统管理员可以维护药品基础数据");
		}
		if (string.IsNullOrWhiteSpace(req.DrugCode) || string.IsNullOrWhiteSpace(req.DrugName))
		{
			return ApiResponse<InvDrug>.Fail(400, "药品编码与药品名称不能为空");
		}
		if (req.Price < 0m || req.CostPrice < 0m)
		{
			return ApiResponse<InvDrug>.Fail(400, "药品零售单价与采购成本价不能为负数");
		}
		var idIsExplicit = !string.IsNullOrWhiteSpace(req.Id);
		string code = req.DrugCode.Trim().ToUpperInvariant();
		var isNewDrug = string.IsNullOrWhiteSpace(req.Id) &&
		                await _drugRepo.QuerySingleOrDefaultAsync(
			                "SELECT * FROM inv_drugs WHERE drug_code = @Code",
			                new { Code = code }) == null;
		var explicitIdIsNew = idIsExplicit &&
		                      await _drugRepo.QuerySingleOrDefaultAsync(
			                      "SELECT * FROM inv_drugs WHERE id = @Id",
			                      new { Id = req.Id!.Trim() }) == null;
		if (!isNewDrug && !explicitIdIsNew && (string.IsNullOrWhiteSpace(req.AdjustReason) ||
		                   string.Equals(req.AdjustReason.Trim(), "药品主数据维护",
			                   StringComparison.Ordinal)))
		{
			return ApiResponse<InvDrug>.Fail(400, "修改药品时请填写本次维护或调价的依据");
		}
		InvDrug invDrug = (string.IsNullOrWhiteSpace(req.Id) ? null : (await _drugRepo.GetByIdAsync(req.Id)));
		InvDrug invDrug2 = invDrug;
		if (invDrug2 == null)
		{
			invDrug2 = await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new
			{
				Code = code
			});
		}
		InvDrug existing = invDrug2;
		string operatorId = CurrentUserId();
		long nowMs = SystemTime.UtcNowMs;
		InvDrug savedDrug;
		try
		{
			savedDrug = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
			{
			if (existing != null)
			{
				decimal oldRetail = existing.Price;
				decimal oldCost = existing.CostPrice;
				bool priceChanged = Math.Abs(oldRetail - req.Price) > 0.0001m || Math.Abs(oldCost - req.CostPrice) > 0.0001m;
				existing.DrugCode = code;
				existing.DrugName = req.DrugName.Trim();
				existing.Spec = (string.IsNullOrWhiteSpace(req.Spec) ? existing.Spec : req.Spec.Trim());
				existing.UnitName = (string.IsNullOrWhiteSpace(req.UnitName) ? existing.UnitName : req.UnitName.Trim());
				existing.Price = req.Price;
				existing.CostPrice = req.CostPrice;
				existing.AntibioticLevel = Math.Clamp(req.AntibioticLevel, 0, 2);
				existing.IsNarcotic = req.IsNarcotic;
				existing.IsSkinTestRequired = req.IsSkinTestRequired;
				existing.IsActive = req.IsActive;
				existing.UpdatedBy = operatorId;
				await _drugRepo.UpdateAsync(existing, ct);
				if (priceChanged)
				{
					int totalStock = (await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches WHERE drug_id = @DrugId", new
					{
						DrugId = existing.Id
					}, ct)).Sum((InvDrugBatch b) => b.Quantity);
					decimal diffAmount = (req.Price - oldRetail) * (decimal)totalStock;
					InvDrugPriceLog priceLog = new InvDrugPriceLog
					{
						AdjustNo = $"PRC-MNT-{DateTime.UtcNow:yyMMddHHmmssfff}",
						DrugId = existing.Id,
						DrugCode = existing.DrugCode,
						DrugName = existing.DrugName,
						OldRetailPrice = oldRetail,
						NewRetailPrice = req.Price,
						OldCostPrice = oldCost,
						NewCostPrice = req.CostPrice,
						AffectedStockQuantity = totalStock,
						PriceDiffAmount = diffAmount,
						AdjustReason = string.IsNullOrWhiteSpace(req.AdjustReason)
							? "药品主数据维护同步调价"
							: req.AdjustReason.Trim(),
						EffectiveAtMs = nowMs,
						OperatorId = operatorId,
						CreatedBy = operatorId,
						UpdatedBy = operatorId
					};
					await _priceLogRepo.InsertAsync(priceLog, ct);
				}
				return existing;
			}
			InvDrug newDrug = new InvDrug
			{
				Id = (string.IsNullOrWhiteSpace(req.Id) ? ("DRUG-" + Guid.NewGuid().ToString("N").Substring(0, 8)
					.ToUpper()) : req.Id.Trim()),
				DrugCode = code,
				DrugName = req.DrugName.Trim(),
				Spec = (string.IsNullOrWhiteSpace(req.Spec) ? "标准规格" : req.Spec.Trim()),
				UnitName = (string.IsNullOrWhiteSpace(req.UnitName) ? "盒" : req.UnitName.Trim()),
				Price = req.Price,
				CostPrice = req.CostPrice,
				AntibioticLevel = Math.Clamp(req.AntibioticLevel, 0, 2),
				IsNarcotic = req.IsNarcotic,
				IsSkinTestRequired = req.IsSkinTestRequired,
				IsActive = req.IsActive,
				CreatedBy = operatorId,
				UpdatedBy = operatorId
			};
			await _drugRepo.InsertAsync(newDrug, ct);
			InvDrugPriceLog initLog = new InvDrugPriceLog
			{
				AdjustNo = $"PRC-INIT-{DateTime.UtcNow:yyMMddHHmmssfff}",
				DrugId = newDrug.Id,
				DrugCode = newDrug.DrugCode,
				DrugName = newDrug.DrugName,
				OldRetailPrice = req.Price,
				NewRetailPrice = req.Price,
				OldCostPrice = req.CostPrice,
				NewCostPrice = req.CostPrice,
				AffectedStockQuantity = 0,
				PriceDiffAmount = 0m,
				AdjustReason = "新药建档初始核定定价",
				EffectiveAtMs = nowMs,
				OperatorId = operatorId,
				CreatedBy = operatorId,
				UpdatedBy = operatorId
			};
			await _priceLogRepo.InsertAsync(initLog, ct);
				return newDrug;
			});
		}
		catch (InvalidOperationException ex)
		{
			return ApiResponse<InvDrug>.Fail(400, ex.Message);
		}
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("INV_DRUG_UPSERT", "InvDrug", savedDrug.Id, $"维护药品基础字典 [{savedDrug.DrugCode} - {savedDrug.DrugName}] 规格={savedDrug.Spec}, 零售价=¥{savedDrug.Price:0.00}, 成本价=¥{savedDrug.CostPrice:0.00}", operatorId);
		}
		return ApiResponse<InvDrug>.Success(savedDrug, "药品基础数据保存成功");
	}

	[HttpPost("drugs/{drugId}/toggle-active")]
	public async Task<ApiResponse<InvDrug>> ToggleDrugActive(string drugId, [FromBody] ToggleDrugActiveRequestDto req)
	{
		if (!IsAdmin())
		{
			return ApiResponse<InvDrug>.Fail(403, "仅系统管理员可以启停药品");
		}
		InvDrug invDrug = await _drugRepo.GetByIdAsync(drugId);
		if (invDrug == null)
		{
			invDrug = await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new
			{
				Code = drugId
			});
		}
		InvDrug drug = invDrug;
		if (drug == null)
		{
			return ApiResponse<InvDrug>.Fail(404, "未找到指定药品");
		}
		drug.IsActive = (short)(req.IsActive ? 1 : 0);
		drug.UpdatedBy = CurrentUserId();
		await _drugRepo.UpdateAsync(drug);
		return ApiResponse<InvDrug>.Success(drug, req.IsActive ? "药品已恢复启用" : "药品已停用");
	}

	[HttpPost("drugs/{drugId}/adjust-price")]
	public async Task<ApiResponse<InvDrugPriceLog>> AdjustDrugPrice(string drugId, [FromBody] AdjustDrugPriceRequestDto req)
	{
		if (!IsAdmin())
		{
			return ApiResponse<InvDrugPriceLog>.Fail(403, "仅系统管理员可以调整药品价格");
		}
		if (req.NewRetailPrice < 0m || req.NewCostPrice < 0m)
		{
			return ApiResponse<InvDrugPriceLog>.Fail(400, "调价后的零售价与成本价不能为负数");
		}
		if (string.IsNullOrWhiteSpace(req.AdjustReason))
		{
			return ApiResponse<InvDrugPriceLog>.Fail(400, "必须填写药品调价依据/原因（如集采降价、物价局调价文号）");
		}
		InvDrug invDrug = await _drugRepo.GetByIdAsync(drugId);
		if (invDrug == null)
		{
			invDrug = await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new
			{
				Code = drugId
			});
		}
		InvDrug drug = invDrug;
		if (drug == null)
		{
			return ApiResponse<InvDrugPriceLog>.Fail(404, "未找到药品 [" + drugId + "]");
		}
		string operatorId = CurrentUserId();
		long nowMs = SystemTime.UtcNowMs;
		InvDrugPriceLog priceLog = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			decimal oldRetail = drug.Price;
			decimal oldCost = drug.CostPrice;
			int totalStockQty = (await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches WHERE drug_id = @DrugId", new
			{
				DrugId = drug.Id
			}, ct)).Sum((InvDrugBatch b) => b.Quantity);
			decimal priceDiffAmount = (req.NewRetailPrice - oldRetail) * (decimal)totalStockQty;
			drug.Price = req.NewRetailPrice;
			drug.CostPrice = req.NewCostPrice;
			drug.UpdatedBy = operatorId;
			await _drugRepo.UpdateAsync(drug, ct);
			InvDrugPriceLog log = new InvDrugPriceLog
			{
				AdjustNo = $"PRC-ADJ-{DateTime.UtcNow:yyMMddHHmmssfff}",
				DrugId = drug.Id,
				DrugCode = drug.DrugCode,
				DrugName = drug.DrugName,
				OldRetailPrice = oldRetail,
				NewRetailPrice = req.NewRetailPrice,
				OldCostPrice = oldCost,
				NewCostPrice = req.NewCostPrice,
				AffectedStockQuantity = totalStockQty,
				PriceDiffAmount = priceDiffAmount,
				AdjustReason = req.AdjustReason.Trim(),
				EffectiveAtMs = nowMs,
				OperatorId = operatorId,
				CreatedBy = operatorId,
				UpdatedBy = operatorId
			};
			await _priceLogRepo.InsertAsync(log, ct);
			return log;
		});
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("INV_PRICE_ADJUST", "InvDrugPriceLog", priceLog.Id, $"药品调价单 [{priceLog.AdjustNo}]: [{drug.DrugName}] 零售价 ¥{priceLog.OldRetailPrice:0.00}->¥{priceLog.NewRetailPrice:0.00}，成本价 ¥{priceLog.OldCostPrice:0.00}->¥{priceLog.NewCostPrice:0.00}，涉及全院库存 {priceLog.AffectedStockQuantity}{drug.UnitName}，调价损益 ¥{priceLog.PriceDiffAmount:0.00}", operatorId);
		}
		return ApiResponse<InvDrugPriceLog>.Success(priceLog, $"药品调价生效！涉及在库总量 {priceLog.AffectedStockQuantity}{drug.UnitName}，库存零售估值差额 ¥{priceLog.PriceDiffAmount:0.00}");
	}

	[HttpGet("price-logs")]
	public async Task<ApiResponse<IReadOnlyList<InvDrugPriceLog>>> GetPriceLogs([FromQuery] string? drugId = null)
	{
		return ApiResponse<IReadOnlyList<InvDrugPriceLog>>.Success(await _priceLogRepo.QueryAsync("SELECT * FROM inv_drug_price_logs\nWHERE (@DrugId IS NULL OR drug_id = @DrugId OR drug_code = @DrugId)\nORDER BY effective_at_ms DESC, created_at_ms DESC", new
		{
			DrugId = drugId
		}));
	}

	[HttpGet("stock-summary")]
	public async Task<ApiResponse<IReadOnlyList<DrugStockSummaryDto>>> GetStockSummary([FromQuery] string? warehouseOrPharmacyId = null, [FromQuery] string? drugId = null)
	{
		Dictionary<string, InvDrug> drugs = (await _drugRepo.GetAllAsync()).ToDictionary((InvDrug d) => d.Id, StringComparer.OrdinalIgnoreCase);
		IReadOnlyList<InvDrugBatch> batches = await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches\nWHERE (@WarehouseId IS NULL OR warehouse_or_pharmacy_id = @WarehouseId)\n  AND (@DrugId IS NULL OR drug_id = @DrugId)", new
		{
			WarehouseId = warehouseOrPharmacyId,
			DrugId = drugId
		});
		int today = SystemTime.TodayInt;
		List<DrugStockSummaryDto> grouped = (from x in (from b in batches
				group b by (WarehouseOrPharmacyId: b.WarehouseOrPharmacyId, DrugId: b.DrugId)).Select((IGrouping<(string WarehouseOrPharmacyId, string DrugId), InvDrugBatch> g) =>
			{
				drugs.TryGetValue(g.Key.DrugId, out var value);
				int num = g.Sum((InvDrugBatch x) => x.Quantity);
				int availableFefoQuantity = g.Where((InvDrugBatch x) => x.IsFrozen == 0 && x.ExpiryDateInt >= today && x.Quantity > 0).Sum((InvDrugBatch x) => x.Quantity);
				int frozenQuantity = g.Where((InvDrugBatch x) => x.IsFrozen != 0).Sum((InvDrugBatch x) => x.Quantity);
				int earliestExpiryDateInt = (from x in g
					where x.Quantity > 0
					select x.ExpiryDateInt).DefaultIfEmpty(20991231).Min();
				decimal num2 = value?.Price ?? 0m;
				decimal num3 = value?.CostPrice ?? 0m;
				return new DrugStockSummaryDto(g.Key.WarehouseOrPharmacyId, g.Key.DrugId, value?.DrugCode ?? g.Key.DrugId, value?.DrugName ?? g.Key.DrugId, value?.Spec ?? "-", value?.UnitName ?? "单位", num2, num3, num, availableFefoQuantity, frozenQuantity, g.Count(), earliestExpiryDateInt, (decimal)num * num3, (decimal)num * num2);
			})
			orderby x.WarehouseOrPharmacyId, x.DrugCode
			select x).ToList();
		return ApiResponse<IReadOnlyList<DrugStockSummaryDto>>.Success(grouped);
	}

	[HttpGet("batches")]
	public async Task<ApiResponse<IReadOnlyList<InvDrugBatch>>> GetBatches([FromQuery] string? warehouseOrPharmacyId = null, [FromQuery] string? drugId = null)
	{
		return ApiResponse<IReadOnlyList<InvDrugBatch>>.Success(await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches\nWHERE (@WarehouseId IS NULL OR warehouse_or_pharmacy_id = @WarehouseId)\n  AND (@DrugId IS NULL OR drug_id = @DrugId)\nORDER BY expiry_date_int ASC, created_at_ms ASC", new
		{
			WarehouseId = warehouseOrPharmacyId,
			DrugId = drugId
		}));
	}

	[HttpPost("batches/stock-in")]
	public async Task<ApiResponse<InvDrugBatch>> StockIn([FromBody] StockInRequestDto req)
	{
		if (!IsInventoryOperator())
		{
			return ApiResponse<InvDrugBatch>.Fail(403, "仅药库药师或系统管理员可以办理采购入库");
		}
		if (!await CanStockInAsync(req.WarehouseOrPharmacyId))
		{
			return ApiResponse<InvDrugBatch>.Fail(403, "采购入库必须由具有该药库岗位权限的药库药师办理");
		}
		if (!IsInOrganizationWarehouse(req.WarehouseOrPharmacyId))
		{
			return ApiResponse<InvDrugBatch>.Fail(403, "采购入库只能入全院中心药库或冷链生物制品专库，不能直接入零售药房");
		}
		if (req.Quantity <= 0)
		{
			return ApiResponse<InvDrugBatch>.Fail(400, "入库数量必须大于 0");
		}
		if (string.IsNullOrWhiteSpace(req.BatchNo))
		{
			return ApiResponse<InvDrugBatch>.Fail(400, "生产批号不能为空");
		}
		if (req.ExpiryDateInt < SystemTime.TodayInt)
		{
			return ApiResponse<InvDrugBatch>.Fail(400, $"严禁入库已过期药品批次（效期 {req.ExpiryDateInt} 早于今日 {SystemTime.TodayInt}）");
		}
		decimal maxTemp = req.MaxAllowedColdChainTempCelsius ?? 8.0m;
		if (req.ColdChainTempCelsius.HasValue && req.ColdChainTempCelsius.Value > maxTemp)
		{
			return ApiResponse<InvDrugBatch>.Fail(400, $"冷链药品收货温度超标（实测 {req.ColdChainTempCelsius.Value:0.0}°C > 阈值 {maxTemp:0.0}°C），GSP 强阻断拒绝入库！");
		}
		InvDrug invDrug = await _drugRepo.GetByIdAsync(req.DrugId);
		if (invDrug == null)
		{
			invDrug = await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new
			{
				Code = req.DrugId
			});
		}
		InvDrug drug = invDrug;
		if (drug == null)
		{
			return ApiResponse<InvDrugBatch>.Fail(404, "药品定义 [" + req.DrugId + "] 不存在");
		}
		if (drug.IsActive != 1)
		{
			return ApiResponse<InvDrugBatch>.Fail(400, "药品已停用，不能继续采购入库");
		}
		string requestKey = "INV_STOCK_IN_" + (string.IsNullOrWhiteSpace(req.RequestKey) ? Guid.NewGuid().ToString("N") : req.RequestKey.Trim());
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			var (isDuplicate, cachedJson, isConflict) = await _idempotency.TryAcquireAsync(requestKey, JsonSerializer.Serialize(req));
			if (isConflict)
			{
				return ApiResponse<InvDrugBatch>.Fail(409, "请求幂等键冲突：已被不同的入库请求使用");
			}
			if (isDuplicate)
			{
				if (string.IsNullOrWhiteSpace(cachedJson))
					return ApiResponse<InvDrugBatch>.Fail(409, "相同入库请求正在处理或上次未完成，请刷新库存后确认");
				try
				{
					InvDrugBatch? cached = JsonSerializer.Deserialize<InvDrugBatch>(cachedJson);
					if (cached != null)
						return ApiResponse<InvDrugBatch>.Success(cached, "幂等命中：已返回原入库批次，未重复增加库存");
				}
				catch (JsonException)
				{
				}
			}
		}
		InvDrugBatch batch;
		try
		{
			batch = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
			{
				string operatorId = CurrentUserId();
				InvDrugBatch existing = await _batchRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drug_batches\nWHERE warehouse_or_pharmacy_id = @WarehouseId AND drug_id = @DrugId AND batch_no = @BatchNo", new
				{
					WarehouseId = req.WarehouseOrPharmacyId,
					DrugId = drug.Id,
					BatchNo = req.BatchNo.Trim()
				}, ct);
				if (existing != null && existing.ExpiryDateInt != req.ExpiryDateInt)
				{
					throw new InvalidOperationException(
						$"同一批号 [{existing.BatchNo}] 已登记效期 {existing.ExpiryDateInt}，本次录入 {req.ExpiryDateInt}，效期不一致，禁止合并入库");
				}
				InvDrugBatch targetBatch;
				if (existing != null)
				{
					existing.Quantity += req.Quantity;
					existing.ExpiryDateInt = req.ExpiryDateInt;
					existing.InvoiceNo = req.InvoiceNo ?? existing.InvoiceNo;
					existing.ColdChainTempCelsius = req.ColdChainTempCelsius ?? existing.ColdChainTempCelsius;
					existing.UpdatedBy = operatorId;
					await _batchRepo.UpdateAsync(existing, ct);
					targetBatch = existing;
				}
				else
				{
					targetBatch = new InvDrugBatch
					{
						WarehouseOrPharmacyId = req.WarehouseOrPharmacyId,
						DrugId = drug.Id,
						BatchNo = req.BatchNo.Trim(),
						ExpiryDateInt = req.ExpiryDateInt,
						InvoiceNo = req.InvoiceNo,
						ColdChainTempCelsius = req.ColdChainTempCelsius,
						Quantity = req.Quantity,
						IsFrozen = 0,
						CreatedBy = operatorId,
						UpdatedBy = operatorId
					};
					await _batchRepo.InsertAsync(targetBatch, ct);
				}
				InvStockLedger ledger = new InvStockLedger
				{
					WarehouseOrPharmacyId = req.WarehouseOrPharmacyId,
					DrugId = drug.Id,
					BatchId = targetBatch.Id,
					ChangeType = "IN_PURCHASE",
					ChangeQuantity = req.Quantity,
					BalanceAfter = targetBatch.Quantity,
					ReferenceDocNo = (req.InvoiceNo ?? $"IN-{DateTime.UtcNow:yyMMddHHmmssfff}"),
					UnitCostPrice = drug.CostPrice,
					UnitRetailPrice = drug.Price,
					CreatedBy = operatorId,
					UpdatedBy = operatorId
				};
				await _ledgerRepo.InsertAsync(ledger, ct);
				return targetBatch;
			});
		}
		catch (InvalidOperationException ex)
		{
			return ApiResponse<InvDrugBatch>.Fail(400, ex.Message);
		}
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			await _idempotency.SaveResponseAsync(requestKey, JsonSerializer.Serialize(batch));
		}
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("INV_STOCK_IN", "InvDrugBatch", batch.Id, $"采购入库：药品 [{drug.DrugName}] 批号 [{batch.BatchNo}] 效期 {batch.ExpiryDateInt} 入库 +{req.Quantity}{drug.UnitName}，结余 {batch.Quantity}{drug.UnitName}", CurrentUserId(), _userSession?.DisplayName, req.WarehouseOrPharmacyId);
		}
		return ApiResponse<InvDrugBatch>.Success(batch, "药品批次验收入库成功并已登记库存台账");
	}

	[HttpPost("batches/{batchId}/freeze")]
	public async Task<ApiResponse<InvDrugBatch>> SetBatchFreezeStatus(string batchId, [FromBody] FreezeBatchRequestDto req)
	{
		if (!IsInventoryOperator())
		{
			return ApiResponse<InvDrugBatch>.Fail(403, "仅药库药师或系统管理员可以冻结或解冻批次");
		}
		InvDrugBatch batch = await _batchRepo.GetByIdAsync(batchId);
		if (batch == null)
		{
			return ApiResponse<InvDrugBatch>.Fail(404, "药品批次不存在");
		}
		batch.IsFrozen = (short)(req.IsFrozen ? 1 : 0);
		string operatorId = CurrentUserId();
		batch.UpdatedBy = operatorId;
		await _batchRepo.UpdateAsync(batch);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync(req.IsFrozen ? "INV_BATCH_FREEZE" : "INV_BATCH_UNFREEZE", "InvDrugBatch", batch.Id, $"批次 [{batch.BatchNo}] 状态变更为 {(req.IsFrozen ? "冻结锁定" : "解除冻结")}，原因：{req.Reason ?? "质量管控"}", operatorId);
		}
		return ApiResponse<InvDrugBatch>.Success(batch, req.IsFrozen ? "药品批次已冻结锁定，禁止发药出库" : "药品批次已解除冻结");
	}

	[HttpPost("batches/{batchId}/stock-take")]
	public async Task<ApiResponse<InvDrugBatch>> AdjustBatchByStockTake(string batchId, [FromBody] StockTakeAdjustRequestDto req)
	{
		if (!IsInventoryOperator())
		{
			return ApiResponse<InvDrugBatch>.Fail(403, "仅药库药师或系统管理员可以执行库存盘点");
		}
		if (req.ActualQuantity < 0)
		{
			return ApiResponse<InvDrugBatch>.Fail(400, "盘点实盘数量不能为负数");
		}
		InvDrugBatch batch = await _batchRepo.GetByIdAsync(batchId);
		if (batch == null)
		{
			return ApiResponse<InvDrugBatch>.Fail(404, "未找到待盘点药品批次");
		}
		int diff = req.ActualQuantity - batch.Quantity;
		string operatorId = CurrentUserId();
		InvDrug? stockTakeDrug = await _drugRepo.GetByIdAsync(batch.DrugId);
		InvDrugBatch updatedBatch = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			batch.Quantity = req.ActualQuantity;
			batch.UpdatedBy = operatorId;
			await _batchRepo.UpdateAsync(batch, ct);
			InvStockLedger ledger = new InvStockLedger
			{
				WarehouseOrPharmacyId = batch.WarehouseOrPharmacyId,
				DrugId = batch.DrugId,
				BatchId = batch.Id,
				ChangeType = "INV_ADJUST",
				ChangeQuantity = diff,
				BalanceAfter = batch.Quantity,
				ReferenceDocNo = $"CHK-{DateTime.UtcNow:yyMMddHHmmss}",
				UnitCostPrice = stockTakeDrug?.CostPrice,
				UnitRetailPrice = stockTakeDrug?.Price,
				CreatedBy = operatorId,
				UpdatedBy = operatorId
			};
			await _ledgerRepo.InsertAsync(ledger, ct);
			return batch;
		});
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("INV_STOCK_TAKE", "InvDrugBatch", batch.Id, $"库存盘点调整：批次 [{batch.BatchNo}] 变动 {((diff >= 0) ? $"+{diff}" : diff.ToString())}，实盘结余 {batch.Quantity}，原因：{req.Reason}", operatorId, null, batch.WarehouseOrPharmacyId);
		}
		return ApiResponse<InvDrugBatch>.Success(updatedBatch, $"库存盘点损益调整完成（变动 {((diff >= 0) ? $"+{diff}" : diff.ToString())}，最新结余 {updatedBatch.Quantity}）");
	}

	[HttpGet("finance-report")]
	public async Task<ApiResponse<DrugFinanceReconciliationReportDto>> GetDrugFinanceReport([FromQuery] string? warehouseOrPharmacyId = null)
	{
		IReadOnlyList<InvDrug> drugs = await _drugRepo.GetAllAsync();
		Dictionary<string, InvDrug> drugsById = drugs.ToDictionary((InvDrug d) => d.Id, StringComparer.OrdinalIgnoreCase);
		IReadOnlyList<InvDrugBatch> batches = await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches WHERE (@WhId IS NULL OR warehouse_or_pharmacy_id = @WhId)", new
		{
			WhId = warehouseOrPharmacyId
		});
		IReadOnlyList<InvStockLedger> ledgers = await _ledgerRepo.QueryAsync("SELECT * FROM inv_stock_ledgers WHERE (@WhId IS NULL OR warehouse_or_pharmacy_id = @WhId)", new
		{
			WhId = warehouseOrPharmacyId
		});
		IReadOnlyList<InvDrugPriceLog> priceLogs = await _priceLogRepo.GetAllAsync();
		Dictionary<string, List<InvDrugPriceLog>> priceLogsByDrug = priceLogs
			.GroupBy((InvDrugPriceLog p) => p.DrugId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary((IGrouping<string, InvDrugPriceLog> g) => g.Key,
				(IGrouping<string, InvDrugPriceLog> g) => g.OrderBy((InvDrugPriceLog p) => p.EffectiveAtMs).ToList(),
				StringComparer.OrdinalIgnoreCase);

		// Historical stock events keep the prices in force when they occurred. The current
		// drug master is only a fallback for legacy ledgers written before price snapshots existed.
		(decimal Cost, decimal Retail) EventPrices(InvStockLedger ledger)
		{
			if (ledger.UnitCostPrice.HasValue && ledger.UnitRetailPrice.HasValue)
				return (ledger.UnitCostPrice.Value, ledger.UnitRetailPrice.Value);

			InvDrug? ledgerDrug = drugsById.GetValueOrDefault(ledger.DrugId);
			decimal cost = ledgerDrug?.CostPrice ?? 0m;
			decimal retail = ledgerDrug?.Price ?? 0m;
			if (priceLogsByDrug.TryGetValue(ledger.DrugId, out List<InvDrugPriceLog>? logs))
			{
				InvDrugPriceLog? historical = logs.LastOrDefault((InvDrugPriceLog p) => p.EffectiveAtMs <= ledger.CreatedAtMs);
				if (historical != null)
					return (historical.NewCostPrice, historical.NewRetailPrice);
				InvDrugPriceLog? earliest = logs.FirstOrDefault();
				if (earliest != null)
					return (earliest.OldCostPrice, earliest.OldRetailPrice);
			}
			return (cost, retail);
		}

		static int MovementQuantity(InvStockLedger ledger) => Math.Abs(ledger.ChangeQuantity);
		Dictionary<string, decimal> purchasedCostAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, decimal> purchasedRetailAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, decimal> dispensedCostAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, decimal> returnedCostAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
		List<DrugFinanceItemBreakdownDto> breakdowns = new List<DrugFinanceItemBreakdownDto>();
		foreach (InvDrug d in drugs.OrderBy((InvDrug x) => x.DrugCode))
		{
			List<InvStockLedger> dLedgers = ledgers.Where((InvStockLedger l) => l.DrugId == d.Id).ToList();
			List<InvDrugBatch> dBatches = batches.Where((InvDrugBatch b) => b.DrugId == d.Id).ToList();
			List<InvStockLedger> purchaseEvents = dLedgers.Where((InvStockLedger l) => l.ChangeType == "IN_PURCHASE").ToList();
			List<InvStockLedger> dispenseEvents = dLedgers.Where((InvStockLedger l) => l.ChangeType == "OUT_DISPENSE").ToList();
			List<InvStockLedger> returnEvents = dLedgers.Where((InvStockLedger l) => l.ChangeType == "IN_RETURN").ToList();
			int purchasedQty = purchaseEvents.Sum(MovementQuantity);
			int dispensedQty = dispenseEvents.Sum(MovementQuantity);
			int returnedQty = returnEvents.Sum(MovementQuantity);
			decimal purchasedCost = purchaseEvents.Sum((InvStockLedger l) => EventPrices(l).Cost * MovementQuantity(l));
			decimal purchasedRetail = purchaseEvents.Sum((InvStockLedger l) => EventPrices(l).Retail * MovementQuantity(l));
			decimal dispensedSales = dispenseEvents.Sum((InvStockLedger l) => EventPrices(l).Retail * MovementQuantity(l));
			decimal dispensedCost = dispenseEvents.Sum((InvStockLedger l) => EventPrices(l).Cost * MovementQuantity(l));
			decimal returnedSales = returnEvents.Sum((InvStockLedger l) => EventPrices(l).Retail * MovementQuantity(l));
			decimal returnedCost = returnEvents.Sum((InvStockLedger l) => EventPrices(l).Cost * MovementQuantity(l));
			int currentStockQty = dBatches.Sum((InvDrugBatch b) => b.Quantity);
			breakdowns.Add(new DrugFinanceItemBreakdownDto(d.Id, d.DrugCode, d.DrugName, d.Spec, d.UnitName, d.CostPrice, d.Price, purchasedQty, purchasedCost, dispensedQty, dispensedSales, returnedQty, returnedSales, currentStockQty, (decimal)currentStockQty * d.CostPrice, (decimal)currentStockQty * d.Price));
			purchasedCostAmounts[d.Id] = purchasedCost;
			purchasedRetailAmounts[d.Id] = purchasedRetail;
			dispensedCostAmounts[d.Id] = dispensedCost;
			returnedCostAmounts[d.Id] = returnedCost;
		}
		int totalPurchasedQty = breakdowns.Sum((DrugFinanceItemBreakdownDto x) => x.PurchasedQuantity);
		decimal totalProcurementCost = purchasedCostAmounts.Values.Sum();
		decimal totalPurchasedRetail = purchasedRetailAmounts.Values.Sum();
		int totalDispensedQty = breakdowns.Sum((DrugFinanceItemBreakdownDto x) => x.DispensedQuantity);
		decimal totalDispensedSales = breakdowns.Sum((DrugFinanceItemBreakdownDto x) => x.DispensedSalesAmount);
		decimal totalDispensedCost = dispensedCostAmounts.Values.Sum();
		int totalReturnedQty = breakdowns.Sum((DrugFinanceItemBreakdownDto x) => x.ReturnedQuantity);
		decimal totalReturnedSales = breakdowns.Sum((DrugFinanceItemBreakdownDto x) => x.ReturnedSalesAmount);
		decimal totalReturnedCost = returnedCostAmounts.Values.Sum();
		decimal netSalesRevenue = totalDispensedSales - totalReturnedSales;
		decimal netCostOfSales = totalDispensedCost - totalReturnedCost;
		DrugFinanceReconciliationReportDto report = new DrugFinanceReconciliationReportDto(NetDrugGrossMargin: netSalesRevenue - netCostOfSales, TotalStockInEvents: Enumerable.Count(ledgers, (InvStockLedger l) => l.ChangeType == "IN_PURCHASE"), TotalPurchasedQuantity: totalPurchasedQty, TotalProcurementCostAmount: totalProcurementCost, TotalPurchasedRetailValuation: totalPurchasedRetail, TotalDispenseEvents: Enumerable.Count(ledgers, (InvStockLedger l) => l.ChangeType == "OUT_DISPENSE"), TotalDispensedQuantity: totalDispensedQty, TotalDispensedSalesAmount: totalDispensedSales, TotalDispensedCostAmount: totalDispensedCost, TotalReturnEvents: Enumerable.Count(ledgers, (InvStockLedger l) => l.ChangeType == "IN_RETURN"), TotalReturnedQuantity: totalReturnedQty, TotalReturnedSalesAmount: totalReturnedSales, NetDrugSalesRevenue: netSalesRevenue, TotalPriceAdjustCount: priceLogs.Count, TotalPriceDiffImpactAmount: priceLogs.Sum((InvDrugPriceLog p) => p.PriceDiffAmount), CurrentTotalInventoryCostValue: breakdowns.Sum((DrugFinanceItemBreakdownDto x) => x.CurrentStockCostValue), CurrentTotalInventoryRetailValue: breakdowns.Sum((DrugFinanceItemBreakdownDto x) => x.CurrentStockRetailValue), ItemBreakdowns: breakdowns, TotalTransferEvents: Enumerable.Count(ledgers, (InvStockLedger l) => l.ChangeType == "OUT_TRANSFER" || l.ChangeType == "OUT_WAREHOUSE_ISSUE"), TotalStockAdjustmentEvents: Enumerable.Count(ledgers, (InvStockLedger l) => l.ChangeType == "INV_ADJUST"));
		return ApiResponse<DrugFinanceReconciliationReportDto>.Success(report, "财务药品采购入库、临床销售与退药对账核算成功");
	}

	[HttpGet("transfers")]
	public async Task<ApiResponse<IReadOnlyList<InvTransferOrder>>> GetTransfers([FromQuery] string? sourceWarehouseId = null, [FromQuery] string? targetWarehouseId = null, [FromQuery] string? transferType = null)
	{
		return ApiResponse<IReadOnlyList<InvTransferOrder>>.Success(await _transferRepo.QueryAsync("SELECT * FROM inv_transfer_orders\nWHERE (@SrcId IS NULL OR source_warehouse_id = @SrcId)\n  AND (@DstId IS NULL OR target_warehouse_id = @DstId)\n  AND (@Type IS NULL OR transfer_type = @Type)\nORDER BY created_at_ms DESC", new
		{
			SrcId = sourceWarehouseId,
			DstId = targetWarehouseId,
			Type = transferType
		}));
	}

	[HttpPost("transfers")]
	public Task<ApiResponse<IReadOnlyList<InvTransferOrder>>> ExecuteMultiWarehouseTransfer([FromBody] CreateTransferOrIssueRequestDto req)
	{
		return ExecuteStockTransferInternalAsync(req, string.IsNullOrWhiteSpace(req.TransferType) ? "INTER_WAREHOUSE" : req.TransferType.Trim().ToUpperInvariant());
	}

	[HttpPost("warehouse-issue")]
	public Task<ApiResponse<IReadOnlyList<InvTransferOrder>>> ExecuteWarehouseIssueToPharmacy([FromBody] CreateTransferOrIssueRequestDto req)
	{
		return ExecuteStockTransferInternalAsync(req, "WAREHOUSE_ISSUE");
	}

	private async Task<ApiResponse<IReadOnlyList<InvTransferOrder>>> ExecuteStockTransferInternalAsync(CreateTransferOrIssueRequestDto req, string normalizedType)
	{
		if (!IsInventoryOperator())
		{
			return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(403, "仅药库药师或系统管理员可以执行调拨和药库发药");
		}
		string requestKey = "INV_" + normalizedType + "_" + (string.IsNullOrWhiteSpace(req.RequestKey) ? Guid.NewGuid().ToString("N") : req.RequestKey.Trim());
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			var (isDuplicate, cachedJson, isConflict) = await _idempotency.TryAcquireAsync(requestKey, JsonSerializer.Serialize(req));
			if (isConflict)
				return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(409, "请求幂等键冲突：已被不同的调拨请求使用");
			if (isDuplicate)
			{
				if (string.IsNullOrWhiteSpace(cachedJson))
					return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(409, "相同调拨请求正在处理或上次未完成，请刷新库存后确认");
				try
				{
					var cached = JsonSerializer.Deserialize<List<InvTransferOrder>>(cachedJson);
					if (cached != null)
						return ApiResponse<IReadOnlyList<InvTransferOrder>>.Success(cached, "幂等命中：已返回原调拨单，未重复扣减库存");
				}
				catch (JsonException)
				{
				}
			}
		}
		if (string.IsNullOrWhiteSpace(req.SourceWarehouseId) || string.IsNullOrWhiteSpace(req.TargetWarehouseId))
		{
			return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(400, "源库房（调出方/药库）与目标库房（调入方/药房）不能为空");
		}
		if (normalizedType == "WAREHOUSE_ISSUE")
		{
			if (!await CanIssueFromWarehouseAsync(req.SourceWarehouseId.Trim()))
				return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(403, "药库发药只能由该药库岗位操作员办理");
			if (!IsInOrganizationWarehouse(req.SourceWarehouseId))
				return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(400, "药库发药物流必须从全院中心药库或冷链生物制品专库发出");
			if (!IsInOrganizationPharmacy(req.TargetWarehouseId))
				return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(400, "药库发药的目标必须是有效的零售药房");
		}
		if (string.Equals(req.SourceWarehouseId.Trim(), req.TargetWarehouseId.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(400, "调出库房与调入库房不能相同");
		}
		if (req.Quantity <= 0)
		{
			return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(400, "调拨/药库发药数量必须大于 0");
		}
		InvDrug invDrug = await _drugRepo.GetByIdAsync(req.DrugId);
		if (invDrug == null)
		{
			invDrug = await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new
			{
				Code = req.DrugId
			});
		}
		InvDrug drug = invDrug;
		if (drug == null)
		{
			return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(404, "药品 [" + req.DrugId + "] 不存在");
		}
		int today = SystemTime.TodayInt;
		IReadOnlyList<InvDrugBatch> sourceBatches = await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches\nWHERE warehouse_or_pharmacy_id = @SourceId\n  AND drug_id = @DrugId\n  AND is_frozen = 0\n  AND expiry_date_int >= @Today\n  AND quantity > 0\nORDER BY expiry_date_int ASC, created_at_ms ASC", new
		{
			SourceId = req.SourceWarehouseId.Trim(),
			DrugId = drug.Id,
			Today = today
		});
		if (!string.IsNullOrWhiteSpace(req.BatchId))
		{
			sourceBatches = sourceBatches.Where((InvDrugBatch b) => b.Id == req.BatchId.Trim() || b.BatchNo.Equals(req.BatchId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
		}
		int totalAvailable = sourceBatches.Sum((InvDrugBatch b) => b.Quantity);
		if (totalAvailable < req.Quantity)
		{
			return ApiResponse<IReadOnlyList<InvTransferOrder>>.Fail(400, $"源库房 [{req.SourceWarehouseId}] 药品 [{drug.DrugName}] 合格效期库存不足（需 {req.Quantity}{drug.UnitName}，可用 {totalAvailable}{drug.UnitName}）");
		}
		string operatorId = CurrentUserId();
		long nowMs = SystemTime.UtcNowMs;
		string prefix = ((normalizedType == "WAREHOUSE_ISSUE") ? "ISS" : "TRF");
		string outChangeType = ((normalizedType == "WAREHOUSE_ISSUE") ? "OUT_WAREHOUSE_ISSUE" : "OUT_TRANSFER");
		string inChangeType = ((normalizedType == "WAREHOUSE_ISSUE") ? "IN_PHARMACY_RECEIVE" : "IN_TRANSFER");
		List<InvTransferOrder> createdOrders = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			List<InvTransferOrder> orders = new List<InvTransferOrder>();
			int remaining = req.Quantity;
			int seq = 1;
			foreach (InvDrugBatch srcBatch in sourceBatches)
			{
				if (remaining <= 0)
				{
					break;
				}
				int take = Math.Min(srcBatch.Quantity, remaining);
				srcBatch.Quantity -= take;
				srcBatch.UpdatedBy = operatorId;
				await _batchRepo.UpdateAsync(srcBatch, ct);
				string transferNo = $"{prefix}-{DateTime.UtcNow:yyMMddHHmmssfff}-{seq:D2}";
				seq++;
				InvStockLedger outLedger = new InvStockLedger
				{
					WarehouseOrPharmacyId = req.SourceWarehouseId.Trim(),
					DrugId = drug.Id,
					BatchId = srcBatch.Id,
					ChangeType = outChangeType,
					ChangeQuantity = -take,
					BalanceAfter = srcBatch.Quantity,
					ReferenceDocNo = transferNo,
					UnitCostPrice = drug.CostPrice,
					UnitRetailPrice = drug.Price,
					CreatedBy = operatorId,
					UpdatedBy = operatorId
				};
				await _ledgerRepo.InsertAsync(outLedger, ct);
				InvDrugBatch targetBatch = await _batchRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drug_batches\nWHERE warehouse_or_pharmacy_id = @TargetId AND drug_id = @DrugId AND batch_no = @BatchNo", new
				{
					TargetId = req.TargetWarehouseId.Trim(),
					DrugId = drug.Id,
					BatchNo = srcBatch.BatchNo
				}, ct);
				if (targetBatch != null)
				{
					targetBatch.Quantity += take;
					targetBatch.UpdatedBy = operatorId;
					await _batchRepo.UpdateAsync(targetBatch, ct);
				}
				else
				{
					targetBatch = new InvDrugBatch
					{
						WarehouseOrPharmacyId = req.TargetWarehouseId.Trim(),
						DrugId = drug.Id,
						BatchNo = srcBatch.BatchNo,
						ExpiryDateInt = srcBatch.ExpiryDateInt,
						InvoiceNo = srcBatch.InvoiceNo,
						ColdChainTempCelsius = srcBatch.ColdChainTempCelsius,
						Quantity = take,
						IsFrozen = 0,
						CreatedBy = operatorId,
						UpdatedBy = operatorId
					};
					await _batchRepo.InsertAsync(targetBatch, ct);
				}
				InvStockLedger inLedger = new InvStockLedger
				{
					WarehouseOrPharmacyId = req.TargetWarehouseId.Trim(),
					DrugId = drug.Id,
					BatchId = targetBatch.Id,
					ChangeType = inChangeType,
					ChangeQuantity = take,
					BalanceAfter = targetBatch.Quantity,
					ReferenceDocNo = transferNo,
					UnitCostPrice = drug.CostPrice,
					UnitRetailPrice = drug.Price,
					CreatedBy = operatorId,
					UpdatedBy = operatorId
				};
				await _ledgerRepo.InsertAsync(inLedger, ct);
				InvTransferOrder trfOrder = new InvTransferOrder
				{
					TransferNo = transferNo,
					TransferType = normalizedType,
					SourceWarehouseId = req.SourceWarehouseId.Trim(),
					TargetWarehouseId = req.TargetWarehouseId.Trim(),
					DrugId = drug.Id,
					DrugNameSnapshot = drug.DrugName,
					BatchId = srcBatch.Id,
					BatchNo = srcBatch.BatchNo,
					ExpiryDateInt = srcBatch.ExpiryDateInt,
					Quantity = take,
					UnitCostPrice = drug.CostPrice,
					UnitRetailPrice = drug.Price,
					TotalRetailAmount = (decimal)take * drug.Price,
					Status = 2,
					ApplicantId = operatorId,
					DispatchedById = operatorId,
					DispatchedAtMs = nowMs,
					ReceivedById = operatorId,
					ReceivedAtMs = nowMs,
					Remark = (req.Remark ?? ((normalizedType == "WAREHOUSE_ISSUE") ? "药库按FEFO向药房发药出库" : "多库房跨库FEFO调拨")),
					CreatedBy = operatorId,
					UpdatedBy = operatorId
				};
				await _transferRepo.InsertAsync(trfOrder, ct);
				orders.Add(trfOrder);
				remaining -= take;
			}
			return orders;
		});
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			await _idempotency.SaveResponseAsync(requestKey, JsonSerializer.Serialize(createdOrders));
		}
		if (_auditTrail != null && createdOrders.Count > 0)
		{
			string actionTitle = ((normalizedType == "WAREHOUSE_ISSUE") ? "药库发药出库" : "多库房库存调拨");
			await _auditTrail.RecordAuditAsync((normalizedType == "WAREHOUSE_ISSUE") ? "INV_WAREHOUSE_ISSUE" : "INV_MULTI_TRANSFER", "InvTransferOrder", createdOrders[0].Id, $"{actionTitle}: [{req.SourceWarehouseId}] -> [{req.TargetWarehouseId}] 药品 [{drug.DrugName}] 共 {req.Quantity}{drug.UnitName}（单号：{string.Join(",", createdOrders.Select((InvTransferOrder o) => o.TransferNo))}）", operatorId, null, req.SourceWarehouseId);
		}
		string msg = ((normalizedType == "WAREHOUSE_ISSUE") ? $"药库发药出库完成！已从 [{req.SourceWarehouseId}] 按 FEFO 批次向 [{req.TargetWarehouseId}] 调配 [{drug.DrugName}] {req.Quantity}{drug.UnitName}" : $"多库房调拨完成！已从 [{req.SourceWarehouseId}] 向 [{req.TargetWarehouseId}] 调拨 [{drug.DrugName}] {req.Quantity}{drug.UnitName}");
		return ApiResponse<IReadOnlyList<InvTransferOrder>>.Success(createdOrders, msg);
	}

	[HttpGet("ledgers")]
	public async Task<ApiResponse<IReadOnlyList<InvStockLedger>>> GetLedgers([FromQuery] string? warehouseOrPharmacyId = null, [FromQuery] string? drugId = null, [FromQuery] string? referenceDocNo = null, [FromQuery] int limit = 100)
	{
		int safeLimit = Math.Clamp(limit, 1, 500);
		return ApiResponse<IReadOnlyList<InvStockLedger>>.Success(await _ledgerRepo.QueryAsync("SELECT * FROM inv_stock_ledgers\nWHERE (@WarehouseId IS NULL OR warehouse_or_pharmacy_id = @WarehouseId)\n  AND (@DrugId IS NULL OR drug_id = @DrugId)\n  AND (@RefNo IS NULL OR reference_doc_no = @RefNo)\nORDER BY created_at_ms DESC, rowid DESC\nLIMIT @Limit", new
		{
			WarehouseId = warehouseOrPharmacyId,
			DrugId = drugId,
			RefNo = referenceDocNo,
			Limit = safeLimit
		}));
	}

	private string CurrentUserId()
	{
		if (string.IsNullOrWhiteSpace(_userSession?.UserId))
		{
			throw new UnauthorizedAccessException("请先登录后再办理药库业务");
		}
		return _userSession.UserId;
	}

	private bool IsAdmin() =>
		_userSession != null
		&& (RoleCode.IsAdmin(_userSession.RoleCode)
			|| RoleCode.HasRole(_userSession.Roles, RoleCode.Admin));

	private bool IsInventoryOperator() =>
		IsAdmin()
		|| RoleCode.HasAnyRole(_userSession?.Roles, RoleCode.Pharmacist, "DRUG-ADMIN", "WAREHOUSE");

	private bool IsInOrganizationWarehouse(string? warehouseId)
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(warehouseId))
			return false;
		using var conn = _connFactory.CreateConnection();
		return conn.ExecuteScalar<int>(
			"SELECT count(*) FROM sys_departments WHERE id = @Id AND dept_category = 'WAREHOUSE' AND is_active = 1",
			new { Id = warehouseId.Trim() }) == 1;
	}

	private bool IsInOrganizationPharmacy(string? pharmacyId)
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(pharmacyId))
			return false;
		using var conn = _connFactory.CreateConnection();
		return conn.ExecuteScalar<int>(
			"SELECT count(*) FROM sys_departments WHERE id = @Id AND dept_category = 'PHARMACY' AND is_active = 1",
			new { Id = pharmacyId.Trim() }) == 1;
	}

	private async Task<bool> CanStockInAsync(string? warehouseId)
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(warehouseId))
			return false;
		if (IsAdmin())
			return true;
		using var conn = _connFactory.CreateConnection();
		return await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sys_user_dept_posts WHERE user_id = @UserId AND dept_id = @DeptId AND dept_category = 'WAREHOUSE'",
			new { UserId = _userSession.UserId, DeptId = warehouseId.Trim() }) > 0;
	}

	private async Task<bool> CanIssueFromWarehouseAsync(string warehouseId)
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(warehouseId))
			return false;
		if (IsAdmin())
			return true;
		if (!RoleCode.HasAnyRole(_userSession.Roles, "WAREHOUSE", "DRUG-ADMIN"))
			return false;
		using var conn = _connFactory.CreateConnection();
		return await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sys_user_dept_posts WHERE user_id = @UserId AND dept_id = @DeptId AND dept_category = 'WAREHOUSE'",
			new { UserId = _userSession.UserId, DeptId = warehouseId.Trim() }) > 0;
	}
}
