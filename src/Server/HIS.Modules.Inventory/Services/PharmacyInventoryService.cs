using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Inventory.Models;

namespace HIS.Modules.Inventory.Services;

public class PharmacyInventoryService : IPharmacyInventoryContract, IPriceTableContract
{
	private readonly IRepository<InvDrug> _drugRepo;

	private readonly IRepository<InvDrugBatch> _batchRepo;

	private readonly IRepository<InvStockLedger> _ledgerRepo;

	private readonly IDbConnectionFactory _connFactory;

	public PharmacyInventoryService(IRepository<InvDrug> drugRepo, IRepository<InvDrugBatch> batchRepo, IRepository<InvStockLedger> ledgerRepo, IDbConnectionFactory connFactory)
	{
		_drugRepo = drugRepo;
		_batchRepo = batchRepo;
		_ledgerRepo = ledgerRepo;
		_connFactory = connFactory;
	}

	public async Task<DrugCatalogSnapshotDto?> GetDrugSnapshotAsync(string drugIdOrCode, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(drugIdOrCode))
		{
			return null;
		}
		InvDrug drug = await FindDrugAsync(drugIdOrCode.Trim(), ct);
		if (drug == null)
		{
			return null;
		}
		return new DrugCatalogSnapshotDto(drug.Id, drug.DrugCode, drug.DrugName, drug.Spec, drug.UnitName, drug.Price, drug.AntibioticLevel, drug.IsNarcotic == 1, drug.IsSkinTestRequired == 1, drug.IsActive == 1);
	}

	public async Task<PriceItemSnapshotDto?> GetPriceSnapshotAsync(string itemCode, long atTimeMs, CancellationToken ct = default(CancellationToken))
	{
		InvDrug drug = await FindDrugAsync(itemCode, ct);
		if (drug == null)
		{
			return null;
		}
		return new PriceItemSnapshotDto(drug.DrugCode, drug.DrugName, drug.Price, drug.UnitName, (drug.IsNarcotic == 1) ? "NARCOTIC_DRUG" : "WESTERN_DRUG");
	}

	public async Task<bool> CheckStockAvailabilityAsync(string pharmacyId, string drugId, int requestedQuantity, CancellationToken ct = default(CancellationToken))
	{
		if (requestedQuantity <= 0)
		{
			return true;
		}
		string targetDrugId = (await FindDrugAsync(drugId, ct))?.Id ?? drugId;
		int today = SystemTime.TodayInt;
		return (await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches\nWHERE warehouse_or_pharmacy_id = @PharmacyId\n  AND drug_id = @DrugId\n  AND is_frozen = 0\n  AND expiry_date_int >= @Today\n  AND quantity > 0", new
		{
			PharmacyId = pharmacyId,
			DrugId = targetDrugId,
			Today = today
		}, ct)).Sum((InvDrugBatch x) => x.Quantity) >= requestedQuantity;
	}

	public async Task<StockMutationResultDto> DeductFefoStockAsync(string pharmacyId, string drugIdOrCode, int requestedQuantity, string referenceDocNo, string? operatorId = null, CancellationToken ct = default(CancellationToken), string? paymentOrderId = null)
	{
		if (requestedQuantity <= 0)
		{
			return new StockMutationResultDto(IsSuccess: true, "无需扣减库存", 0, Array.Empty<BatchDeductionItemDto>());
		}
		InvDrug drug = await FindDrugAsync(drugIdOrCode, ct);
		string targetDrugId = drug?.Id ?? drugIdOrCode;
		return await DeductFefoStockCoreAsync(pharmacyId, targetDrugId, drug, requestedQuantity,
			referenceDocNo, operatorId, ct, paymentOrderId);
	}

	private async Task<StockMutationResultDto> DeductFefoStockCoreAsync(
		string pharmacyId, string targetDrugId, InvDrug? drug, int requestedQuantity,
		string referenceDocNo, string? operatorId, CancellationToken ct,
		string? paymentOrderId)
	{
		if (requestedQuantity <= 0)
		{
			return new StockMutationResultDto(IsSuccess: true, "无需扣减库存", 0, Array.Empty<BatchDeductionItemDto>());
		}
		int today = SystemTime.TodayInt;
		return await _connFactory.ExecuteInTransactionAsync(async (CancellationToken token) =>
		{
			IReadOnlyList<InvDrugBatch> allBatches = await _batchRepo.QueryAsync("SELECT * FROM inv_drug_batches\nWHERE warehouse_or_pharmacy_id = @PharmacyId\n  AND drug_id = @DrugId\nORDER BY expiry_date_int ASC, created_at_ms ASC", new
			{
				PharmacyId = pharmacyId,
				DrugId = targetDrugId
			}, token);
			if (allBatches.Count == 0)
			{
				return new StockMutationResultDto(IsSuccess: false, $"药房 [{pharmacyId}] 药品 [{drug?.DrugName ?? targetDrugId}] 没有任何库存批次，禁止发药", 0, Array.Empty<BatchDeductionItemDto>());
			}
			List<InvDrugBatch> validBatches = (from b in allBatches
				where b.IsFrozen == 0 && b.ExpiryDateInt >= today && b.Quantity > 0
				orderby b.ExpiryDateInt, b.CreatedAtMs
				select b).ToList();
			int availableQty = validBatches.Sum((InvDrugBatch x) => x.Quantity);
			if (availableQty < requestedQuantity)
			{
				string drugLabel = drug?.DrugName ?? targetDrugId;
				return new StockMutationResultDto(IsSuccess: false, $"药房 [{pharmacyId}] 药品 [{drugLabel}] 可用合格效期库存不足（需 {requestedQuantity}，可用 {availableQty}）", 0, Array.Empty<BatchDeductionItemDto>());
			}
			int remaining = requestedQuantity;
			List<BatchDeductionItemDto> deductedItems = new List<BatchDeductionItemDto>();
			foreach (InvDrugBatch batch in validBatches)
			{
				if (remaining <= 0)
				{
					break;
				}
				int take = Math.Min(batch.Quantity, remaining);
				batch.Quantity -= take;
				batch.UpdatedBy = operatorId;
				await _batchRepo.UpdateAsync(batch, token);
				InvStockLedger ledger = new InvStockLedger
				{
					WarehouseOrPharmacyId = pharmacyId,
					DrugId = targetDrugId,
					BatchId = batch.Id,
					ChangeType = "OUT_DISPENSE",
					ChangeQuantity = -take,
					BalanceAfter = batch.Quantity,
					ReferenceDocNo = referenceDocNo,
					PaymentOrderId = paymentOrderId,
					UnitCostPrice = drug?.CostPrice,
					UnitRetailPrice = drug?.Price,
					CreatedBy = operatorId,
					UpdatedBy = operatorId
				};
				await _ledgerRepo.InsertAsync(ledger, token);
				deductedItems.Add(new BatchDeductionItemDto(batch.Id, batch.BatchNo, batch.ExpiryDateInt, take, batch.Quantity));
				remaining -= take;
			}
			return new StockMutationResultDto(IsSuccess: true, $"已按 FEFO 近效期先出规则扣减 {deductedItems.Count} 个批次共 {requestedQuantity} 单位", requestedQuantity, deductedItems);
		}, ct);
	}

	public async Task<StockMutationResultDto> RestoreStockFromReturnAsync(string pharmacyId, string referenceDocNo, string? operatorId = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(referenceDocNo))
		{
			return new StockMutationResultDto(IsSuccess: false, "缺少关联发药单号", 0, Array.Empty<BatchDeductionItemDto>());
		}
		return await _connFactory.ExecuteInTransactionAsync(async (CancellationToken token) =>
		{
			IReadOnlyList<InvStockLedger> dispenseLedgers = await _ledgerRepo.QueryAsync("SELECT * FROM inv_stock_ledgers\nWHERE reference_doc_no = @RefNo AND change_type = 'OUT_DISPENSE'\nORDER BY created_at_ms ASC", new
			{
				RefNo = referenceDocNo
			}, token);
			if (dispenseLedgers.Count == 0)
			{
				return new StockMutationResultDto(IsSuccess: false, "未检索到该发药单的批次扣减流水，不能确认退药入库", 0, Array.Empty<BatchDeductionItemDto>());
			}
			if ((await _ledgerRepo.QueryAsync("SELECT * FROM inv_stock_ledgers WHERE reference_doc_no = @RefNo AND change_type = 'IN_RETURN'", new
			{
				RefNo = referenceDocNo
			}, token)).Count > 0)
			{
				return new StockMutationResultDto(IsSuccess: true, "该发药单库存已回补，幂等跳过", 0, Array.Empty<BatchDeductionItemDto>());
			}
			List<BatchDeductionItemDto> restoredItems = new List<BatchDeductionItemDto>();
			int totalRestored = 0;
			foreach (InvStockLedger outLedger in dispenseLedgers)
			{
				int returnQty = Math.Abs(outLedger.ChangeQuantity);
				if (returnQty > 0)
				{
					InvDrugBatch batch = await _batchRepo.GetByIdAsync(outLedger.BatchId, token);
					if (batch != null)
					{
						batch.Quantity += returnQty;
						batch.UpdatedBy = operatorId;
						await _batchRepo.UpdateAsync(batch, token);
						InvStockLedger inLedger = new InvStockLedger
						{
							WarehouseOrPharmacyId = batch.WarehouseOrPharmacyId,
							DrugId = batch.DrugId,
							BatchId = batch.Id,
							ChangeType = "IN_RETURN",
							ChangeQuantity = returnQty,
							BalanceAfter = batch.Quantity,
							ReferenceDocNo = referenceDocNo,
							UnitCostPrice = outLedger.UnitCostPrice,
							UnitRetailPrice = outLedger.UnitRetailPrice,
							CreatedBy = operatorId,
							UpdatedBy = operatorId
						};
						await _ledgerRepo.InsertAsync(inLedger, token);
						restoredItems.Add(new BatchDeductionItemDto(batch.Id, batch.BatchNo, batch.ExpiryDateInt, returnQty, batch.Quantity));
						totalRestored += returnQty;
					}
				}
			}
			return new StockMutationResultDto(IsSuccess: true, $"已完成原批次退药入库回补，共恢复 {totalRestored} 单位库存", totalRestored, restoredItems);
		}, ct);
	}

	private async Task<InvDrug?> FindDrugAsync(string drugIdOrCode, CancellationToken ct)
	{
		InvDrug byId = await _drugRepo.GetByIdAsync(drugIdOrCode, ct);
		if (byId != null)
		{
			return byId;
		}
		return await _drugRepo.QuerySingleOrDefaultAsync("SELECT * FROM inv_drugs WHERE drug_code = @Code", new
		{
			Code = drugIdOrCode
		}, ct);
	}
}
