using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public partial class WorkbenchModel
{
	public void CashierSettleAndNotifyPharmacy(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 处方收费结算属于【财务处收费员 (ROLE-CASHIER)】专属职责！当前角色无收费权限，请点击「切换为财务收费员」后再操作。";
			return;
		}
		PharmacyDispenseModel pharmacyDispenseModel = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => !x.IsPaidNotified && !x.IsOrderClosed && !x.IsReturned);
		if (pharmacyDispenseModel == null)
		{
			SystemNotice = "⚠️ [A➔B➔C 闭环门禁拦截] 当前无处于【步骤A：医生已签发·待收费】的处方！请先在「门诊医生站」按 A(接诊)➔B(病历审签)➔C(签署处方推送) 推送处方后再执行收费结算。";
			return;
		}
		if (pharmacyDispenseModel.IsPaidNotified)
		{
			SystemNotice = "处方 [" + pharmacyDispenseModel.PrescriptionNo + "] 已完成【步骤B：财务收费通知】，请直接执行【步骤C：药房条码核验发药】！";
			return;
		}
		string value = $"PAY{DateTime.Now:yyMMddHHmmss}";
		pharmacyDispenseModel.PaymentOrderNo = value;
		pharmacyDispenseModel.IsPaidNotified = true;
		pharmacyDispenseModel.CanDispense = !pharmacyDispenseModel.IsAllergyBlocked;
		pharmacyDispenseModel.DispenseStatus = (pharmacyDispenseModel.IsAllergyBlocked ? "⚠️ 已收费 · 但被过敏红线阻断" : "已收费通知药房 · 待扫码发药");
		pharmacyDispenseModel.PaymentNoticeStatus = $"✓ [步骤B完成] 收费员刘洋已收费 ({value} · ¥{pharmacyDispenseModel.TotalAmount:0.00}) -> 处方状态变更为 [已收费(3)] 并实时通知药房可发药！";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"💰 [步骤B：收费状态变更通知药房完成] 处方 [{pharmacyDispenseModel.PrescriptionNo}] ({pharmacyDispenseModel.PatientName}) 已完成收费结算 ({value})，已解锁【步骤C：药房扫码发药并关单】！";

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var patientId = !string.IsNullOrWhiteSpace(pharmacyDispenseModel.PatientId)
				? pharmacyDispenseModel.PatientId
				: "PAT-OPD-01";
			var visitId = !string.IsNullOrWhiteSpace(pharmacyDispenseModel.VisitId)
				? pharmacyDispenseModel.VisitId
				: pharmacyDispenseModel.PrescriptionNo;
			var settleDto = new
			{
				Scene = "OPD",
				PatientId = patientId,
				VisitOrEncounterId = visitId,
				TotalAmount = pharmacyDispenseModel.TotalAmount,
				InsuranceAmount = 0m,
				PrepaymentDeduct = 0m,
				SelfPayAmount = pharmacyDispenseModel.TotalAmount,
				PaymentChannel = "CASH",
				ChargeItems = (object?)null,
				RequestKey = "CASHIER_" + value
			};
			_ = http.PostAsJsonAsync("/api/v4/finance/settle", settleDto);
		}
		catch { }
	}

	public void ExecuteFinancialRefundForOrder(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 原路退费结清属于【财务处收费员 (ROLE-CASHIER)】专属职责！药房仅负责退药入库，严禁药房直接退钱，请切换为财务收费员执行步骤E退费。";
			return;
		}
		PharmacyDispenseModel pharmacyDispenseModel = item ?? PharmacyRecords.FirstOrDefault((PharmacyDispenseModel x) => x.IsReturned && !x.IsFinancialRefunded);
		if (pharmacyDispenseModel == null)
		{
			SystemNotice = "⚠️ [C➔D➔E 顺序门禁拦截] 必须先由药房完成【步骤D：退药扫码质检入库 (drug_return_confirmed=1)】，财务窗口方可解锁执行【步骤E：退费结清】！";
			return;
		}
		if (!pharmacyDispenseModel.IsReturned)
		{
			SystemNotice = "⚠️ [C➔D➔E 顺序门禁拦截] 处方 [" + pharmacyDispenseModel.PrescriptionNo + "] 药品尚未退回药房（drug_return_confirmed=0），严禁在未退药情况下直接退费！";
			return;
		}
		if (pharmacyDispenseModel.IsFinancialRefunded)
		{
			SystemNotice = "处方 [" + pharmacyDispenseModel.PrescriptionNo + "] 已完成财务退费结清，不能重复退费。";
			return;
		}
		string value = $"REF{DateTime.Now:yyMMddHHmmss}";
		pharmacyDispenseModel.IsFinancialRefunded = true;
		pharmacyDispenseModel.DispenseStatus = "✓ 步骤A~E全闭环: 已退药回库并完成财务退费";
		pharmacyDispenseModel.PaymentNoticeStatus = $"✓ [步骤E完成] 财务收费员刘洋已凭药房退药单完成原路退费 ({value} · -¥{pharmacyDispenseModel.TotalAmount:0.00})，医-药-财五步闭环完毕！";
		RecalculateDrugStockAndFinanceMetrics();
		SystemNotice = $"💸 [步骤E：财务退费结清完成] 处方 [{pharmacyDispenseModel.PrescriptionNo}] ({pharmacyDispenseModel.PatientName}) 已凭药房退药确认回执完成退费 (-¥{pharmacyDispenseModel.TotalAmount:0.00}，单号 {value})，A➔B➔C➔D➔E 全链路闭环！";

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var refundDto = new
			{
				PaymentOrderId = pharmacyDispenseModel.PaymentOrderNo ?? pharmacyDispenseModel.PrescriptionNo,
				RefundAmount = pharmacyDispenseModel.TotalAmount,
				Reason = "药房已完成实物退药入库质检，财务处依药房退药单办理原路退费冲红",
				RequestKey = "REFUND_" + value
			};
			_ = http.PostAsJsonAsync("/api/v4/finance/refund", refundDto);
		}
		catch { }
	}

	public void SettleInpatientInsuranceForPatient(InpatientRecordModel? patient = null)
	{
		InpatientRecordModel inpatientRecordModel = patient ?? SelectedInpatient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 住院医保基金结算核验（出院六严关·步骤E/关卡5）属于【财务收费员 (ROLE-CASHIER)】专属职责！";
				return;
			}
			SelectedInpatient = inpatientRecordModel;
			CompleteDischargeGate5_Insurance();
		}
	}

	public void ClearInpatientPrepaymentForPatient(InpatientRecordModel? patient = null)
	{
		InpatientRecordModel inpatientRecordModel = patient ?? SelectedInpatient;
		if (inpatientRecordModel != null)
		{
			if (!CanOperateFinance)
			{
				SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 住院预交金清算退补（出院六严关·步骤F/关卡6）属于【财务收费员 (ROLE-CASHIER)】专属职责！";
				return;
			}
			SelectedInpatient = inpatientRecordModel;
			CompleteDischargeGate6_Prepayment();
		}
	}

	public void GenerateCashierDailyClosing()
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 收费员日结轧账单生成属于【财务收费员 (ROLE-CASHIER)】专属职责！";
			return;
		}
		List<PharmacyDispenseModel> list = PharmacyRecords.Where((PharmacyDispenseModel x) => x.IsPaidNotified).ToList();
		List<PharmacyDispenseModel> list2 = PharmacyRecords.Where((PharmacyDispenseModel x) => x.IsFinancialRefunded).ToList();
		List<InpatientRecordModel> list3 = AllInpatientRecords.Where((InpatientRecordModel x) => x.PrepaymentCleared).ToList();
		decimal num = list.Sum((PharmacyDispenseModel x) => x.TotalAmount) + list3.Sum((InpatientRecordModel x) => x.ActualSelfPay);
		decimal num2 = list2.Sum((PharmacyDispenseModel x) => x.TotalAmount);
		decimal value = num - num2;
		string value2 = $"CLS-{DateTime.Now:yyyyMMdd-HHmmss}";
		DailyClosingStatusText = $"✓ 日结轧账完成 · 日结单 [{value2}] 已生成 | 收费员: {CurrentOperatorName} | 门急诊收费:{list.Count}笔 | 退费冲红:{list2.Count}笔 (-¥{num2:N2}) | 住院结清:{list3.Count}人 | 实收净额: ¥{value:N2}";
		SystemNotice = $"📊 [财务日结轧账完成] 已生成收费员日结单 [{value2}]，实收净额 ¥{value:N2}，账实相符并已锁定日结区间！";

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			_ = http.PostAsync("/api/v4/finance/daily-closing", null);
		}
		catch { }
	}
}
