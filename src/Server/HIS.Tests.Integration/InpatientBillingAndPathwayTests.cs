using System;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.ClinicalPathway.Models;
using HIS.Modules.ClinicalPathway.Services;
using HIS.Modules.Inpatient.Services;
using Xunit;

namespace HIS.Tests.Integration;

public sealed class InpatientBillingAndPathwayTests
{
    [Fact]
    public async Task DailyBilling_Resolves_Real_Tariff_Prices_And_Is_Idempotent()
    {
        var db = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        long now = SystemTime.UtcNowMs;

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_price_items
                (id, item_code, item_name, fee_category, unit_name, unit_price, reimburse_type, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES
                ('PI-1', 'FEE_NURSING_L1', '一级护理费', 'NURSING', '日', 60.00, 'A', 1, 1, @Now, @Now, 1),
                ('PI-2', 'ORD_OXYGEN', '持续低流量吸氧', 'TREAT', '小时', 12.50, 'A', 1, 1, @Now, @Now, 1)",
                new { Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO ipd_encounters
                (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id, is_borrowed_bed, bed_code, active_bed_token, nursing_level_code, prepayment_balance, status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES
                ('ENC-1', 'ZY2026001', 'PAT-1', 'C1', 'DEPT-1', 'WARD-1', 0, '01床', 'TOK-1', 'LEVEL_1', 2000, 1, 1, @Now, @Now, 1)",
                new { Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO ipd_beds
                (id, campus_id, ward_id, room_code, bed_code, bed_type, daily_fee, bed_status, current_encounter_id, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES
                ('BED-1', 'C1', 'WARD-1', 'R1', '01床', 'NORMAL', 80.00, 1, 'ENC-1', 1, @Now, @Now, 1)",
                new { Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO ord_medical_orders
                (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id, exec_target_dept_id, order_term_type, group_no, order_category, item_code, item_name, dosage, frequency_code, route_code, status, order_doctor_id, plan_start_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES
                ('ORD-1', 'O1', 'ENC-1', 'PAT-1', 'DEPT-1', 'WARD-1', 'WARD-1', 1, 1, 'TREAT', 'ORD_OXYGEN', '持续低流量吸氧', 4, 'QD', 'INH', 2, 'DOC-1', @Now, 1, @Now, @Now, 1)",
                new { Now = now });
        }

        var run1 = await InpatientDailyBillingHostedService.ExecuteDailyBillingCoreAsync(db, 20261121, "TEST", "TESTER");
        Assert.False(run1.AlreadyExecuted);
        Assert.Equal(1, run1.PatientCount);
        // 80 (bed) + 60 (L1 nursing from cfg_price_items) + 4 * 12.5 (oxygen from cfg_price_items) = 190.00
        Assert.Equal(190.00m, run1.TotalAmount);

        var run2 = await InpatientDailyBillingHostedService.ExecuteDailyBillingCoreAsync(db, 20261121, "TEST", "TESTER");
        Assert.True(run2.AlreadyExecuted);
        Assert.Equal(run1.RunId, run2.RunId);
    }

    [Fact]
    public async Task ClinicalPathway_Compares_Real_Orders_And_Detects_Missing_And_Extra_Variances()
    {
        var db = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        long now = SystemTime.UtcNowMs;

        var pwRepo = new DapperRepository<CpwPathway>(db, "cpw_pathways");
        var stRepo = new DapperRepository<CpwStage>(db, "cpw_stages");
        var itRepo = new DapperRepository<CpwStageItem>(db, "cpw_stage_items");
        var enRepo = new DapperRepository<CpwPatientEnrollment>(db, "cpw_patient_enrollments");
        var vaRepo = new DapperRepository<CpwVariance>(db, "cpw_variances");

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cpw_stage_items (id, stage_id, item_category, item_code, item_name, is_mandatory, sort_order, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES
                ('SI-1', 'ST-1', 'LAB', 'LAB_CBC', '血常规', 1, 1, 1, @Now, @Now, 1),
                ('SI-2', 'ST-1', 'EXAM', 'EXAM_ECG', '心电图', 1, 2, 1, @Now, @Now, 1)", new { Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO cpw_patient_enrollments (id, encounter_id, patient_id, pathway_id, current_stage_id, enrolled_at_ms, status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('EN-1', 'ENC-CPW', 'PAT-1', 'PW-1', 'ST-1', @Now, 'ACTIVE', 1, @Now, @Now, 1)", new { Now = now });

            // 患者已开血常规（命中路径），未开心电图（缺失），另开了一个路径外医嘱（变异）
            await conn.ExecuteAsync(@"
                INSERT INTO ord_medical_orders
                (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id, exec_target_dept_id, order_term_type, group_no, order_category, item_code, item_name, dosage, frequency_code, route_code, status, order_doctor_id, plan_start_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES
                ('O-1', 'ON1', 'ENC-CPW', 'PAT-1', 'D1', 'W1', 'W1', 2, 1, 'LAB', 'LAB_CBC', '血常规', 1, 'ST', 'IV', 1, 'DOC1', @Now, 1, @Now, @Now, 1),
                ('O-2', 'ON2', 'ENC-CPW', 'PAT-1', 'D1', 'W1', 'W1', 2, 2, 'DRUG', 'DRUG_EXTRA', '路径外特殊用药', 1, 'ST', 'PO', 1, 'DOC1', @Now, 1, @Now, @Now, 1)",
                new { Now = now });
        }

        var svc = new ClinicalPathwayService(pwRepo, stRepo, itRepo, enRepo, vaRepo, db);
        var cmp = await svc.CompareOrdersAsync("EN-1");

        Assert.Single(cmp.MissingMandatoryItems);
        Assert.Contains("心电图", cmp.MissingMandatoryItems[0]);
        Assert.Contains(cmp.PotentialVariances, v => v.Contains("路径外特殊用药"));
    }
}
