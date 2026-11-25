using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class FinPaymentOrderDto
{
	public string Id { get; set; } = string.Empty;
	public string PaymentNo { get; set; } = string.Empty;
	public decimal TotalAmount { get; set; }
	public int Status { get; set; }
}

public sealed class FinRefundOrderDto
{
	public string Id { get; set; } = string.Empty;
	public string RefundNo { get; set; } = string.Empty;
	public decimal RefundAmount { get; set; }
	public int Status { get; set; }
}

public sealed class FinDailyClosingDto
{
	public string Id { get; set; } = string.Empty;
	public string ClosingNo { get; set; } = string.Empty;
	public decimal TotalIncome { get; set; }
	public decimal TotalRefund { get; set; }
	public decimal NetAmount { get; set; }
}

public partial class WorkbenchModel
{
	public bool LastFinancialOperationSuccess { get; set; }

	public async Task<bool> CashierSettleAndNotifyPharmacyAsync(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 处方收费结算属于【财务处收费员 (ROLE-CASHIER)】专属职责！当前角色无收费权限，请点击「切换为财务收费员」后再操作。";
			return false;
		}

		PharmacyDispenseModel? target = item;
		if (target == null)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = "⚠️ [操作拦截] 请先在列表中明确选中需要收费结算的处方订单，系统禁止未选单自动结算！";
			return false;
		}

		if (target.IsPaidNotified)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = $"处方 [{target.PrescriptionNo}] 已完成收费通知，请直接执行【步骤C：药房条码核验发药】！";
			return false;
		}

		string clientRequestKey = "CASHIER_" + Guid.NewGuid().ToString("N");
		var patientId = !string.IsNullOrWhiteSpace(target.PatientId) ? target.PatientId : "PAT-OPD-01";
		var visitId = !string.IsNullOrWhiteSpace(target.VisitId) ? target.VisitId : target.PrescriptionNo;

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var settleDto = new
			{
				Scene = "OPD",
				PatientId = patientId,
				VisitOrEncounterId = visitId,
				TotalAmount = target.TotalAmount,
				InsuranceAmount = 0m,
				PrepaymentDeduct = 0m,
				SelfPayAmount = target.TotalAmount,
				PaymentChannel = "CASH",
				ChargeItems = (object?)null,
				RequestKey = clientRequestKey
			};

			var response = await http.PostAsJsonAsync("/api/v4/finance/settle", settleDto);
			var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<FinPaymentOrderDto>>();

			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code is not (0 or 200) || envelope.Data == null)
			{
				LastFinancialOperationSuccess = false;
				string errMsg = envelope?.Message ?? response.ReasonPhrase ?? "服务端结算处理失败";
				SystemNotice = $"❌ [收费失败] 处方 [{target.PrescriptionNo}] 结算被服务端拦截: {errMsg}";
				return false;
			}

			string serverPaymentNo = !string.IsNullOrWhiteSpace(envelope.Data.PaymentNo)
				? envelope.Data.PaymentNo
				: (!string.IsNullOrWhiteSpace(envelope.Data.Id) ? envelope.Data.Id : $"PAY{DateTime.Now:yyMMddHHmmss}");

			target.PaymentOrderNo = serverPaymentNo;
			target.IsPaidNotified = true;
			target.CanDispense = !target.IsAllergyBlocked;
			target.DispenseStatus = (target.IsAllergyBlocked ? "⚠️ 已收费 · 但被过敏红线阻断" : "已收费通知药房 · 待扫码发药");
			target.PaymentNoticeStatus = $"✓ [已完成收费] 收费单号: {serverPaymentNo} · 实收 ¥{target.TotalAmount:0.00} -> 处方状态变更为 [已收费(3)] 并实时通知药房可发药！";
			RecalculateDrugStockAndFinanceMetrics();
			LastFinancialOperationSuccess = true;
			SystemNotice = $"💰 [步骤B：收费完成] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 结算成功 (后台凭证: {serverPaymentNo} · ¥{target.TotalAmount:0.00})，已推送药房发药队列！";
			return true;
		}
		catch (Exception ex)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = $"❌ [收费超时/网络异常] 处方 [{target.PrescriptionNo}] 结算请求未获确认: {ex.Message}，未更改本地支付状态，请核对原单后重试！";
			return false;
		}
	}

	public void CashierSettleAndNotifyPharmacy(PharmacyDispenseModel? item = null)
	{
		_ = CashierSettleAndNotifyPharmacyAsync(item);
	}

	public async Task<bool> ExecuteFinancialRefundForOrderAsync(PharmacyDispenseModel? item = null)
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 原路退费结清属于【财务处收费员 (ROLE-CASHIER)】专属职责！药房仅负责退药入库，严禁药房直接退钱，请切换为财务收费员执行步骤E退费。";
			return false;
		}

		PharmacyDispenseModel? target = item;
		if (target == null)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = "⚠️ [操作拦截] 请先在列表中明确选中需要退费冲红的订单，系统禁止未选单自动退款！";
			return false;
		}

		if (!target.IsReturned)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = $"⚠️ [C➔D➔E 顺序门禁拦截] 处方 [{target.PrescriptionNo}] 药品尚未退回药房（drug_return_confirmed=0），严禁在未退药情况下直接退费！";
			return false;
		}
		if (target.IsFinancialRefunded)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = $"处方 [{target.PrescriptionNo}] 已完成财务退费结清，不能重复退费。";
			return false;
		}

		string clientRequestKey = "REFUND_" + Guid.NewGuid().ToString("N");
		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var refundDto = new
			{
				PaymentOrderId = target.PaymentOrderNo ?? target.PrescriptionNo,
				RefundAmount = target.TotalAmount,
				Reason = "药房已完成实物退药入库质检，财务处依药房退药单办理原路退费冲红",
				RequestKey = clientRequestKey
			};

			var response = await http.PostAsJsonAsync("/api/v4/finance/refund", refundDto);
			var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<FinRefundOrderDto>>();

			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code is not (0 or 200) || envelope.Data == null)
			{
				LastFinancialOperationSuccess = false;
				string errMsg = envelope?.Message ?? response.ReasonPhrase ?? "服务端退费处理失败";
				SystemNotice = $"❌ [退费失败] 处方 [{target.PrescriptionNo}] 退款被服务端拦截: {errMsg}";
				return false;
			}

			string serverRefundNo = !string.IsNullOrWhiteSpace(envelope.Data.RefundNo)
				? envelope.Data.RefundNo
				: (!string.IsNullOrWhiteSpace(envelope.Data.Id) ? envelope.Data.Id : $"REF{DateTime.Now:yyMMddHHmmss}");

			target.IsFinancialRefunded = true;
			target.DispenseStatus = "✓ 步骤A~E全闭环: 已退药回库并完成财务退费";
			target.PaymentNoticeStatus = $"✓ [步骤E完成] 财务收费员已凭药房退药单完成原路退费 (红字单据: {serverRefundNo} · -¥{target.TotalAmount:0.00})，医-药-财五步闭环完毕！";
			RecalculateDrugStockAndFinanceMetrics();
			LastFinancialOperationSuccess = true;
			SystemNotice = $"💸 [步骤E：财务退费冲红完成] 处方 [{target.PrescriptionNo}] ({target.PatientName}) 已凭药房退药确认回执完成原路退款 (-¥{target.TotalAmount:0.00}，单号 {serverRefundNo})！";
			return true;
		}
		catch (Exception ex)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = $"❌ [退费超时/网络异常] 处方 [{target.PrescriptionNo}] 退款请求未获确认: {ex.Message}，未更改本地状态，请核对原单！";
			return false;
		}
	}

	public void ExecuteFinancialRefundForOrder(PharmacyDispenseModel? item = null)
	{
		_ = ExecuteFinancialRefundForOrderAsync(item);
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

	public async Task<bool> GenerateCashierDailyClosingAsync()
	{
		EnsurePharmacyWarehouseFinanceInitialized();
		if (!CanOperateFinance)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = "🛡️ [职责分离 SoD 权限拦截] 收费员日结轧账单生成属于【财务收费员 (ROLE-CASHIER)】专属职责！";
			return false;
		}

		try
		{
			using HttpClient http = CreateConfigHttpClient();
			var response = await http.PostAsync("/api/v4/finance/daily-closing", null);
			var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<FinDailyClosingDto>>();

			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code is not (0 or 200) || envelope.Data == null)
			{
				LastFinancialOperationSuccess = false;
				string errMsg = envelope?.Message ?? response.ReasonPhrase ?? "服务端日结处理失败";
				SystemNotice = $"❌ [日结失败] 日结请求被服务端拦截: {errMsg}";
				return false;
			}

			var closing = envelope.Data;
			string serverClosingNo = !string.IsNullOrWhiteSpace(closing.ClosingNo)
				? closing.ClosingNo
				: (!string.IsNullOrWhiteSpace(closing.Id) ? closing.Id : $"CLS-{DateTime.Now:yyyyMMdd-HHmmss}");

			DailyClosingStatusText = $"✓ 服务端日结完成 · 日结单 [{serverClosingNo}] | 收费员: {CurrentOperatorName} | 门诊/住院实收: ¥{closing.TotalIncome:N2} | 退款冲红: -¥{closing.TotalRefund:N2} | 实缴净额: ¥{closing.NetAmount:N2}";
			LastFinancialOperationSuccess = true;
			SystemNotice = $"📊 [财务日结轧账完成] 服务端已锁定今日流水，正式日结单 [{serverClosingNo}]，实收净额 ¥{closing.NetAmount:N2}！";
			return true;
		}
		catch (Exception ex)
		{
			LastFinancialOperationSuccess = false;
			SystemNotice = $"❌ [日结超时/网络异常] 日结请求未获确认: {ex.Message}";
			return false;
		}
	}

	public void GenerateCashierDailyClosing()
	{
		_ = GenerateCashierDailyClosingAsync();
	}
}
