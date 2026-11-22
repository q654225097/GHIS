using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Services;

public class DischargeChecklistService
{
	private readonly IDbConnectionFactory _connFactory;

	public DischargeChecklistService(IDbConnectionFactory connFactory)
	{
		_connFactory = connFactory;
	}

	public async Task<DischargeCheckResult> PerformSixItemCheckAsync(string encounterId, CancellationToken ct = default(CancellationToken))
	{
		List<string> failures = new List<string>();
		int unstoppedOrders = await QueryCountAsync("SELECT count(*) FROM ord_medical_orders\nWHERE encounter_id = @EncounterId\n  AND status IN (0, 1)", new
		{
			EncounterId = encounterId
		}, ct);
		int pendingPlans = await QueryCountAsync("SELECT count(*) FROM ord_execution_plans WHERE encounter_id = @EncounterId AND exec_status = 0", new
		{
			EncounterId = encounterId
		}, ct);
		if (unstoppedOrders > 0 || pendingPlans > 0)
		{
			failures.Add($"存在 {unstoppedOrders} 条未停止的长期医嘱或未执行的临时医嘱（待执行计划 {pendingPlans} 项）");
		}
		int pendingDispense = await QueryCountAsync("SELECT count(*) FROM phm_dispense_orders\nWHERE (prescription_id IN (SELECT id FROM opd_prescriptions WHERE visit_id = @EncounterId)\n    OR ward_summary_id = @EncounterId)\n  AND status = 0", new
		{
			EncounterId = encounterId
		}, ct);
		if (pendingDispense > 0)
		{
			failures.Add($"药房存在 {pendingDispense} 张待发药或待确认退药单");
		}
		int unclosedMedTech = await QueryCountAsync("SELECT count(*) FROM mtc_service_requests\nWHERE (source_visit_id = @EncounterId\n    OR source_order_id = @EncounterId\n    OR source_order_id IN (SELECT id FROM ord_medical_orders WHERE encounter_id = @EncounterId))\n  AND status < 3", new
		{
			EncounterId = encounterId
		}, ct);
		if (unclosedMedTech > 0)
		{
			failures.Add($"医技科室存在 {unclosedMedTech} 项未确费、未出报告或未撤销检查/检验单");
		}
		int unacknowledgedCriticalValues = await QueryCountAsync(@"
SELECT (
    SELECT count(*) FROM mtc_service_requests
    WHERE (source_visit_id = @EncounterId
        OR source_order_id = @EncounterId
        OR source_order_id IN (SELECT id FROM ord_medical_orders WHERE encounter_id = @EncounterId))
      AND is_critical_value = 1 AND status <> 4
      AND (critical_ack_doc_id IS NULL OR trim(critical_ack_doc_id) = ''
           OR critical_ack_nurse_id IS NULL OR trim(critical_ack_nurse_id) = ''
           OR critical_ack_doc_id = critical_ack_nurse_id)
) + (
    SELECT count(*) FROM medtech_critical_values
    WHERE (visit_or_encounter_id = @EncounterId
        OR patient_id IN (SELECT patient_id FROM ipd_encounters WHERE id = @EncounterId))
      AND status <> 3
      AND (doc_acked_by IS NULL OR trim(doc_acked_by) = ''
           OR nurse_acked_by IS NULL OR trim(nurse_acked_by) = ''
           OR doc_acked_by = nurse_acked_by)
)", new
		{
			EncounterId = encounterId
		}, ct);
		if (unacknowledgedCriticalValues > 0)
		{
			failures.Add($"存在 {unacknowledgedCriticalValues} 份危急值报告尚未完成医生与护士双签收闭环");
		}
		int unreturnedDrugs = await QueryCountAsync("SELECT count(*) FROM phm_dispense_orders\nWHERE (ward_summary_id = @EncounterId\n    OR prescription_id IN (\n        SELECT id FROM opd_prescriptions WHERE visit_id = @EncounterId))\n  AND (status = 0 OR (status = 2 AND drug_return_confirmed <> 1))", new
		{
			EncounterId = encounterId
		}, ct);
		if (unreturnedDrugs > 0)
		{
			failures.Add($"药品房存在 {unreturnedDrugs} 张待发药或已发药未确认退药的单据，不能关闭住院账目");
		}
		int unclosedSurgeries = await QueryCountAsync(@"
SELECT (
    SELECT count(*) FROM mtc_surgery_schedules
    WHERE encounter_id = @EncounterId AND status < 2
) + (
    SELECT count(*) FROM mte_surgery_schedules
    WHERE encounter_id = @EncounterId AND (status < 2 OR who_sign_out_ok = 0 OR instrument_count_balanced = 0)
)", new
		{
			EncounterId = encounterId
		}, ct);
		if (unclosedSurgeries > 0)
		{
			failures.Add("手术麻醉记账或安全核查尚未完成");
		}
		int unreturnedBloodBags = await QueryCountAsync("SELECT count(*) FROM mtc_blood_requests WHERE encounter_id = @EncounterId AND status IN (1, 2, 3) AND empty_bag_returned = 0", new
		{
			EncounterId = encounterId
		}, ct);
		int pendingConsults = await QueryCountAsync("SELECT count(*) FROM ipd_consultations WHERE encounter_id = @EncounterId AND status IN ('REQUESTED', 'ACCEPTED')", new { EncounterId = encounterId }, ct);
		if (pendingConsults > 0)
		{
			failures.Add($"存在 {pendingConsults} 份全院会诊单尚未完成会诊意见与病历回写");
		}
		int pendingDrugReturns = await QueryCountAsync("SELECT count(*) FROM ipd_drug_returns WHERE encounter_id = @EncounterId AND status = 'APPLIED'", new { EncounterId = encounterId }, ct);
		if (pendingDrugReturns > 0)
		{
			failures.Add($"存在 {pendingDrugReturns} 笔病区退药申请尚未经药房审批冲正");
		}
		int pendingDischargeMeds = await QueryCountAsync("SELECT count(*) FROM ipd_discharge_medications WHERE encounter_id = @EncounterId AND status = 'PRESCRIBED'", new { EncounterId = encounterId }, ct);
		if (pendingDischargeMeds > 0)
		{
			failures.Add($"存在 {pendingDischargeMeds} 张出院带药处方尚未完成药房发药核对");
		}
		if (unreturnedBloodBags > 0)
		{
			failures.Add($"输血科存在 {unreturnedBloodBags} 袋已配发血液尚未完成输血完毕空血袋回收核销");
		}
		if (await QueryCountAsync("SELECT count(*) FROM emr_documents WHERE visit_or_encounter_id = @EncounterId AND doc_type_code LIKE '%ADMISSION%' AND latest_ofd_archive_id IS NOT NULL", new
		{
			EncounterId = encounterId
		}, ct) == 0)
		{
			failures.Add("缺少已签署并生成国标 OFD 的《入院记录》");
		}
		if (await QueryCountAsync("SELECT count(*) FROM emr_documents WHERE visit_or_encounter_id = @EncounterId AND doc_type_code LIKE '%DISCHARGE%' AND latest_ofd_archive_id IS NOT NULL", new
		{
			EncounterId = encounterId
		}, ct) == 0)
		{
			failures.Add("缺少已签署并生成国标 OFD 的《出院记录/小结》");
		}
		return new DischargeCheckResult(failures.Count == 0, failures);
	}

	private async Task<int> QueryCountAsync(string sql, object param, CancellationToken ct)
	{
		using DbConnection conn = _connFactory.CreateConnection();
		using DbCommand cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		PropertyInfo[] properties = param.GetType().GetProperties();
		foreach (PropertyInfo prop in properties)
		{
			DbParameter p = cmd.CreateParameter();
			p.ParameterName = "@" + prop.Name;
			p.Value = prop.GetValue(param) ?? DBNull.Value;
			cmd.Parameters.Add(p);
		}
		return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
	}
}
