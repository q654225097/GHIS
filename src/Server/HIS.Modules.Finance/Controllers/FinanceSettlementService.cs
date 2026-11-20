using System.Data.Common;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Finance.Models;

namespace HIS.Modules.Finance.Controllers;

internal static class FinanceSettlementService
{
    internal static string NormalizeScene(string? scene)
        => string.IsNullOrWhiteSpace(scene) ? "OPD" : scene.Trim().ToUpperInvariant();

    internal static async Task<(decimal Amount, string? Error)> LoadOpdPrescriptionTotalAsync(
        DbConnection conn,
        string visitId,
        string patientId,
        DbTransaction? transaction,
        CancellationToken ct,
        string? expectedPaymentId = null)
    {
        var rows = (await conn.QueryAsync<ReviewedPrescriptionRow>(new CommandDefinition(
            """
            SELECT id, prescription_no, visit_id, patient_id, target_pharmacy_id,
                   total_amount, status, payment_order_id, reviewer_pharmacist_id
            FROM opd_prescriptions
            WHERE visit_id = @VisitId AND status = 1
            ORDER BY created_at_ms
            """,
            new { VisitId = visitId }, transaction, cancellationToken: ct))).ToList();

        var medTechRows = (await conn.QueryAsync<decimal>(new CommandDefinition(
            """
            SELECT fee_amount FROM mtc_service_requests
            WHERE source_visit_id = @VisitId AND source_type = 'OPD' AND is_fee_confirmed = 0 AND status = 0
            """,
            new { VisitId = visitId }, transaction, cancellationToken: ct))).ToList();

        if (rows.Count == 0 && medTechRows.Count == 0)
            return (0m, "没有已通过药师前置审方的处方或待缴费医技项目，禁止收费。请先完成审方");

        decimal total = 0;
        foreach (var rx in rows)
        {
            if (!string.Equals(rx.PatientId, patientId, StringComparison.OrdinalIgnoreCase))
                return (0m, $"处方 {rx.PrescriptionNo} 的患者与结算患者不一致");
            if (!string.IsNullOrWhiteSpace(rx.PaymentOrderId) &&
                !string.Equals(rx.PaymentOrderId, expectedPaymentId, StringComparison.OrdinalIgnoreCase))
                return (0m, $"处方 {rx.PrescriptionNo} 已被其他收费单占用");
            if (string.IsNullOrWhiteSpace(rx.ReviewerPharmacistId))
                return (0m, $"处方 {rx.PrescriptionNo} 缺少药师审方签名");
            if (string.IsNullOrWhiteSpace(rx.TargetPharmacyId))
                return (0m, $"处方 {rx.PrescriptionNo} 缺少目标药房");
            var itemRows = (await conn.QueryAsync<PrescriptionItemAmountRow>(new CommandDefinition(
                """
                SELECT unit_price, total_quantity, drug_id, drug_name_snapshot
                FROM opd_prescription_items
                WHERE prescription_id = @RxId AND is_deleted = 0
                """,
                new { RxId = rx.Id }, transaction, cancellationToken: ct))).ToList();
            if (itemRows.Count == 0)
                return (0m, $"处方 {rx.PrescriptionNo} 没有有效药品明细");
            var itemTotal = itemRows.Sum(x => x.UnitPrice * x.TotalQuantity);
            if (Math.Abs(itemTotal - rx.TotalAmount) > 0.01m)
                return (0m, $"处方 {rx.PrescriptionNo} 明细金额与处方总额不一致，禁止收费");
            total += itemTotal;
        }
        total += medTechRows.Sum();
        return (total, null);
    }

    internal static async Task<string?> RejectIfUnsafeOpdRefundAsync(
        DbConnection conn,
        FinPaymentOrder pay,
        DbTransaction? transaction,
        CancellationToken ct)
    {
        var unsafeOrders = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*) FROM phm_dispense_orders
            WHERE prescription_id IN (
                SELECT id FROM opd_prescriptions WHERE visit_id = @VisitId)
              AND status IN (1, 3) AND drug_return_confirmed = 0
            """,
            new { VisitId = pay.VisitOrEncounterId }, transaction, cancellationToken: ct));
        if (unsafeOrders > 0)
            return "处方药品已发出但尚未在药房完成实物扫码验收入库，不能办理财务退费";

        var dispensed = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*) FROM opd_prescriptions
            WHERE visit_id = @VisitId AND status = 4
              AND NOT EXISTS (
                  SELECT 1 FROM phm_dispense_orders d
                  WHERE d.prescription_id = opd_prescriptions.id
                    AND d.status = 2 AND d.drug_return_confirmed = 1)
            """,
            new { VisitId = pay.VisitOrEncounterId }, transaction, cancellationToken: ct));
        return dispensed > 0 ? "已发药处方尚未在药房完成实物扫码验收入库，不能办理财务退费" : null;
    }

    internal static async Task<(List<ReviewedPrescriptionRow> Rows, string? Error)> ClaimReviewedPrescriptionsAsync(
        DbConnection conn,
        string visitId,
        DbTransaction? transaction,
        long now,
        string operatorId,
        string paymentId,
        CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<ReviewedPrescriptionRow>(new CommandDefinition(
            """
            SELECT id, prescription_no, visit_id, patient_id, target_pharmacy_id,
                   total_amount, status, payment_order_id, reviewer_pharmacist_id, reviewer_pharmacist_id
            FROM opd_prescriptions
            WHERE visit_id = @VisitId AND status = 1
            ORDER BY created_at_ms
            """,
            new { VisitId = visitId }, transaction, cancellationToken: ct))).ToList();
        foreach (var rx in rows)
        {
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE opd_prescriptions
                SET status = 3, payment_order_id = @PaymentId,
                    updated_at_ms = @Now, updated_by = @OperatorId,
                    version_no = version_no + 1
                WHERE id = @Id AND status = 1
                  AND (payment_order_id IS NULL OR payment_order_id = @PaymentId)
                  AND reviewer_pharmacist_id IS NOT NULL
                """,
                new { Id = rx.Id, Now = now, OperatorId = operatorId, PaymentId = paymentId },
                transaction, cancellationToken: ct));
            if (affected != 1)
                return (rows, $"处方 {rx.PrescriptionNo} 状态已变更，收费已取消");
        }
        return (rows, null);
    }

    internal static async Task<decimal> LoadPaymentRefundedAmountAsync(
        DbConnection conn,
        string paymentId,
        DbTransaction? transaction,
        CancellationToken ct)
    {
        return await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(refund_amount), 0)
            FROM fin_refund_orders
            WHERE original_payment_id = @PaymentId AND status = 1
            """,
            new { PaymentId = paymentId }, transaction, cancellationToken: ct));
    }

    internal static string NewDocumentNo(string prefix, long timestampMs)
        => prefix + timestampMs.ToString("yyyyMMddHHmmssfff") + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
}

internal sealed class ReviewedPrescriptionRow
{
    public string Id { get; set; } = string.Empty;
    public string PrescriptionNo { get; set; } = string.Empty;
    public string VisitId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string TargetPharmacyId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int Status { get; set; }
    public string? PaymentOrderId { get; set; }
    public string? ReviewerPharmacistId { get; set; }
}

internal sealed class PrescriptionItemAmountRow
{
    public string DrugId { get; set; } = string.Empty;
    public string DrugNameSnapshot { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int TotalQuantity { get; set; }
}

internal sealed class OpdVisitSettlementRow
{
    public string Id { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string OpdDeptId { get; set; } = string.Empty;
    public int Status { get; set; }
}
