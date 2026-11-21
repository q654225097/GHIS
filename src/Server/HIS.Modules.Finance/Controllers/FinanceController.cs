using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Finance.Models;
using HIS.Modules.Finance.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Finance.Controllers;

[ApiController]
[Route("api/v4/finance")]
public class FinanceController : ControllerBase
{
	public sealed record SettlePaymentRequestDto(string Scene, string PatientId, string VisitOrEncounterId, decimal TotalAmount, decimal InsuranceAmount, decimal PrepaymentDeduct, decimal SelfPayAmount, string PaymentChannel, List<FinChargeLedger>? ChargeItems, string? RequestKey = null, string? CashierUserId = null, string? PrepaymentId = null, decimal? CashTendered = null);

	public sealed record ExecuteRefundRequestDto(string PaymentOrderId, decimal RefundAmount, string Reason, string? PharmacistReturnConfirmDocId = null, string? RequestKey = null);

	public sealed record RecordInpatientFeeItemRequestDto(string EncounterId, string PatientId, int FeeDateInt, string ItemCategory, string ItemCode, string ItemName, string? Spec, string UnitName, decimal UnitPrice, decimal Quantity, decimal SelfPayRatio, string ReimburseType, string ChargeDeptId, string ExecDeptId, string? OrderId = null);

	public sealed record DailyStatementItemDto(int FeeDateInt, int ItemCount, decimal TotalAmount, decimal SelfPayAmount, IReadOnlyList<FinInpatientFeeItem> Items);

	private readonly IRepository<FinPaymentOrder> _paymentRepo;

	private readonly IRepository<FinRefundOrder> _refundRepo;

	private readonly IRepository<FinChargeLedger> _chargeRepo;

	private readonly IRepository<FinDailyClosing> _closingRepo;

	private readonly IRepository<FinInpatientFeeItem> _inpatientFeeRepo;

	private readonly IMedicalInsuranceAdapter? _insuranceAdapter;

	private readonly IDbConnectionFactory _connFactory;

	private readonly IAuditTrailContract? _auditTrail;

	private readonly IOutboxContract? _outbox;

	private readonly IUserSession? _userSession;

	private readonly IIdempotencyContract? _idempotency;

	public FinanceController(
		IRepository<FinPaymentOrder> paymentRepo,
		IRepository<FinRefundOrder> refundRepo,
		IRepository<FinChargeLedger> chargeRepo,
		IRepository<FinDailyClosing> closingRepo,
		IDbConnectionFactory connFactory,
		IAuditTrailContract? auditTrail = null,
		IOutboxContract? outbox = null,
		IUserSession? userSession = null,
		IIdempotencyContract? idempotency = null,
		IRepository<FinInpatientFeeItem>? inpatientFeeRepo = null,
		IMedicalInsuranceAdapter? insuranceAdapter = null)
	{
		_paymentRepo = paymentRepo;
		_refundRepo = refundRepo;
		_chargeRepo = chargeRepo;
		_closingRepo = closingRepo;
		_connFactory = connFactory;
		_auditTrail = auditTrail;
		_outbox = outbox;
		_userSession = userSession;
		_idempotency = idempotency;
		_inpatientFeeRepo = inpatientFeeRepo ?? new DapperRepository<FinInpatientFeeItem>(connFactory, "fin_inpatient_fee_items");
		_insuranceAdapter = insuranceAdapter;
	}

