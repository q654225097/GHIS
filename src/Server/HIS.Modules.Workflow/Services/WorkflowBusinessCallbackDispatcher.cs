using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;

namespace HIS.Modules.Workflow.Services;

public class WorkflowBusinessCallbackDispatcher : IWorkflowBusinessCallback
{
    private readonly IDbConnectionFactory _connFactory;

    public WorkflowBusinessCallbackDispatcher(IDbConnectionFactory connFactory)
    {
        _connFactory = connFactory;
    }

    public async Task OnWorkflowCompletedAsync(
        string boType,
        string businessKey,
        string finalStatus,
        string? approverUserId,
        long approvedAtMs,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        string table = boType.ToUpperInvariant() switch
        {
            "CSM_REQUISITION" => "csm_requisitions",
            "INV_TRANSFER" => "inv_transfer_orders",
            "INV_STOCK_TAKE" => "inv_stock_takes",
            "BLOOD_REQUEST" => "mtc_blood_requests",
            "SURGERY_SCHEDULE" => "mte_surgery_schedules",
            "EMR_ACCESS_GRANT" => "sec_emr_access_grants",
            "ORD_SPECIAL_DRUG" => "ord_medical_orders",
            "IPD_DISCHARGE_REQUEST" => "ipd_discharge_requests",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(table))
        {
            return;
        }

        // Execute in transaction
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        using var tx = conn.BeginTransaction();
        try
        {
            string sql = $@"
                UPDATE {table} 
                SET approval_status = @Status, 
                    approved_at_ms = @ApprovedAt, 
                    approved_by = @ApprovedBy, 
                    updated_at_ms = @ApprovedAt, 
                    version_no = version_no + 1
                WHERE id = @Id";

            await conn.ExecuteAsync(sql, new
            {
                Status = finalStatus,
                ApprovedAt = approvedAtMs,
                ApprovedBy = approverUserId,
                Id = businessKey
            }, tx);

            // Special side-effects for specific BO types
            if (boType.Equals("EMR_ACCESS_GRANT", StringComparison.OrdinalIgnoreCase))
            {
                int accessStatus = finalStatus == "APPROVED" ? 1 : 2;
                await conn.ExecuteAsync(
                    "UPDATE sec_emr_access_grants SET status = @AccessStatus WHERE id = @Id",
                    new { AccessStatus = accessStatus, Id = businessKey }, tx);
            }
            else if (boType.Equals("CSM_REQUISITION", StringComparison.OrdinalIgnoreCase) && finalStatus != "APPROVED")
            {
                // Keep status = 0 (editable) so requester can revise and resubmit instead of permanent cancellation deadlock
                await conn.ExecuteAsync(
                    "UPDATE csm_requisitions SET status = 0 WHERE id = @Id AND status <> 2",
                    new { Id = businessKey }, tx);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
