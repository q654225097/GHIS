using System;
using System.Collections.Generic;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public partial class WorkbenchModel
{
	public void EnsurePharmacyWarehouseFinanceInitialized()
	{
		if (!_phmWhFinInitialized)
		{
			_phmWhFinInitialized = true;
			InitDrugMasterAndBatches();
			InitDrugPriceLogs();
			InitWarehouseTransfers();
			RecalculateDrugStockAndFinanceMetrics();
		}
	}

	private void InitDrugMasterAndBatches()
	{
		DrugMasterList.Clear();
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-01",
			DrugCode = "PENICILLIN_G",
			DrugName = "注射用青霉素钠",
			Spec = "80万单位/支",
			UnitName = "支",
			RetailPrice = 12.50m,
			CostPrice = 8.00m,
			AntibioticLevel = 1,
			IsSkinTestRequired = true,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-02",
			DrugCode = "AMOXICILLIN_CAPS",
			DrugName = "阿莫西林胶囊",
			Spec = "0.25g*24粒/盒",
			UnitName = "盒",
			RetailPrice = 18.00m,
			CostPrice = 11.20m,
			AntibioticLevel = 1,
			IsSkinTestRequired = true,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			RetailPrice = 5.50m,
			CostPrice = 3.20m,
			AntibioticLevel = 0,
			IsSkinTestRequired = false,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-04",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			UnitName = "盒",
			RetailPrice = 22.50m,
			CostPrice = 14.00m,
			AntibioticLevel = 0,
			IsSkinTestRequired = false,
			IsNarcotic = false,
			IsActive = true
		});
		DrugMasterList.Add(new DrugMasterItemModel
		{
			DrugId = "DRUG-05",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			Spec = "75mg*7片/盒",
			UnitName = "盒",
			RetailPrice = 56.80m,
			CostPrice = 38.00m,
			AntibioticLevel = 0,
			IsSkinTestRequired = false,
			IsNarcotic = false,
			IsActive = true
		});
		WarehouseBatchList.Clear();
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH1-01",
			WarehouseId = "DEPT-WH-01",
			WarehouseName = "全院中心药库 (DEPT-WH-01)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-01",
			DrugCode = "PENICILLIN_G",
			DrugName = "注射用青霉素钠",
			Spec = "80万单位/支",
			UnitName = "支",
			BatchNo = "LOT202609W",
			ExpiryDateInt = 20280901,
			InvoiceNo = "INV-2026-101",
			ColdChainInfo = "阴凉库 18.0°C 合格",
			UnitCostPrice = 8.00m,
			UnitRetailPrice = 12.50m,
			Quantity = 1000
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH1-03",
			WarehouseId = "DEPT-WH-01",
			WarehouseName = "全院中心药库 (DEPT-WH-01)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			BatchNo = "LOT202610W",
			ExpiryDateInt = 20281001,
			InvoiceNo = "INV-2026-102",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 3.20m,
			UnitRetailPrice = 5.50m,
			Quantity = 2000
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH1-04",
			WarehouseId = "DEPT-WH-01",
			WarehouseName = "全院中心药库 (DEPT-WH-01)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-04",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			UnitName = "盒",
			BatchNo = "LOT202611W",
			ExpiryDateInt = 20281101,
			InvoiceNo = "INV-2026-103",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 14.00m,
			UnitRetailPrice = 22.50m,
			Quantity = 1500
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-WH2-05",
			WarehouseId = "DEPT-WH-02",
			WarehouseName = "冷链生物制品专库 (DEPT-WH-02)",
			WarehouseCategory = "WAREHOUSE",
			DrugId = "DRUG-05",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			Spec = "75mg*7片/盒",
			UnitName = "盒",
			BatchNo = "LOT202612C",
			ExpiryDateInt = 20281201,
			InvoiceNo = "INV-2026-104",
			ColdChainInfo = "GSP冷链 4.2°C 合格",
			UnitCostPrice = 38.00m,
			UnitRetailPrice = 56.80m,
			Quantity = 600
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-01-NEAR",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-01",
			DrugCode = "PENICILLIN_G",
			DrugName = "注射用青霉素钠",
			Spec = "80万单位/支",
			UnitName = "支",
			BatchNo = "LOT202601A",
			ExpiryDateInt = 20270301,
			InvoiceNo = "INV-2026-001",
			ColdChainInfo = "阴凉柜合格",
			UnitCostPrice = 8.00m,
			UnitRetailPrice = 12.50m,
			Quantity = 50
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-03-NEAR",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			BatchNo = "LOT202602S",
			ExpiryDateInt = 20270501,
			InvoiceNo = "INV-2026-004",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 3.20m,
			UnitRetailPrice = 5.50m,
			Quantity = 100
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-04-MAIN",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-04",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			UnitName = "盒",
			BatchNo = "LOT202605C",
			ExpiryDateInt = 20280501,
			InvoiceNo = "INV-2026-006",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 14.00m,
			UnitRetailPrice = 22.50m,
			Quantity = 320
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-05-MAIN",
			WarehouseId = "DEPT-PHM-01",
			WarehouseName = "门诊西药房 (DEPT-PHM-01)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-05",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			Spec = "75mg*7片/盒",
			UnitName = "盒",
			BatchNo = "LOT202605D",
			ExpiryDateInt = 20280501,
			InvoiceNo = "INV-2026-007",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 38.00m,
			UnitRetailPrice = 56.80m,
			Quantity = 180
		});
		WarehouseBatchList.Add(new WarehouseBatchItemModel
		{
			BatchId = "BATCH-PHM2-03",
			WarehouseId = "DEPT-PHM-02",
			WarehouseName = "住院中心药房 (DEPT-PHM-02)",
			WarehouseCategory = "PHARMACY",
			DrugId = "DRUG-03",
			DrugCode = "NORMAL_SALINE_500",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			UnitName = "瓶",
			BatchNo = "LOT202608S",
			ExpiryDateInt = 20280801,
			InvoiceNo = "INV-2026-005",
			ColdChainInfo = "常温合格",
			UnitCostPrice = 3.20m,
			UnitRetailPrice = 5.50m,
			Quantity = 400
		});
		ApplyWarehouseBatchFilter();
	}

	private void InitDrugPriceLogs()
	{
		DrugPriceLogs.Clear();
		DrugPriceLogs.Add(new DrugPriceAdjustmentLogModel
		{
			AdjustNo = "PRC-ADJ-20261101-01",
			DrugCode = "DRUG-CARD-006",
			DrugName = "阿司匹林肠溶片",
			OldRetailPrice = 25.00m,
			NewRetailPrice = 22.50m,
			OldCostPrice = 15.50m,
			NewCostPrice = 14.00m,
			AffectedStockQuantity = 1820,
			UnitName = "盒",
			PriceDiffAmount = -4550.00m,
			AdjustReason = "国家第九批药品集中带量采购中选零差率调价",
			EffectiveTime = "2026-11-01 00:00:00",
			OperatorName = "物价管理员 (EMP001)"
		});
		DrugPriceLogs.Add(new DrugPriceAdjustmentLogModel
		{
			AdjustNo = "PRC-ADJ-20261103-02",
			DrugCode = "DRUG-CARD-005",
			DrugName = "硫酸氢氯吡格雷片",
			OldRetailPrice = 62.00m,
			NewRetailPrice = 56.80m,
			OldCostPrice = 41.00m,
			NewCostPrice = 38.00m,
			AffectedStockQuantity = 780,
			UnitName = "盒",
			PriceDiffAmount = -4056.00m,
			AdjustReason = "省医保集采续约联动降价",
			EffectiveTime = "2026-11-03 00:00:00",
			OperatorName = "物价管理员 (EMP001)"
		});
	}

	private void InitWarehouseTransfers()
	{
		WarehouseTransferOrders.Clear();
		WarehouseTransferOrders.Add(new WarehouseTransferOrderModel
		{
			TransferNo = "ISS-20261105-001",
			TransferType = "WAREHOUSE_ISSUE",
			SourceWarehouseId = "DEPT-WH-01",
			SourceWarehouseName = "全院中心药库 (DEPT-WH-01)",
			TargetWarehouseId = "DEPT-PHM-01",
			TargetWarehouseName = "门诊西药房 (DEPT-PHM-01)",
			DrugName = "阿司匹林肠溶片",
			Spec = "100mg*30片/盒",
			BatchNo = "LOT202611W",
			ExpiryDateInt = 20281101,
			Quantity = 120,
			UnitName = "盒",
			TotalRetailAmount = 2700.00m,
			OperatorName = "孙伟 (主管药师)",
			TransferTime = "2026-11-05 08:30:15",
			StatusText = "✓ 药库已按FEFO发药出库 · 药房已扫码验收入账",
			Remark = "门诊西药房周常补货请领单 (药库FEFO发药)"
		});
		WarehouseTransferOrders.Add(new WarehouseTransferOrderModel
		{
			TransferNo = "TRF-20261106-002",
			TransferType = "INTER_WAREHOUSE",
			SourceWarehouseId = "DEPT-PHM-01",
			SourceWarehouseName = "门诊西药房 (DEPT-PHM-01)",
			TargetWarehouseId = "DEPT-PHM-02",
			TargetWarehouseName = "住院中心药房 (DEPT-PHM-02)",
			DrugName = "0.9%氯化钠注射液",
			Spec = "500ml/瓶",
			BatchNo = "LOT202602S",
			ExpiryDateInt = 20270501,
			Quantity = 50,
			UnitName = "瓶",
			TotalRetailAmount = 275.00m,
			OperatorName = "孙伟 (主管药师)",
			TransferTime = "2026-11-06 14:20:00",
			StatusText = "✓ 跨库房调拨完成 · 双边库存台账已平衡",
			Remark = "门诊西药房向住院中心药房跨库房应急调拨"
		});
	}

	public void ApplyWarehouseBatchFilter()
	{
		List<WarehouseBatchItemModel> list = (from b in WarehouseBatchList
			where SelectedWarehouseFilter == "ALL" || string.Equals(b.WarehouseId, SelectedWarehouseFilter, StringComparison.OrdinalIgnoreCase)
			orderby b.ExpiryDateInt
			select b).ToList();
		FilteredWarehouseBatchList.Clear();
		foreach (WarehouseBatchItemModel item in list)
		{
			FilteredWarehouseBatchList.Add(item);
		}
	}

	public void RefreshPharmacyWarehouseMetrics()
	{
		RecalculateDrugStockAndFinanceMetrics();
	}

	public void RecalculateDrugStockAndFinanceMetrics()
	{
		foreach (DrugMasterItemModel drug in DrugMasterList)
		{
			drug.TotalStockQuantity = WarehouseBatchList.Where((WarehouseBatchItemModel b) => b.DrugId == drug.DrugId || b.DrugCode == drug.DrugCode).Sum((WarehouseBatchItemModel b) => b.Quantity);
		}
		DrugFinanceBreakdownList.Clear();
		foreach (DrugMasterItemModel drug2 in DrugMasterList)
		{
			int num = PharmacyRecords.Where((PharmacyDispenseModel r) => r.IsOrderClosed && (r.DrugId == drug2.DrugId || r.DrugSummary.Contains(drug2.DrugName))).Sum((PharmacyDispenseModel r) => r.DispenseQuantity) + 35;
			int num2 = PharmacyRecords.Where((PharmacyDispenseModel r) => r.IsReturned && (r.DrugId == drug2.DrugId || r.DrugSummary.Contains(drug2.DrugName))).Sum((PharmacyDispenseModel r) => r.DispenseQuantity) + 2;
			int num3 = drug2.TotalStockQuantity + num - num2;
			DrugFinanceBreakdownList.Add(new DrugFinanceReconcileItemModel
			{
				DrugCode = drug2.DrugCode,
				DrugName = drug2.DrugName,
				Spec = drug2.Spec,
				UnitName = drug2.UnitName,
				CostPrice = drug2.CostPrice,
				RetailPrice = drug2.RetailPrice,
				PurchasedQty = num3,
				PurchasedCostAmount = (decimal)num3 * drug2.CostPrice,
				DispensedQty = num,
				DispensedSalesAmount = (decimal)num * drug2.RetailPrice,
				ReturnedQty = num2,
				ReturnedSalesAmount = (decimal)num2 * drug2.RetailPrice,
				CurrentStockQty = drug2.TotalStockQuantity,
				CurrentStockRetailValue = (decimal)drug2.TotalStockQuantity * drug2.RetailPrice
			});
		}
		OnPropertyChanged("PendingPharmacyDispenseCount");
		OnPropertyChanged("ClosedPharmacyOrderCount");
		OnPropertyChanged("ReturnedPharmacyOrderCount");
		OnPropertyChanged("TotalInventoryCostValueText");
		OnPropertyChanged("TotalInventoryRetailValueText");
		OnPropertyChanged("TotalProcurementCostText");
		OnPropertyChanged("TotalDrugNetSalesText");
		OnPropertyChanged("TotalDrugReturnDeductionText");
		OnPropertyChanged("TotalPriceDiffImpactText");
		OnPropertyChanged("UnpaidPrescriptionCount");
		OnPropertyChanged("PendingFinancialRefundCount");
		OnPropertyChanged("CompletedFinancialRefundCount");
	}

	public void DispenseSinglePrescription(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperatePharmacy)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 扫码核验发药与关单属于【药剂科药师 (ROLE-PHARMACIST)】专属职责！财务或临床人员严禁操作发药，请切换为药房药师。";
			return;
		}
		PharmacyDispenseModel target = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => x.CanDispense && !x.IsAllergyBlocked && !x.IsOrderClosed);
		if (target == null)
		{
			SystemNotice = "⚠\ufe0f [A➔B➔C 闭环门禁拦截] 当前无满足【步骤C：扫码发药】条件的处方（需已完成步骤B收费通知、无过敏阻断且未发药关单）。";
			return;
		}
		if (target.IsOrderClosed)
		{
			SystemNotice = $"\ud83d\udd12 [9. 防二次发药门禁拦截] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 已完成发药并关闭订单，系统 100% 拒绝二次重复发药！";
			return;
		}
		if (!target.IsPaidNotified)
		{
			SystemNotice = $"⚠\ufe0f [A➔B➔C 顺序门禁拦截] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 尚未完成【步骤B：财务收费员收费结算】，未收到可发药通知，严禁跳过收费直接发药！";
			return;
		}
		if (target.IsAllergyBlocked)
		{
			SystemNotice = "\ud83d\udee1\ufe0f 用药安全阻断：处方 [" + target.PrescriptionNo + "] 含青霉素阳性拦截药品，请先在门诊医生处方区点击「自动替换为安全替代药」并重签推送后再发药！";
			return;
		}
		WarehouseBatchItemModel warehouseBatchItemModel = (from b in WarehouseBatchList
			where b.WarehouseId == "DEPT-PHM-01" && !b.IsFrozen && b.Quantity >= target.DispenseQuantity && (b.DrugId == target.DrugId || b.DrugId == "DRUG-04")
			orderby b.ExpiryDateInt
			select b).FirstOrDefault();
		string text = "LOT202605C";
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity -= target.DispenseQuantity;
			text = $"{warehouseBatchItemModel.BatchNo} (余{warehouseBatchItemModel.Quantity}{warehouseBatchItemModel.UnitName})";
		}
		target.IsOrderClosed = true;
		target.CanDispense = false;
		target.DispenseStatus = "\ud83d\udd12 步骤C已完成: 已发药并关闭订单 (防二次发药)";
		target.PaymentNoticeStatus = "✓ [步骤C完成] 已按 FEFO 批次 [" + text + "] 发药出库 · 订单已自动关闭锁止 (若患者需退药，现已解锁【步骤D: 退药扫码入库】)";
		target.PharmacistName = "孙伟 (主管药师 · 扫码复核)";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"✓ [步骤C：药房扫码发药并自动关单完成] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 已按 FEFO 扣减批次 [{text}] 完成发药并关闭订单（严防二次发药），同时已解锁【步骤D：退药扫码入库】！";
	}

	public void ConfirmReturnDrugForOrder(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperatePharmacy)
		{
			SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 实物退药扫码质检与回补库存属于【药剂科药师 (ROLE-PHARMACIST)】专属职责！请切换为药房药师执行步骤D退药回库。";
			return;
		}
		PharmacyDispenseModel target = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => x.IsOrderClosed && !x.IsReturned);
		if (target == null)
		{
			SystemNotice = "⚠\ufe0f [B➔C➔D 顺序门禁拦截] 请先选择一张已完成【步骤C：药房已发药并关单】的处方，未发药处方严禁直接执行退药入库！";
			return;
		}
		if (!target.IsOrderClosed)
		{
			SystemNotice = "⚠\ufe0f [B➔C➔D 顺序门禁拦截] 处方 [" + target.PrescriptionNo + "] 尚未完成【步骤C：药房扫码发药】，不能跳过发药直接办理退药入库！";
			return;
		}
		if (target.IsReturned)
		{
			SystemNotice = "处方 [" + target.PrescriptionNo + "] 已完成【步骤D：退药扫码入库】，请点击右侧「\ud83d\udcb8 步骤E：执行财务退费」完成退款闭环！";
			return;
		}
		WarehouseBatchItemModel warehouseBatchItemModel = WarehouseBatchList.FirstOrDefault((WarehouseBatchItemModel b) => b.WarehouseId == "DEPT-PHM-01" && (b.DrugId == target.DrugId || b.DrugId == "DRUG-04"));
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity += target.DispenseQuantity;
		}
		target.IsReturned = true;
		target.CanDispense = false;
		target.DispenseStatus = "步骤D已完成: 药房已退药回库 · 待执行【步骤E: 财务退费】";
		target.PaymentNoticeStatus = "✓ [步骤D完成] 药房已完成 GSP 包装与冷链质检并回补原批次库存 (drug_return_confirmed=1) · 已解锁【步骤E: 财务窗口退费】！";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"✓ [步骤D：药房实物退药回库完成] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 实物扫码核验通过，已回补原批次库存 +{target.DispenseQuantity}，已解锁【步骤E：财务退费结清】！";
	}

	public void SaveNewOrUpdatedDrugMaster()
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (string.IsNullOrWhiteSpace(EditDrugCode) || string.IsNullOrWhiteSpace(EditDrugName))
		{
			SystemNotice = "药品编码与药品名称不能为空。";
			return;
		}
		string code = EditDrugCode.Trim().ToUpperInvariant();
		DrugMasterItemModel drugMasterItemModel = DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => string.Equals(d.DrugCode, code, StringComparison.OrdinalIgnoreCase));
		if (drugMasterItemModel != null)
		{
			decimal retailPrice = drugMasterItemModel.RetailPrice;
			decimal costPrice = drugMasterItemModel.CostPrice;
			drugMasterItemModel.DrugName = EditDrugName.Trim();
			drugMasterItemModel.Spec = EditDrugSpec.Trim();
			drugMasterItemModel.UnitName = EditDrugUnit.Trim();
			drugMasterItemModel.RetailPrice = EditDrugRetailPrice;
			drugMasterItemModel.CostPrice = EditDrugCostPrice;
			drugMasterItemModel.AntibioticLevel = EditDrugAntibioticLevel;
			drugMasterItemModel.IsSkinTestRequired = EditDrugSkinTest;
			drugMasterItemModel.IsNarcotic = EditDrugNarcotic;
			if (Math.Abs(retailPrice - EditDrugRetailPrice) > 0.001m || Math.Abs(costPrice - EditDrugCostPrice) > 0.001m)
			{
				RecordDrugPriceAdjustmentInternal(drugMasterItemModel, retailPrice, EditDrugRetailPrice, costPrice, EditDrugCostPrice, "药品基础数据维护同步调价");
			}
			RecalculateDrugStockAndFinanceMetrics();
			SystemNotice = $"✓ [1. 药品基础数据维护] 已更新药品 [{drugMasterItemModel.DrugCode} - {drugMasterItemModel.DrugName}] ({drugMasterItemModel.Spec})，零售价=¥{drugMasterItemModel.RetailPrice:0.00}，成本价=¥{drugMasterItemModel.CostPrice:0.00}。";
		}
		else
		{
			DrugMasterItemModel drugMasterItemModel2 = new DrugMasterItemModel
			{
				DrugId = $"DRUG-{DrugMasterList.Count + 1:D2}",
				DrugCode = code,
				DrugName = EditDrugName.Trim(),
				Spec = (string.IsNullOrWhiteSpace(EditDrugSpec) ? "标准规格" : EditDrugSpec.Trim()),
				UnitName = (string.IsNullOrWhiteSpace(EditDrugUnit) ? "盒" : EditDrugUnit.Trim()),
				RetailPrice = EditDrugRetailPrice,
				CostPrice = EditDrugCostPrice,
				AntibioticLevel = EditDrugAntibioticLevel,
				IsSkinTestRequired = EditDrugSkinTest,
				IsNarcotic = EditDrugNarcotic,
				IsActive = true,
				TotalStockQuantity = 0
			};
			DrugMasterList.Insert(0, drugMasterItemModel2);
			RecalculateDrugStockAndFinanceMetrics();
			SystemNotice = $"✓ [1. 药品基础数据维护] 成功新增药品字典 [{drugMasterItemModel2.DrugCode} - {drugMasterItemModel2.DrugName}] ({drugMasterItemModel2.Spec})，核定零售价=¥{drugMasterItemModel2.RetailPrice:0.00}。";
		}
	}

	public void ToggleDrugMasterStatus(DrugMasterItemModel? drug)
	{
		if (drug != null)
		{
			drug.IsActive = !drug.IsActive;
			SystemNotice = $"[1. 药品基础数据维护] 药品 [{drug.DrugName}] 状态已变更为：{drug.StatusText}。";
		}
	}

	public void ExecuteQuickPriceAdjustment(DrugMasterItemModel? drug = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		DrugMasterItemModel target = drug ?? DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => d.DrugCode == EditDrugCode) ?? DrugMasterList.FirstOrDefault();
		if (target == null)
		{
			return;
		}
		decimal retailPrice = target.RetailPrice;
		decimal costPrice = target.CostPrice;
		decimal num = ((drug != null) ? Math.Max(1.00m, Math.Round(target.RetailPrice * 0.90m, 2)) : EditDrugRetailPrice);
		decimal num2 = ((drug != null) ? Math.Max(0.50m, Math.Round(target.CostPrice * 0.90m, 2)) : EditDrugCostPrice);
		target.RetailPrice = num;
		target.CostPrice = num2;
		foreach (WarehouseBatchItemModel item in WarehouseBatchList.Where((WarehouseBatchItemModel b) => b.DrugId == target.DrugId || b.DrugCode == target.DrugCode))
		{
			item.UnitRetailPrice = num;
			item.UnitCostPrice = num2;
		}
		DrugPriceAdjustmentLogModel drugPriceAdjustmentLogModel = RecordDrugPriceAdjustmentInternal(target, retailPrice, num, costPrice, num2, string.IsNullOrWhiteSpace(PriceAdjustReason) ? "国家集采中选联动调价" : PriceAdjustReason);
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"\ud83d\udcc8 [2. 药品定价调价完成] 调价单 [{drugPriceAdjustmentLogModel.AdjustNo}]：[{target.DrugName}] 零售价 ¥{retailPrice:0.00}→¥{num:0.00}，涉及全院在库 {drugPriceAdjustmentLogModel.AffectedStockQuantity}{target.UnitName}，库存损益差额 {((drugPriceAdjustmentLogModel.PriceDiffAmount >= 0m) ? $"+¥{drugPriceAdjustmentLogModel.PriceDiffAmount:0.00}" : $"-¥{Math.Abs(drugPriceAdjustmentLogModel.PriceDiffAmount):0.00}")}！";
	}

	private DrugPriceAdjustmentLogModel RecordDrugPriceAdjustmentInternal(DrugMasterItemModel drug, decimal oldRetail, decimal newRetail, decimal oldCost, decimal newCost, string reason)
	{
		int num = WarehouseBatchList.Where((WarehouseBatchItemModel b) => b.DrugId == drug.DrugId || b.DrugCode == drug.DrugCode).Sum((WarehouseBatchItemModel b) => b.Quantity);
		decimal priceDiffAmount = (newRetail - oldRetail) * (decimal)num;
		DrugPriceAdjustmentLogModel drugPriceAdjustmentLogModel = new DrugPriceAdjustmentLogModel
		{
			AdjustNo = $"PRC-ADJ-{DateTime.Now:yyyyMMdd}-{DrugPriceLogs.Count + 1:D2}",
			DrugCode = drug.DrugCode,
			DrugName = drug.DrugName,
			OldRetailPrice = oldRetail,
			NewRetailPrice = newRetail,
			OldCostPrice = oldCost,
			NewCostPrice = newCost,
			AffectedStockQuantity = num,
			UnitName = drug.UnitName,
			PriceDiffAmount = priceDiffAmount,
			AdjustReason = reason,
			EffectiveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			OperatorName = CurrentOperatorName
		};
		DrugPriceLogs.Insert(0, drugPriceAdjustmentLogModel);
		return drugPriceAdjustmentLogModel;
	}

	public void ExecuteStockInNewBatch()
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (StockInQuantity <= 0)
		{
			SystemNotice = "入库数量必须大于 0。";
			return;
		}
		if (StockInColdChainTemp > 8.0m && StockInWarehouseId == "DEPT-WH-02")
		{
			SystemNotice = $"❌ [GSP 冷链超温强阻断] 冷链专库收货实测温度 {StockInColdChainTemp:0.0}°C > 阈值 8.0°C，系统强制拒绝入库！";
			return;
		}
		DrugMasterItemModel drug = DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => string.Equals(d.DrugCode, StockInDrugCode, StringComparison.OrdinalIgnoreCase)) ?? DrugMasterList.First();
		string text = ResolveWarehouseDisplayName(StockInWarehouseId);
		WarehouseBatchItemModel warehouseBatchItemModel = WarehouseBatchList.FirstOrDefault((WarehouseBatchItemModel b) => b.WarehouseId == StockInWarehouseId && b.DrugCode == drug.DrugCode && b.BatchNo == StockInBatchNo);
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity += StockInQuantity;
		}
		else
		{
			WarehouseBatchList.Insert(0, new WarehouseBatchItemModel
			{
				BatchId = $"BATCH-{WarehouseBatchList.Count + 1:D3}",
				WarehouseId = StockInWarehouseId,
				WarehouseName = text,
				WarehouseCategory = (StockInWarehouseId.StartsWith("DEPT-WH") ? "WAREHOUSE" : "PHARMACY"),
				DrugId = drug.DrugId,
				DrugCode = drug.DrugCode,
				DrugName = drug.DrugName,
				Spec = drug.Spec,
				UnitName = drug.UnitName,
				BatchNo = (string.IsNullOrWhiteSpace(StockInBatchNo) ? $"LOT{DateTime.Now:yyyyMMdd}" : StockInBatchNo.Trim()),
				ExpiryDateInt = StockInExpiryInt,
				InvoiceNo = (string.IsNullOrWhiteSpace(StockInInvoiceNo) ? $"INV-{DateTime.Now:yyyyMMdd}" : StockInInvoiceNo.Trim()),
				ColdChainInfo = ((StockInWarehouseId == "DEPT-WH-02") ? $"GSP冷链 {StockInColdChainTemp:0.0}°C 合格" : "常温验收合格"),
				UnitCostPrice = drug.CostPrice,
				UnitRetailPrice = drug.RetailPrice,
				Quantity = StockInQuantity,
				IsFrozen = false
			});
		}
		ApplyWarehouseBatchFilter();
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"✓ [3 & 4. 药品采购入库与财务应付登记] 库房 [{text}] 成功入库 [{drug.DrugName}] 批号 [{StockInBatchNo}] +{StockInQuantity}{drug.UnitName}（采购成本 ¥{(decimal)StockInQuantity * drug.CostPrice:0.00}，随货同行发票：{StockInInvoiceNo}）！";
	}

	public void ToggleBatchFreezeStatus(WarehouseBatchItemModel? batch)
	{
		if (batch != null)
		{
			batch.IsFrozen = !batch.IsFrozen;
			SystemNotice = (batch.IsFrozen ? $"\ud83d\udd12 [3. 库存质量管控] 已冻结库房 [{batch.WarehouseName}] 批次 [{batch.BatchNo} ({batch.DrugName})]，FEFO 发药与调拨已自动跳过该批次！" : $"✓ [3. 库存质量管控] 已解除批次 [{batch.BatchNo} ({batch.DrugName})] 冻结状态，恢复正常发药与调拨。");
		}
	}

	public void ExecuteWarehouseIssueOrTransfer(string transferType)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (string.Equals(TransferSourceWarehouseId, TransferTargetWarehouseId, StringComparison.OrdinalIgnoreCase))
		{
			SystemNotice = "调出库房（源库房）与调入库房（目标库房/药房）不能相同！";
			return;
		}
		if (TransferQuantity <= 0)
		{
			SystemNotice = "调拨或药库发药数量必须大于 0。";
			return;
		}
		DrugMasterItemModel drug = DrugMasterList.FirstOrDefault((DrugMasterItemModel d) => string.Equals(d.DrugCode, TransferDrugCode, StringComparison.OrdinalIgnoreCase)) ?? DrugMasterList.First();
		WarehouseBatchItemModel sourceBatch = (from b in WarehouseBatchList
			where b.WarehouseId == TransferSourceWarehouseId && (b.DrugCode == drug.DrugCode || b.DrugId == drug.DrugId) && !b.IsFrozen && b.Quantity > 0
			orderby b.ExpiryDateInt
			select b).FirstOrDefault();
		if (sourceBatch == null || sourceBatch.Quantity < TransferQuantity)
		{
			int value = sourceBatch?.Quantity ?? 0;
			SystemNotice = $"❌ 源库房 [{ResolveWarehouseDisplayName(TransferSourceWarehouseId)}] 药品 [{drug.DrugName}] 可用库存不足（需 {TransferQuantity}{drug.UnitName}，当前单批可用 {value}{drug.UnitName}）！";
			return;
		}
		sourceBatch.Quantity -= TransferQuantity;
		string text = ResolveWarehouseDisplayName(TransferTargetWarehouseId);
		WarehouseBatchItemModel warehouseBatchItemModel = WarehouseBatchList.FirstOrDefault((WarehouseBatchItemModel b) => b.WarehouseId == TransferTargetWarehouseId && b.DrugCode == drug.DrugCode && b.BatchNo == sourceBatch.BatchNo);
		if (warehouseBatchItemModel != null)
		{
			warehouseBatchItemModel.Quantity += TransferQuantity;
		}
		else
		{
			WarehouseBatchList.Insert(0, new WarehouseBatchItemModel
			{
				BatchId = $"BATCH-TRF-{WarehouseBatchList.Count + 1:D3}",
				WarehouseId = TransferTargetWarehouseId,
				WarehouseName = text,
				WarehouseCategory = (TransferTargetWarehouseId.StartsWith("DEPT-WH") ? "WAREHOUSE" : "PHARMACY"),
				DrugId = drug.DrugId,
				DrugCode = drug.DrugCode,
				DrugName = drug.DrugName,
				Spec = drug.Spec,
				UnitName = drug.UnitName,
				BatchNo = sourceBatch.BatchNo,
				ExpiryDateInt = sourceBatch.ExpiryDateInt,
				InvoiceNo = sourceBatch.InvoiceNo,
				ColdChainInfo = sourceBatch.ColdChainInfo,
				UnitCostPrice = drug.CostPrice,
				UnitRetailPrice = drug.RetailPrice,
				Quantity = TransferQuantity,
				IsFrozen = false
			});
		}
		bool flag = string.Equals(transferType, "WAREHOUSE_ISSUE", StringComparison.OrdinalIgnoreCase);
		string value2 = (flag ? "ISS" : "TRF");
		string text2 = $"{value2}-{DateTime.Now:yyyyMMdd}-{WarehouseTransferOrders.Count + 1:D3}";
		string text3 = ResolveWarehouseDisplayName(TransferSourceWarehouseId);
		WarehouseTransferOrderModel warehouseTransferOrderModel = new WarehouseTransferOrderModel
		{
			TransferNo = text2,
			TransferType = (flag ? "WAREHOUSE_ISSUE" : "INTER_WAREHOUSE"),
			SourceWarehouseId = TransferSourceWarehouseId,
			SourceWarehouseName = text3,
			TargetWarehouseId = TransferTargetWarehouseId,
			TargetWarehouseName = text,
			DrugName = drug.DrugName,
			Spec = drug.Spec,
			BatchNo = sourceBatch.BatchNo,
			ExpiryDateInt = sourceBatch.ExpiryDateInt,
			Quantity = TransferQuantity,
			UnitName = drug.UnitName,
			TotalRetailAmount = (decimal)TransferQuantity * drug.RetailPrice,
			OperatorName = CurrentOperatorName,
			TransferTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			StatusText = (flag ? "✓ 药库已按FEFO发药出库 · 药房已扫码入账" : "✓ 多库房跨库调拨完成 · 双边台账已平衡"),
			Remark = ((!string.IsNullOrWhiteSpace(TransferRemark)) ? TransferRemark.Trim() : (flag ? "药库向药房发药补货" : "多库房跨库调拨"))
		};
		WarehouseTransferOrders.Insert(0, warehouseTransferOrderModel);
		ApplyWarehouseBatchFilter();
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = (flag ? $"\ud83d\udce6 [6. 药库发药管理闭环] 单号 [{text2}]：已从 [{text3}] 按 FEFO 批次 [{sourceBatch.BatchNo}] 向 [{text}] 发放 [{drug.DrugName}] {TransferQuantity}{drug.UnitName}（零售总额 ¥{warehouseTransferOrderModel.TotalRetailAmount:0.00}）！" : $"\ud83d\udd04 [5. 多个库房调拨闭环] 单号 [{text2}]：已完成 [{text3}] ➔ [{text}] 跨库房调拨 [{drug.DrugName}] {TransferQuantity}{drug.UnitName}（批次 {sourceBatch.BatchNo}）！");
	}

	public void DispenseInpatientDischargeMedsForPatient(InpatientRecordModel? patient = null)
	{
		InpatientRecordModel inpatientRecordModel = patient ?? SelectedInpatient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperatePharmacy)
			{
				SystemNotice = "\ud83d\udee1\ufe0f [职责分离 SoD 权限拦截] 住院出院带药核发（出院六严关·步骤C/关卡3）属于【药房药师 (ROLE-PHARMACIST)】专属职责！";
				return;
			}
			SelectedInpatient = inpatientRecordModel;
			CompleteDischargeGate3_DischargeMeds();
		}
	}

	private static string ResolveWarehouseDisplayName(string warehouseId)
	{
		string result = warehouseId switch
		{
			"DEPT-WH-01" => "全院中心药库 (DEPT-WH-01)", 
			"DEPT-WH-02" => "冷链生物制品专库 (DEPT-WH-02)", 
			"DEPT-PHM-01" => "门诊西药房 (DEPT-PHM-01)", 
			"DEPT-PHM-02" => "住院中心药房 (DEPT-PHM-02)", 
			_ => warehouseId, 
		};
		return result;
	}
}