	private bool CanCollectPayment()
	{
		if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId)) return false;
		return RoleCode.IsAdmin(_userSession.RoleCode)
			|| RoleCode.HasRole(_userSession.Roles, RoleCode.Admin)
			|| RoleCode.HasRole(_userSession.Roles, RoleCode.Cashier);
	}

	[HttpPost("settle")]
	public async Task<ApiResponse<FinPaymentOrder>> SettlePayment([FromBody] SettlePaymentRequestDto req)
	{
		if (!CanCollectPayment())
			return ApiResponse<FinPaymentOrder>.Fail(403, "仅收费员可以办理收费结算");
		if (req == null) return ApiResponse<FinPaymentOrder>.Fail(400, "结算内容不能为空");
		var scene = FinanceSettlementService.NormalizeScene(req.Scene);
		if (scene != "OPD")
			return ApiResponse<FinPaymentOrder>.Fail(400, "住院费用请通过住院出院结算流程办理，不能使用门诊收费接口");
		if (string.IsNullOrWhiteSpace(req.PatientId) || string.IsNullOrWhiteSpace(req.VisitOrEncounterId))
			return ApiResponse<FinPaymentOrder>.Fail(400, "患者与门诊就诊号不能为空");
		string normalizedChannel = string.IsNullOrWhiteSpace(req.PaymentChannel) ? "CASH" : req.PaymentChannel.Trim().ToUpperInvariant();
		if (req.SelfPayAmount > 0m && normalizedChannel != "CASH")
		{
			return ApiResponse<FinPaymentOrder>.Fail(400, $"当前版本支付渠道仅开放现金（CASH）实收，不支持渠道 [{normalizedChannel}]");
		}
		decimal cashTendered = req.CashTendered ?? req.SelfPayAmount;
		if (req.SelfPayAmount > 0m && cashTendered < req.SelfPayAmount)
		{
			return ApiResponse<FinPaymentOrder>.Fail(400, $"现金实收金额 (¥{cashTendered:0.00}) 小于应收自付金额 (¥{req.SelfPayAmount:0.00})");
		}
		decimal changeAmount = req.SelfPayAmount > 0m ? Math.Round(cashTendered - req.SelfPayAmount, 2) : 0m;
		if (req.InsuranceAmount < 0 || req.PrepaymentDeduct < 0 || req.SelfPayAmount < 0)
			return ApiResponse<FinPaymentOrder>.Fail(400, "医保、预交和自费金额不能为负数");
		decimal componentSum = req.InsuranceAmount + req.PrepaymentDeduct + req.SelfPayAmount;
		var cashierId = _userSession!.UserId;
		if (req.TotalAmount > 0m && Math.Abs(componentSum - req.TotalAmount) > 0.01m)
		{
			return ApiResponse<FinPaymentOrder>.Fail(400, $"结算金额勾稽校验失败：总金额 ({req.TotalAmount:0.00}) != 医保统筹 ({req.InsuranceAmount:0.00}) + 预交金抵扣 ({req.PrepaymentDeduct:0.00}) + 个人自付 ({req.SelfPayAmount:0.00})");
		}
		if (!string.IsNullOrWhiteSpace(req.RequestKey))
		{
			if (_idempotency != null)
			{
				string idemKey = "FIN_SETTLE_" + req.RequestKey.Trim();
				var (isDup, cachedJson, isConflict) = await _idempotency.TryAcquireAsync(idemKey, JsonSerializer.Serialize(req));
				if (isConflict)
				{
					return ApiResponse<FinPaymentOrder>.Fail(409, "请求幂等键冲突：已被不同的结算请求使用");
				}
				if (isDup && !string.IsNullOrWhiteSpace(cachedJson))
				{
					try
					{
						var cached = JsonSerializer.Deserialize<FinPaymentOrder>(cachedJson);
						if (cached != null)
						{
							return ApiResponse<FinPaymentOrder>.Success(cached, "幂等命中：已返回原结算单据（防重复扣费）");
						}
					}
					catch { }
				}
			}

			FinPaymentOrder existingByKey = await _paymentRepo.QuerySingleOrDefaultAsync("SELECT * FROM fin_payment_orders WHERE request_key = @Key", new
			{
				Key = req.RequestKey.Trim()
			});
			if (existingByKey != null)
			{
				if (!string.Equals(existingByKey.PatientId, req.PatientId, StringComparison.OrdinalIgnoreCase) ||
					!string.Equals(existingByKey.VisitOrEncounterId, req.VisitOrEncounterId, StringComparison.OrdinalIgnoreCase))
					return ApiResponse<FinPaymentOrder>.Fail(409, "请求幂等键已被其他患者或就诊使用");
				return ApiResponse<FinPaymentOrder>.Success(existingByKey, "幂等命中：已返回原结算单据");
			}
		}
		List<string> notifiedPrescriptionNos = new List<string>();
		var paymentId = Guid.NewGuid().ToString("D");
		var paymentNo = FinanceSettlementService.NewDocumentNo("PAY", SystemTime.UtcNowMs);
		using (var preflight = _connFactory.CreateConnection())
		{
			var visit = await preflight.QuerySingleOrDefaultAsync<OpdVisitSettlementRow>(
				"SELECT id, patient_id, opd_dept_id, status FROM opd_visits WHERE id = @VisitId",
				new { VisitId = req.VisitOrEncounterId });
			if (visit == null) return ApiResponse<FinPaymentOrder>.Fail(404, "门诊就诊记录不存在");
			if (!string.Equals(visit.PatientId, req.PatientId, StringComparison.OrdinalIgnoreCase))
				return ApiResponse<FinPaymentOrder>.Fail(409, "结算患者与门诊就诊记录不一致");
			if (visit.Status is not (1 or 2)) return ApiResponse<FinPaymentOrder>.Fail(409, "该门诊就诊已完成或已取消，不能收费");
			var (serverTotal, totalError) = await FinanceSettlementService.LoadOpdPrescriptionTotalAsync(
				preflight, req.VisitOrEncounterId, req.PatientId, null, CancellationToken.None);
			if (totalError != null) return ApiResponse<FinPaymentOrder>.Fail(400, totalError);
			if (serverTotal <= 0m) return ApiResponse<FinPaymentOrder>.Fail(400, "服务端处方应收总额为零，不能收费");
			if (req.PrepaymentDeduct > 0m)
			{
				if (string.IsNullOrWhiteSpace(req.PrepaymentId))
					return ApiResponse<FinPaymentOrder>.Fail(400, "预交金抵扣必须指定原预交金单据");
				var prepaid = await preflight.QuerySingleOrDefaultAsync<decimal>(
					"SELECT COALESCE(SUM(amount), 0) FROM ipd_prepayments WHERE id = @Id AND encounter_id = @EncounterId AND patient_id = @PatientId",
					new { Id = req.PrepaymentId, EncounterId = req.VisitOrEncounterId, PatientId = req.PatientId });
				if (prepaid < req.PrepaymentDeduct)
					return ApiResponse<FinPaymentOrder>.Fail(400, "预交金单据金额不足或不属于当前患者");
			}
			if (req.TotalAmount > 0m && Math.Abs(req.TotalAmount - serverTotal) > 0.01m)
				return ApiResponse<FinPaymentOrder>.Fail(409,
					$"客户端金额与处方应收金额不一致（客户端 ¥{req.TotalAmount:0.00}，服务端 ¥{serverTotal:0.00}），禁止伪造金额");
			if (Math.Abs(componentSum - serverTotal) > 0.01m)
				return ApiResponse<FinPaymentOrder>.Fail(400,
					$"结算金额勾稽校验失败：应收 ¥{serverTotal:0.00}，实收分项合计 ¥{componentSum:0.00}");
		}

		FinPaymentOrder pay = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			using DbConnection conn = _connFactory.CreateConnection();
			var visit = await conn.QuerySingleOrDefaultAsync<OpdVisitSettlementRow>(new CommandDefinition(
				"SELECT id, patient_id, opd_dept_id, status FROM opd_visits WHERE id = @VisitId",
				new { VisitId = req.VisitOrEncounterId }, _connFactory.CurrentTransaction, cancellationToken: ct));
			if (visit == null) throw new InvalidOperationException("门诊就诊记录不存在");
			if (visit.Status is not (1 or 2)) throw new InvalidOperationException("门诊就诊已完成或已取消，不能收费");
			var current = await FinanceSettlementService.LoadOpdPrescriptionTotalAsync(
				conn, req.VisitOrEncounterId, req.PatientId, _connFactory.CurrentTransaction, ct, paymentId);
			if (current.Error != null)
				throw new InvalidOperationException(current.Error);
			if (Math.Abs(current.Amount - componentSum) > 0.01m)
				throw new InvalidOperationException("收费过程中处方金额发生变化，请重新核对后结算");
			if (req.PrepaymentDeduct > 0m)
			{
				var prepaidClaim = await conn.ExecuteAsync(new CommandDefinition(
					"UPDATE ipd_prepayments SET ext_data_json = @ExtJson, updated_at_ms = @Now, updated_by = @Cashier, version_no = version_no + 1 WHERE id = @Id AND encounter_id = @EncounterId AND patient_id = @PatientId AND amount >= @Amount AND NOT EXISTS (SELECT 1 FROM fin_payment_orders WHERE prepayment_id = @Id)",
					new { Id = req.PrepaymentId, EncounterId = req.VisitOrEncounterId, PatientId = req.PatientId, Amount = req.PrepaymentDeduct, Now = SystemTime.UtcNowMs, Cashier = cashierId, ExtJson = JsonSerializer.Serialize(new { consumedByPaymentId = paymentId, consumedAtMs = SystemTime.UtcNowMs }) },
					_connFactory.CurrentTransaction, cancellationToken: ct));
				if (prepaidClaim != 1)
					throw new InvalidOperationException("预交金已被使用或金额不足，收费已取消");
			}
			FinPaymentOrder payment = new FinPaymentOrder
			{
				Id = paymentId,
				PaymentNo = paymentNo,
				RequestKey = req.RequestKey?.Trim(),
				Scene = scene,
				PatientId = req.PatientId,
				VisitOrEncounterId = req.VisitOrEncounterId,
				TotalAmount = current.Amount,
				InsuranceAmount = req.InsuranceAmount,
				PrepaymentDeduct = req.PrepaymentDeduct,
				SelfPayAmount = req.SelfPayAmount,
				PaymentChannel = req.SelfPayAmount > 0m ? "CASH" : normalizedChannel,
			ExtDataJson = JsonSerializer.Serialize(new { cashTendered, changeAmount, paymentMode = "CASH_ONLY" }),
				PrepaymentId = req.PrepaymentDeduct > 0m ? req.PrepaymentId : null,
				Status = 1,
				PaidAtMs = SystemTime.UtcNowMs,
				InvoiceOfdId = FinanceSettlementContract.GenerateFiscalInvoiceNo("BILL"),
				CreatedBy = cashierId,
				UpdatedBy = cashierId
			};
			await _paymentRepo.InsertAsync(payment, ct);
			var ledger = new FinChargeLedger
			{
				PatientId = req.PatientId,
				VisitOrEncounterId = req.VisitOrEncounterId,
				PaymentOrderId = payment.Id,
				OrderDeptId = string.IsNullOrWhiteSpace(visit.OpdDeptId) ? "DEPT-OPD-01" : visit.OpdDeptId,
				ExecDeptId = string.IsNullOrWhiteSpace(visit.OpdDeptId) ? "DEPT-OPD-01" : visit.OpdDeptId,
				ItemCode = "OPD_PRESCRIPTION",
				ItemName = "门诊处方药品费",
				Quantity = 1,
				UnitPrice = current.Amount,
				TotalAmount = current.Amount,
				Status = 1,
				CreatedBy = cashierId,
				UpdatedBy = cashierId
			};
			await _chargeRepo.InsertAsync(ledger, ct);
			long nowMs = SystemTime.UtcNowMs;
			var (paidPrescriptions, claimError) = await FinanceSettlementService.ClaimReviewedPrescriptionsAsync(
				conn, req.VisitOrEncounterId, _connFactory.CurrentTransaction, nowMs, cashierId, paymentId, ct);
			if (claimError != null) throw new InvalidOperationException(claimError);
			foreach (var rx in paidPrescriptions)
			{
				var dispenseCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
					"SELECT count(*) FROM phm_dispense_orders WHERE prescription_id = @RxId",
					new { RxId = rx.Id }, _connFactory.CurrentTransaction, cancellationToken: ct));
				if (dispenseCount == 0)
				{
					var waitDispenseId = Guid.NewGuid().ToString("D");
					await conn.ExecuteAsync(new CommandDefinition(
						"""
						INSERT INTO phm_dispense_orders
						    (id, dispense_no, pharmacy_id, prescription_id, ward_summary_id,
						     patient_id, status, dispensed_by_id, dispensed_at_ms,
						     drug_return_confirmed, ext_data_json, schema_version,
						     created_at_ms, created_by, updated_at_ms, updated_by, version_no)
						VALUES
						    (@Id, @DispenseNo, @PharmacyId, @RxId, NULL,
						     @PatientId, 0, NULL, NULL,
						     0, @ExtJson, 1,
						     @Now, @Cashier, @Now, @Cashier, 1)
						""",
						new
						{
							Id = waitDispenseId,
							DispenseNo = "WAIT-" + rx.PrescriptionNo,
							PharmacyId = string.IsNullOrWhiteSpace(rx.TargetPharmacyId) ? "DEPT-PHM-01" : rx.TargetPharmacyId,
							RxId = rx.Id,
							PatientId = rx.PatientId,
							ExtJson = JsonSerializer.Serialize(new
							{
								paymentNo = payment.PaymentNo,
								paidAtMs = nowMs,
								notificationState = "READY_TO_DISPENSE"
							}),
							Now = nowMs,
							Cashier = cashierId
						}, _connFactory.CurrentTransaction, cancellationToken: ct));
				}

				var outboxPayload = JsonSerializer.Serialize(new
				{
					EventType = "PHM_READY_TO_DISPENSE",
					PaymentId = payment.Id,
					PaymentNo = payment.PaymentNo,
					PrescriptionId = rx.Id,
					PrescriptionNo = rx.PrescriptionNo,
					TargetPharmacyId = rx.TargetPharmacyId,
					PatientId = rx.PatientId,
					TotalAmount = rx.TotalAmount,
					NotifiedAtMs = nowMs
				});
				await conn.ExecuteAsync(new CommandDefinition(
					"""
					INSERT INTO ops_outbox_messages
					    (id, event_type, payload_json, status, retry_count, last_error,
					     ext_data_json, schema_version, created_at_ms, created_by,
					     updated_at_ms, updated_by, version_no)
					VALUES
					    (@Id, 'PHM_READY_TO_DISPENSE', @Payload, 0, 0, NULL,
					     @ExtJson, 1, @Now, @Cashier,
					     @Now, @Cashier, 1)
					""",
					new
					{
						Id = Guid.NewGuid().ToString("D"),
						Payload = outboxPayload,
						ExtJson = JsonSerializer.Serialize(new { prescriptionId = rx.Id, paymentId = payment.Id }),
						Now = nowMs,
						Cashier = cashierId
					}, _connFactory.CurrentTransaction, cancellationToken: ct));
				notifiedPrescriptionNos.Add(rx.PrescriptionNo);
			}

			await conn.ExecuteAsync(new CommandDefinition(
				"""
				UPDATE mtc_service_requests
				SET status = 1, updated_at_ms = @Now, updated_by = @Cashier, version_no = version_no + 1
				WHERE source_visit_id = @VisitId AND source_type = 'OPD' AND is_fee_confirmed = 0 AND status = 0
				""",
				new { VisitId = req.VisitOrEncounterId, Now = nowMs, Cashier = cashierId },
				_connFactory.CurrentTransaction, cancellationToken: ct));

			return payment;
		});
		string insTraceText = "";
		if (pay.InsuranceAmount > 0m && _insuranceAdapter != null)
		{
			string insTrace = await _insuranceAdapter.SettleAsync(req.PatientId, pay.TotalAmount, CancellationToken.None);
			insTraceText = $"（医保统筹结算流水: {insTrace}）";
		}

		if (_auditTrail != null)
		{
			string rxNotifyText = ((notifiedPrescriptionNos.Count > 0) ? ("，已推送药房可发药通知 [" + string.Join(",", notifiedPrescriptionNos) + "]") : "");
			await _auditTrail.RecordAuditAsync("FIN_SETTLE", "FinPaymentOrder", pay.Id, $"结算单 [{pay.PaymentNo}] 完成混合收款 ¥{pay.TotalAmount:0.00}{insTraceText}，电子票据号：{pay.InvoiceOfdId}{rxNotifyText}", cashierId);
		}
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			await _idempotency.SaveResponseAsync("FIN_SETTLE_" + req.RequestKey.Trim(), JsonSerializer.Serialize(pay));
		}
		string noticeSuffix = ((notifiedPrescriptionNos.Count > 0) ? ("，处方状态已变更为【已收费·待发药】并实时通知目标药房配药（处方号：" + string.Join(", ", notifiedPrescriptionNos) + "）") : "");
		return ApiResponse<FinPaymentOrder>.Success(pay, "医保混合结算完成，已实时开具财政电子票据 OFD" + noticeSuffix);
	}

	[HttpPost("refund")]
	public async Task<ApiResponse<FinRefundOrder>> ExecuteRefund([FromBody] ExecuteRefundRequestDto req)
	{
		if (!CanCollectPayment())
			return ApiResponse<FinRefundOrder>.Fail(403, "仅收费员可以办理退费");
		if (req == null || string.IsNullOrWhiteSpace(req.PaymentOrderId))
			return ApiResponse<FinRefundOrder>.Fail(400, "原结算单号不能为空");
		if (req.RefundAmount <= 0m)
			return ApiResponse<FinRefundOrder>.Fail(400, "退费金额必须大于零");
		if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length > 500)
			return ApiResponse<FinRefundOrder>.Fail(400, "退费原因不能为空且不能超过 500 个字符");

		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			string idemKey = "FIN_REFUND_" + req.RequestKey.Trim();
			var (isDup, cachedJson, isConflict) = await _idempotency.TryAcquireAsync(idemKey, JsonSerializer.Serialize(req));
			if (isConflict)
			{
				return ApiResponse<FinRefundOrder>.Fail(409, "请求幂等键冲突：已被不同的退费操作使用");
			}
			if (isDup && !string.IsNullOrWhiteSpace(cachedJson))
			{
				try
				{
					var cachedRefund = JsonSerializer.Deserialize<FinRefundOrder>(cachedJson);
					if (cachedRefund != null)
					{
						return ApiResponse<FinRefundOrder>.Success(cachedRefund, "幂等命中：已返回原退款单据（防重复退款）");
					}
				}
				catch { }
			}
		}
		var cashierId = _userSession!.UserId;
		var now = SystemTime.UtcNowMs;
		var refundNo = FinanceSettlementService.NewDocumentNo("RFD", now);
		FinPaymentOrder pay = null!;
		FinRefundOrder refund;
		try
		{
			refund = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
			{
			using DbConnection conn = _connFactory.CreateConnection();
			pay = (await conn.QuerySingleOrDefaultAsync<FinPaymentOrder>(new CommandDefinition(
				"SELECT * FROM fin_payment_orders WHERE id = @Id",
				new { Id = req.PaymentOrderId }, _connFactory.CurrentTransaction, cancellationToken: ct)))!;
			if (pay == null) throw new InvalidOperationException("原结算单据不存在");
			if (pay.Scene != "OPD")
				throw new InvalidOperationException("住院退费必须通过住院出院结算流程办理");
			if (pay.Status != 1) throw new InvalidOperationException("原结算单不在可退状态");
			var refunded = await FinanceSettlementService.LoadPaymentRefundedAmountAsync(
				conn, pay.Id, _connFactory.CurrentTransaction, ct);
			if (req.RefundAmount > pay.TotalAmount - refunded + 0.0001m)
				throw new InvalidOperationException(
					$"退费金额超过可退余额（可退 ¥{pay.TotalAmount - refunded:0.00}）");
			var refundUnsafe = await FinanceSettlementService.RejectIfUnsafeOpdRefundAsync(
				conn, pay, _connFactory.CurrentTransaction, ct);
			if (refundUnsafe != null) throw new InvalidOperationException(refundUnsafe);
			var dispensed = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
				"""
				SELECT count(*) FROM opd_prescriptions
				WHERE visit_id = @VisitId AND status = 4 AND payment_order_id = @PaymentId
				""",
				new { VisitId = pay.VisitOrEncounterId, PaymentId = pay.Id },
				_connFactory.CurrentTransaction, cancellationToken: ct));
			if (dispensed > 0 && req.RefundAmount < pay.TotalAmount - 0.0001m)
				throw new InvalidOperationException("已发药处方不支持部分退费，请在药房完成整单退药验收后整单冲红");
			var isFullRefund = Math.Abs((refunded + req.RefundAmount) - pay.TotalAmount) <= 0.01m;
			var paymentClaim = await conn.ExecuteAsync(new CommandDefinition(
				"""
				UPDATE fin_payment_orders
				SET status = @Status, updated_at_ms = @Now, updated_by = @Cashier,
				    version_no = version_no + 1
				WHERE id = @Id AND status = 1
				""",
				new { Id = pay.Id, Status = isFullRefund ? 2 : 1, Now = now, Cashier = cashierId },
				_connFactory.CurrentTransaction, cancellationToken: ct));
			if (paymentClaim != 1) throw new InvalidOperationException("原结算单已被其他退费操作变更，请刷新");

			var pendingRx = (await conn.QueryAsync<ReviewedPrescriptionRow>(new CommandDefinition(
				"""
				SELECT id, prescription_no, patient_id, target_pharmacy_id, total_amount, status
				FROM opd_prescriptions
				WHERE visit_id = @VisitId AND status = 3 AND payment_order_id = @PaymentId
				""",
				new { VisitId = pay.VisitOrEncounterId, PaymentId = pay.Id },
				_connFactory.CurrentTransaction, cancellationToken: ct))).ToList();
			foreach (var rx in pendingRx)
			{
				var closed = await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE opd_prescriptions
					SET status = 5, updated_at_ms = @Now, updated_by = @Cashier,
					    version_no = version_no + 1
					WHERE id = @Id AND status = 3 AND payment_order_id = @PaymentId
					""",
					new { Id = rx.Id, PaymentId = pay.Id, Now = now, Cashier = cashierId },
					_connFactory.CurrentTransaction, cancellationToken: ct));
				if (closed != 1) throw new InvalidOperationException($"处方 {rx.PrescriptionNo} 状态已变更");
				await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE phm_dispense_orders
					SET status = 4, updated_at_ms = @Now, updated_by = @Cashier,
					    version_no = version_no + 1
					WHERE prescription_id = @RxId AND status = 0
					""",
					new { RxId = rx.Id, Now = now, Cashier = cashierId },
					_connFactory.CurrentTransaction, cancellationToken: ct));
			}
			var ledgers = (await conn.QueryAsync<FinChargeLedger>(new CommandDefinition(
				"""
				SELECT * FROM fin_charge_ledgers
				WHERE payment_order_id = @PaymentId AND status = 1
				ORDER BY created_at_ms
				""",
				new { PaymentId = pay.Id }, _connFactory.CurrentTransaction, cancellationToken: ct))).ToList();
			var remainingRefund = req.RefundAmount;
			foreach (var ledger in ledgers)
			{
				var remaining = ledger.TotalAmount - ledger.RefundAmount;
				var reversal = Math.Min(remaining, remainingRefund);
				if (reversal <= 0m) continue;
				var nextRefund = ledger.RefundAmount + reversal;
				var closed = nextRefund >= ledger.TotalAmount - 0.0001m;
				var ledgerClaim = await conn.ExecuteAsync(new CommandDefinition(
					"""
					UPDATE fin_charge_ledgers
					SET refund_amount = @RefundAmount, status = @Status, updated_at_ms = @Now,
					    updated_by = @Cashier, version_no = version_no + 1
					WHERE id = @Id AND version_no = @Version AND status = 1
					""",
					new { Id = ledger.Id, RefundAmount = nextRefund, Status = closed ? 2 : 1, Now = now, Cashier = cashierId, Version = ledger.VersionNo },
					_connFactory.CurrentTransaction, cancellationToken: ct));
				if (ledgerClaim != 1) throw new InvalidOperationException("费用台账已被其他退费操作变更，请刷新");
				remainingRefund -= reversal;
			}
			if (remainingRefund > 0.01m) throw new InvalidOperationException("原结算单没有足够的未退费用台账");

			var rfd = new FinRefundOrder
			{
				RefundNo = refundNo,
				OriginalPaymentId = pay.Id,
				RefundAmount = req.RefundAmount,
				Reason = req.Reason.Trim(),
				DrugReturnConfirmed = (short)(pendingRx.Count == 0 && dispensed > 0 ? 1 : 0),
				ApprovedBy = cashierId,
				RefundedAtMs = now,
				Status = 1,
				ReissuedInvoiceOfdId = FinanceSettlementContract.GenerateFiscalInvoiceNo("RED-BILL"),
				CreatedBy = cashierId,
				UpdatedBy = cashierId
			};
			await _refundRepo.InsertAsync(rfd, ct);
			return rfd;
		});
		}
		catch (InvalidOperationException ex)
		{
			return ApiResponse<FinRefundOrder>.Fail(400, ex.Message);
		}
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("FIN_REFUND", "FinRefundOrder", refund.Id, $"退费单 [{refund.RefundNo}] 冲红原支付单 [{pay.PaymentNo}]，退款 ¥{refund.RefundAmount:0.00}，原因：{refund.Reason}", cashierId);
		}
		if (!string.IsNullOrWhiteSpace(req.RequestKey) && _idempotency != null)
		{
			await _idempotency.SaveResponseAsync("FIN_REFUND_" + req.RequestKey.Trim(), JsonSerializer.Serialize(refund));
		}
		return ApiResponse<FinRefundOrder>.Success(refund, "财务退款成功，原电子蓝票已冲红，剩余项目已换开重具新票据 OFD");
	}

	[HttpGet("daily-closings")]
	public async Task<ApiResponse<IReadOnlyList<FinDailyClosing>>> GetDailyClosings(
		[FromQuery] string? cashierUserId = null,
		[FromQuery] int? startDateInt = null,
		[FromQuery] int? endDateInt = null)
	{
		return ApiResponse<IReadOnlyList<FinDailyClosing>>.Success(await _closingRepo.QueryAsync(@"
SELECT * FROM fin_daily_closings
WHERE (@Cashier IS NULL OR cashier_user_id = @Cashier)
  AND (@StartDate IS NULL OR closing_date_int >= @StartDate)
  AND (@EndDate IS NULL OR closing_date_int <= @EndDate)
ORDER BY closing_date_int DESC, created_at_ms DESC", new
		{
			Cashier = string.IsNullOrWhiteSpace(cashierUserId) ? null : cashierUserId.Trim(),
			StartDate = startDateInt,
			EndDate = endDateInt
		}));
	}

	[HttpGet("payments")]
	public async Task<ApiResponse<IReadOnlyList<FinPaymentOrder>>> GetPayments([FromQuery] string? patientId = null, [FromQuery] string? visitOrEncounterId = null)
	{
		return ApiResponse<IReadOnlyList<FinPaymentOrder>>.Success(await _paymentRepo.QueryAsync("SELECT * FROM fin_payment_orders\nWHERE (@PatientId IS NULL OR patient_id = @PatientId)\n  AND (@VisitId IS NULL OR visit_or_encounter_id = @VisitId)\nORDER BY created_at_ms DESC", new
		{
			PatientId = patientId,
			VisitId = visitOrEncounterId
		}));
	}

	[HttpPost("daily-closing")]
	public async Task<ApiResponse<FinDailyClosing>> SubmitDailyClosing([FromQuery] string? cashierUserId = null)
	{
		if (!CanCollectPayment())
			return ApiResponse<FinDailyClosing>.Fail(403, "仅收费员可以提交日结");
		var cashierId = _userSession!.UserId;
		if (!string.IsNullOrWhiteSpace(cashierUserId) &&
			!string.Equals(cashierUserId.Trim(), cashierId, StringComparison.OrdinalIgnoreCase))
			return ApiResponse<FinDailyClosing>.Fail(403, "只能提交当前登录收费员的日结");
		var dayStart = SystemTime.TodayStartUtcMs;
		var dayEnd = SystemTime.TomorrowStartUtcMs;
		var todayInt = SystemTime.TodayInt;
		var closing = await _connFactory.ExecuteInTransactionAsync(async (CancellationToken ct) =>
		{
			using DbConnection conn = _connFactory.CreateConnection();
			var existing = await conn.QuerySingleOrDefaultAsync<FinDailyClosing>(new CommandDefinition(
				"SELECT * FROM fin_daily_closings WHERE cashier_user_id = @Cashier AND closing_date_int = @Day",
				new { Cashier = cashierId, Day = todayInt }, _connFactory.CurrentTransaction, cancellationToken: ct));
			if (existing != null) throw new InvalidOperationException($"今日日结单已生成（{existing.ClosingNo}），不能重复提交");
			var income = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
				"""
				SELECT COALESCE(SUM(total_amount), 0) FROM fin_payment_orders
				WHERE paid_at_ms >= @DayStart AND paid_at_ms < @DayEnd
				  AND created_by = @Cashier AND status = 1
				""",
				new { DayStart = dayStart, DayEnd = dayEnd, Cashier = cashierId },
				_connFactory.CurrentTransaction, cancellationToken: ct));
			var prepaid = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
				"""
				SELECT COALESCE(SUM(amount), 0) FROM ipd_prepayments
				WHERE paid_at_ms >= @DayStart AND paid_at_ms < @DayEnd AND cashier_id = @Cashier
				""",
				new { DayStart = dayStart, DayEnd = dayEnd, Cashier = cashierId },
				_connFactory.CurrentTransaction, cancellationToken: ct));
			var refunded = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
				"""
				SELECT COALESCE(SUM(refund_amount), 0) FROM fin_refund_orders
				WHERE refunded_at_ms >= @DayStart AND refunded_at_ms < @DayEnd
				  AND created_by = @Cashier AND status = 1
				""",
				new { DayStart = dayStart, DayEnd = dayEnd, Cashier = cashierId },
				_connFactory.CurrentTransaction, cancellationToken: ct));
			var result = new FinDailyClosing
			{
				ClosingNo = FinanceSettlementService.NewDocumentNo("CLS", SystemTime.UtcNowMs),
				CashierUserId = cashierId,
				ClosingDateInt = todayInt,
				TotalIncome = income + prepaid,
				TotalRefund = refunded,
				NetAmount = income + prepaid - refunded,
				ReconciledStatus = 1,
				CreatedBy = cashierId,
				UpdatedBy = cashierId
			};
			await _closingRepo.InsertAsync(result, ct);
			return result;
		});
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("FIN_DAILY_CLOSING", "FinDailyClosing", closing.Id, $"收费员 [{cashierId}] 日结完成：总收入 ¥{closing.TotalIncome:0.00}，总退费 ¥{closing.TotalRefund:0.00}，实缴净额 ¥{closing.NetAmount:0.00}", cashierId);
		}
		return ApiResponse<FinDailyClosing>.Success(closing, "收费员交接班日结单生成成功，已按实收与退费台账锁定当日流水");
	}

	[HttpPost("inpatient-fee-items")]
	public async Task<ApiResponse<FinInpatientFeeItem>> RecordInpatientFeeItem([FromBody] RecordInpatientFeeItemRequestDto req)
	{
		if (string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.PatientId))
			return ApiResponse<FinInpatientFeeItem>.Fail(400, "就诊 ID 与患者 ID 不能为空");

		string userId = _userSession?.UserId ?? "USER-NURSE";
		decimal total = req.Quantity * req.UnitPrice;

		var item = new FinInpatientFeeItem
		{
			EncounterId = req.EncounterId.Trim(),
			PatientId = req.PatientId.Trim(),
			FeeDateInt = req.FeeDateInt > 0 ? req.FeeDateInt : SystemTime.TodayInt,
			ItemCategory = req.ItemCategory ?? "MEDICINE",
			ItemCode = req.ItemCode,
			ItemName = req.ItemName,
			Spec = req.Spec,
			UnitName = req.UnitName ?? "项",
			UnitPrice = req.UnitPrice,
			Quantity = req.Quantity,
			TotalAmount = total,
			SelfPayRatio = req.SelfPayRatio,
			ReimburseType = req.ReimburseType ?? "CLASS_A",
			ChargeDeptId = req.ChargeDeptId,
			ExecDeptId = req.ExecDeptId,
			OrderId = req.OrderId,
			IsSettled = 0,
			CreatedBy = userId,
			UpdatedBy = userId
		};
		await _inpatientFeeRepo.InsertAsync(item);

		return ApiResponse<FinInpatientFeeItem>.Success(item, "住院费用明细已记录");
	}

	[HttpGet("inpatient-fee-items/{encounterId}")]
	public async Task<ApiResponse<IReadOnlyList<FinInpatientFeeItem>>> GetInpatientFeeItems(string encounterId, [FromQuery] int? feeDateInt = null)
	{
		string sql = @"
SELECT * FROM fin_inpatient_fee_items
WHERE encounter_id = @EncounterId
  AND (@FeeDateInt IS NULL OR fee_date_int = @FeeDateInt)
ORDER BY fee_date_int DESC, created_at_ms DESC";

		var list = await _inpatientFeeRepo.QueryAsync(sql, new { EncounterId = encounterId.Trim(), FeeDateInt = feeDateInt });
		return ApiResponse<IReadOnlyList<FinInpatientFeeItem>>.Success(list);
	}

	[HttpGet("inpatient-fee-items/{encounterId}/daily-statement")]
	public async Task<ApiResponse<IReadOnlyList<DailyStatementItemDto>>> GetDailyStatement(string encounterId)
	{
		var list = await _inpatientFeeRepo.QueryAsync(
			"SELECT * FROM fin_inpatient_fee_items WHERE encounter_id = @EncounterId ORDER BY fee_date_int DESC, created_at_ms DESC",
			new { EncounterId = encounterId.Trim() });

		var groups = list.GroupBy(x => x.FeeDateInt)
			.Select(g => new DailyStatementItemDto(
				FeeDateInt: g.Key,
				ItemCount: g.Count(),
				TotalAmount: g.Sum(x => x.TotalAmount),
				SelfPayAmount: g.Sum(x => x.TotalAmount * x.SelfPayRatio),
				Items: g.ToList()
			))
			.ToList();

		return ApiResponse<IReadOnlyList<DailyStatementItemDto>>.Success(groups);
	}

	/// <summary>
	/// 财务日结标准总账凭证导出（借贷平衡，兼容金蝶/用友标准凭证导入格式）
	/// </summary>
	[HttpGet("closings/voucher-export")]
	public async Task<IActionResult> ExportDailyClosingVoucher([FromQuery] int? closingDateInt = null, [FromQuery] string format = "json")
	{
		int targetDate = closingDateInt is > 20000101
			? closingDateInt.Value
			: int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));

		DateTime dayStartLocal = DateTime.SpecifyKind(
			new DateTime(targetDate / 10000, (targetDate / 100) % 100, targetDate % 100, 0, 0, 0),
			DateTimeKind.Unspecified);
		long startMs = new DateTimeOffset(dayStartLocal, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
		long endMs = startMs + 24L * 3600 * 1000;

		using var conn = _connFactory.CreateConnection();
		var orders = (await conn.QueryAsync<dynamic>(@"
			SELECT scene AS Scene,
			       COALESCE(SUM(self_pay_amount), 0) AS CashTotal,
			       COALESCE(SUM(prepayment_deduct), 0) AS PrepayTotal,
			       COALESCE(SUM(insurance_amount), 0) AS InsTotal,
			       COALESCE(SUM(total_amount), 0) AS GrossTotal
			FROM fin_payment_orders
			WHERE status = 1 AND paid_at_ms >= @StartMs AND paid_at_ms < @EndMs
			GROUP BY scene",
			new { StartMs = startMs, EndMs = endMs })).ToList();

		decimal cashSum = orders.Sum(o => Convert.ToDecimal(o.CashTotal));
		decimal prepaySum = orders.Sum(o => Convert.ToDecimal(o.PrepayTotal));
		decimal insSum = orders.Sum(o => Convert.ToDecimal(o.InsTotal));
		decimal opdGross = orders.Where(o => string.Equals((string?)o.Scene, "OPD", StringComparison.OrdinalIgnoreCase)).Sum(o => Convert.ToDecimal(o.GrossTotal));
		decimal ipdGross = orders.Where(o => !string.Equals((string?)o.Scene, "OPD", StringComparison.OrdinalIgnoreCase)).Sum(o => Convert.ToDecimal(o.GrossTotal));

		var lines = new List<object>();
		if (cashSum > 0m) lines.Add(new { VoucherDate = targetDate, AccountCode = "1001", AccountName = "库存现金", Summary = $"{targetDate} 医院现金收款汇总", Debit = cashSum, Credit = 0m });
		if (prepaySum > 0m) lines.Add(new { VoucherDate = targetDate, AccountCode = "2203", AccountName = "预收医疗款", Summary = $"{targetDate} 住院预交金冲抵", Debit = prepaySum, Credit = 0m });
		if (insSum > 0m) lines.Add(new { VoucherDate = targetDate, AccountCode = "1122", AccountName = "应收医保款", Summary = $"{targetDate} 医保统筹记账", Debit = insSum, Credit = 0m });
		if (opdGross > 0m) lines.Add(new { VoucherDate = targetDate, AccountCode = "4001", AccountName = "医疗收入-门诊", Summary = $"{targetDate} 门诊医疗总收入", Debit = 0m, Credit = opdGross });
		if (ipdGross > 0m) lines.Add(new { VoucherDate = targetDate, AccountCode = "4002", AccountName = "医疗收入-住院", Summary = $"{targetDate} 住院医疗总收入", Debit = 0m, Credit = ipdGross });

		decimal totalDebit = cashSum + prepaySum + insSum;
		decimal totalCredit = opdGross + ipdGross;

		if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine("凭证日期,科目编码,科目名称,摘要,借方金额,贷方金额");
			foreach (dynamic row in lines)
			{
				sb.AppendLine($"{row.VoucherDate},{row.AccountCode},{row.AccountName},{row.Summary},{row.Debit:F2},{row.Credit:F2}");
			}
			return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"GL_Voucher_{targetDate}.csv");
		}

		return Ok(ApiResponse<object>.Success(new
		{
			ClosingDateInt = targetDate,
			Balanced = Math.Abs(totalDebit - totalCredit) <= 0.01m,
			TotalDebit = totalDebit,
			TotalCredit = totalCredit,
			Entries = lines
		}, $"已生成 {targetDate} 借贷平衡总账凭证（共 {lines.Count} 条分录）"));
	}
}
