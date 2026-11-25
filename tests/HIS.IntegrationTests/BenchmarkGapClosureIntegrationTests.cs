using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.BloodBank.Controllers;
using HIS.Modules.BloodBank.Models;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Finance.Models;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Ordering.Controllers;
using HIS.Modules.Ordering.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HIS.IntegrationTests;

public class BenchmarkGapClosureIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public BenchmarkGapClosureIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SetAdminAuth(_factory, _client);
    }

    private static void SetAdminAuth(WebApplicationFactory<Program> factory, HttpClient client)
    {
        using var scope = factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var sessionVersion = conn.ExecuteScalar<int>(
            "SELECT session_version FROM sys_users WHERE id = 'USER-ADMIN'");

        var jwt = scope.ServiceProvider.GetRequiredService<BuildingBlocks.Security.JwtTokenService>();
        var token = jwt.GenerateAccessToken(
            userId: "USER-ADMIN",
            userName: "admin",
            displayName: "系统管理员",
            campusId: "CAMPUS-01",
            activeDeptCategory: DepartmentCategory.ADMIN,
            activeDeptId: "DEPT-ADMIN-01",
            activeWardId: "DEPT-WARD-02",
            linkedWardIds: null,
            roles: new[] { "ROLE-ADMIN", "ADMIN", "ROLE-IPD-RESIDENT", "ROLE-WARD-NURSE", "IPD_RESIDENT", "WARD_NURSE", "ROLE-CASHIER", "CASHIER" },
            dataScope: DataScope.ALL_CAMPUS,
            sessionVersion: sessionVersion,
            permHash: "ALL",
            prescribeLevel: 3,
            surgeryLevel: 4,
            emrSignLevel: 3,
            activePostId: "UDP-ADMIN-ADMIN",
            roleId: "ROLE-ADMIN",
            roleCode: "ADMIN",
            roleName: "超级管理员",
            activeDeptName: "全院管理部");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task EnsureTestEncounterAsync(string encounterId, string patientId, string deptId = "DEPT-IPD-01", string wardId = "DEPT-WARD-02")
    {
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            DELETE FROM ord_execution_plans WHERE encounter_id = @EncounterId;
            DELETE FROM ord_medical_orders WHERE encounter_id = @EncounterId;
            INSERT OR REPLACE INTO pat_patients (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_cipher, id_card_hash, phone_masked, allergy_tags_json, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@PatientId, 'MRN-' || substr(@PatientId, 5), '测试患者', 'CSHZ', '1', 19900101, 'ID_CARD', NULL, 'HASH-' || @PatientId, '138****0000', '[]', 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO ipd_encounters (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id, is_borrowed_bed, bed_code, active_bed_token, attending_doctor_id, primary_nurse_id, nursing_level_code, prepayment_balance, status, triage_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@EncounterId, 'ADM-' || substr(@EncounterId, 5), @PatientId, 'CAMPUS-01', @DeptId, @WardId, 0, '01', 'TOKEN-' || @EncounterId, 'USER-ADMIN', 'USER-ADMIN', 'LEVEL_1', 5000.00, 1, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR IGNORE INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES ('DRUG-TEST-G1', 'DRUG-TEST-G1', '0.9%氯化钠注射液', '250ml', '瓶', 12.00, 8.00, 0, 0, 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);
            INSERT OR IGNORE INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES ('DRUG-TEST-G2', 'DRUG-TEST-G2', '维生素C注射液', '2ml:0.5g', '支', 5.00, 3.00, 0, 0, 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);
        ", new { EncounterId = encounterId, PatientId = patientId, DeptId = deptId, WardId = wardId, Now = now });
    }

    private async Task EnsureTestVisitAsync(string visitId, string patientId, string doctorId = "USER-ADMIN", string deptId = "DEPT-OPD-01")
    {
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            DELETE FROM opd_account_ledgers WHERE patient_id = @PatientId;
            DELETE FROM opd_patient_accounts WHERE patient_id = @PatientId;
            DELETE FROM opd_prescription_items WHERE prescription_id IN (SELECT id FROM opd_prescriptions WHERE visit_id = @VisitId);
            DELETE FROM opd_prescriptions WHERE visit_id = @VisitId;
            INSERT OR REPLACE INTO pat_patients (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_cipher, id_card_hash, phone_masked, allergy_tags_json, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@PatientId, 'MRN-' || substr(@PatientId, 5), '门诊测试患者', 'MZCS', '1', 19900101, 'ID_CARD', NULL, 'HASH-' || @PatientId, '138****0000', '[]', 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO opd_visits (id, visit_no, campus_id, opd_dept_id, clinic_room_id, patient_id, schedule_id, doctor_id, is_emergency, is_green_channel, is_return_visit, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@VisitId, 'VNO-' || substr(@VisitId, 5), 'CAMPUS-01', @DeptId, 'ROOM-OPD-101', @PatientId, NULL, @DoctorId, 0, 0, 0, 2, 1, @Now, @DoctorId, @Now, @DoctorId, 1);
        ", new { VisitId = visitId, PatientId = patientId, DeptId = deptId, DoctorId = doctorId, Now = now });
    }

    [Fact]
    public async Task GroupOrders_ParentChild_CascadeVerifyAndStop_WorksCorrectly()
    {
        string encId = "ENC-GAP-GRP-01";
        string patId = "PAT-GAP-GRP-01";
        await EnsureTestEncounterAsync(encId, patId);

        // 1. Create parent order in Group 999
        var reqParent = new OrderingController.CreateOrderRequestDto(
            EncounterId: encId,
            PatientId: patId,
            OrderIpdDeptId: "DEPT-IPD-01",
            PatientWardId: "DEPT-WARD-02",
            ExecTargetDeptId: "DEPT-WARD-02",
            OrderTermType: 1,
            GroupNo: 999,
            OrderCategory: "DRUG",
            ItemCode: "DRUG-TEST-G1",
            ItemName: "0.9%氯化钠注射液",
            Dosage: 250m,
            FrequencyCode: "QD",
            RouteCode: "IV_DRIP",
            OrderDoctorId: "USER-ADMIN",
            OrderSubType: "NORMAL"
        );
        var res1 = await _client.PostAsJsonAsync("/api/v4/orders", reqParent);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var order1 = (await res1.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;

        // 2. Create child order in Group 999
        var reqChild = new OrderingController.CreateOrderRequestDto(
            EncounterId: encId,
            PatientId: patId,
            OrderIpdDeptId: "DEPT-IPD-01",
            PatientWardId: "DEPT-WARD-02",
            ExecTargetDeptId: "DEPT-WARD-02",
            OrderTermType: 1,
            GroupNo: 999,
            OrderCategory: "DRUG",
            ItemCode: "DRUG-TEST-G2",
            ItemName: "维生素C注射液",
            Dosage: 2m,
            FrequencyCode: "QD",
            RouteCode: "IV_DRIP",
            OrderDoctorId: "USER-ADMIN",
            OrderSubType: "NORMAL",
            ParentOrderId: order1.Id
        );
        var res2 = await _client.PostAsJsonAsync("/api/v4/orders", reqChild);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var order2 = (await res2.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;

        // 3. Nurse verifies parent order -> automatically verifies sibling orders in group 999
        var resVerify = await _client.PostAsJsonAsync($"/api/v4/orders/{order1.Id}/verify", new OrderingController.VerifyOrderRequestDto("USER-ADMIN", "DEPT-WARD-02"));
        Assert.Equal(HttpStatusCode.OK, resVerify.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var verifiedCount = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM ord_medical_orders WHERE encounter_id = @EncId AND group_no = 999 AND status = 1", new { EncId = encId });
            Assert.Equal(2, verifiedCount); // Both parent and child orders are now verified (status = 1)

            var planCount = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM ord_execution_plans WHERE encounter_id = @EncId AND exec_status = 0", new { EncId = encId });
            Assert.True(planCount >= 2);
        }

        // 4. Doctor stops parent order -> cascading stops child order in group 999 and cancels pending plans
        var resStop = await _client.PostAsync($"/api/v4/orders/{order1.Id}/stop?doctorId=USER-ADMIN", null);
        Assert.Equal(HttpStatusCode.OK, resStop.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var stoppedCount = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM ord_medical_orders WHERE encounter_id = @EncId AND group_no = 999 AND status = 3", new { EncId = encId });
            Assert.Equal(2, stoppedCount); // Both orders in group 999 cascade stopped!

            var activePlanCount = await conn.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM ord_execution_plans WHERE order_id IN (@Id1, @Id2) AND exec_status = 0",
                new { Id1 = order1.Id, Id2 = order2.Id });
            Assert.Equal(0, activePlanCount); // All pending execution plans were canceled (exec_status = 2)
        }
    }

    [Fact]
    public async Task DischargeMedOrder_DoesNotGenerate_BedsideExecutionPlans()
    {
        string encId = "ENC-GAP-DISC-01";
        string patId = "PAT-GAP-DISC-01";
        await EnsureTestEncounterAsync(encId, patId);

        // Create a temporary discharge medicine order (出院带药)
        var reqDischarge = new OrderingController.CreateOrderRequestDto(
            EncounterId: encId,
            PatientId: patId,
            OrderIpdDeptId: "DEPT-IPD-01",
            PatientWardId: "DEPT-WARD-02",
            ExecTargetDeptId: "DEPT-WARD-02",
            OrderTermType: 2, // 临时医嘱
            GroupNo: 888,
            OrderCategory: "DRUG",
            ItemCode: "DRUG-TEST-G2",
            ItemName: "维生素C注射液",
            Dosage: 5m,
            FrequencyCode: "ST",
            RouteCode: "PO",
            OrderDoctorId: "USER-ADMIN",
            OrderSubType: "DISCHARGE_MED"
        );
        var res = await _client.PostAsJsonAsync("/api/v4/orders", reqDischarge);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var order = (await res.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;
        Assert.Equal("DISCHARGE_MED", order.OrderSubType);

        // Verify order
        var resVerify = await _client.PostAsJsonAsync($"/api/v4/orders/{order.Id}/verify", new OrderingController.VerifyOrderRequestDto("USER-ADMIN", "DEPT-WARD-02"));
        Assert.Equal(HttpStatusCode.OK, resVerify.StatusCode);

        // Check that NO bedside execution plans were generated for discharge medicine
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var plans = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM ord_execution_plans WHERE order_id = @OrderId", new { OrderId = order.Id });
        Assert.Equal(0, plans);
    }

    [Fact]
    public async Task Outpatient_PrepaymentAccount_Recharge_And_InClinicSettlement_Works()
    {
        string patId = "PAT-OPD-ACCT-01";
        string rxId = "RX-OPD-SETTLE-01";
        string visitId = "VISIT-OPD-TEST-01";

        await EnsureTestVisitAsync(visitId, patId);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await conn.ExecuteAsync(@"
                INSERT OR REPLACE INTO opd_prescriptions (id, prescription_no, visit_id, patient_id, opd_dept_id, target_pharmacy_id, rx_type, total_amount, doctor_id, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@RxId, 'RX-NO-01', @VisitId, @PatientId, 'DEPT-OPD-01', 'DEPT-PHM-01', 'WESTERN', 50.00, 'USER-ADMIN', 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

                INSERT OR REPLACE INTO opd_prescription_items (id, prescription_id, group_no, drug_id, drug_name_snapshot, dose_once, dose_unit, frequency_code, route_code, days, total_quantity, unit_price, skin_test_flag, is_deleted, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('RXI-01', @RxId, 1, 'DRUG-01', '阿莫西林胶囊', 500, 'mg', 'TID', 'PO', 3, 2, 25.00, 0, 0, 1, @Now, @Now, 1);
            ", new { RxId = rxId, VisitId = visitId, PatientId = patId, Now = now });
        }

        // 1. Get initial balance (auto creates account)
        var resBal = await _client.GetFromJsonAsync<ApiResponse<OpdPatientAccount>>($"/api/v4/opd/accounts/{patId}/balance");
        Assert.NotNull(resBal?.Data);

        // 2. Recharge account
        var rechargeReq = new FinanceController.OpdRechargeRequestDto(
            PatientId: patId,
            Amount: 500.00m,
            PaymentChannel: "WECHAT",
            OperatorId: "USER-ADMIN"
        );
        var resRecharge = await _client.PostAsJsonAsync("/api/v4/opd/accounts/recharge", rechargeReq);
        Assert.Equal(HttpStatusCode.OK, resRecharge.StatusCode);
        var acc = (await resRecharge.Content.ReadFromJsonAsync<ApiResponse<OpdPatientAccount>>())!.Data!;
        Assert.True(acc.Balance >= 500.00m);

        // 3. Settle in-clinic (direct card deduction)
        var settleReq = new FinanceController.OpdInClinicSettleRequestDto(
            PatientId: patId,
            PrescriptionId: rxId,
            DoctorId: "USER-ADMIN"
        );
        var resSettle = await _client.PostAsJsonAsync("/api/v4/opd/accounts/settle-in-clinic", settleReq);
        Assert.Equal(HttpStatusCode.OK, resSettle.StatusCode);
        var payOrder = (await resSettle.Content.ReadFromJsonAsync<ApiResponse<FinPaymentOrder>>())!.Data!;
        Assert.Equal("PREPAYMENT_ACCOUNT", payOrder.PaymentChannel);
        Assert.Equal(50.00m, payOrder.TotalAmount);

        // 4. Verify prescription is marked paid in DB (status == 3)
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var status = await conn.ExecuteScalarAsync<int>("SELECT status FROM opd_prescriptions WHERE id = @Id", new { Id = rxId });
            Assert.Equal(3, status);
        }
    }

    [Fact]
    public async Task Transfusion_15MinuteCheck_And_BloodBagRecovery_Works()
    {
        string encId = "ENC-GAP-01";
        string patId = "PAT-GAP-01";
        string execId = "EXEC-BT-01";
        string bagBarcode = "BAG-TEST-8888";

        await EnsureTestEncounterAsync(encId, patId);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await conn.ExecuteAsync(@"
                INSERT OR REPLACE INTO bld_inventory_bags (id, bag_barcode, donation_code, source_blood_center, blood_code, abo_type, rh_type, volume_ml, collection_date_int, expiry_date_int, status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('BAG-ID-8888', @Barcode, 'DON-8888', '北京市红十字血液中心', 'RBC', 'A', 'POSITIVE', 200, 20261101, 20261231, 'TRANSFUSING', 1, @Now, @Now, 1);

                INSERT OR REPLACE INTO mtc_blood_requests (id, encounter_id, patient_id, abo_type, rh_type, blood_product_code, volume_ml, empty_bag_returned, status, approval_status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('REQ-BT-01', @EncounterId, @PatientId, 'A', 'POSITIVE', 'RBC', 200, 0, 3, 'APPROVED', 1, @Now, @Now, 1);

                INSERT OR REPLACE INTO bld_transfusion_executions (id, blood_request_id, bag_id, bag_barcode, encounter_id, patient_id, patient_wristband_code, start_nurse_id, dual_check_nurse_id, pre_vitals_json, started_at_ms, status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@ExecId, 'REQ-BT-01', 'BAG-ID-8888', @Barcode, @EncounterId, @PatientId, @PatientId, 'USER-ADMIN', 'USER-ADMIN', '{""temperature"":36.5}', @Now, 'TRANSFUSING', 1, @Now, @Now, 1);
            ", new { EncounterId = encId, PatientId = patId, ExecId = execId, Barcode = bagBarcode, Now = now });
        }

        // 1. Record 15-minute slow drip check
        var checkDto = new BloodBankController.FifteenMinuteCheckDto(
            Temperature: 36.6m,
            Pulse: 76,
            Respiration: 18,
            BloodPressure: "118/76",
            DripRatePerMin: 16, // compliant (<= 20)
            AdverseReactionObserved: false,
            Notes: "患者生命体征平稳，无皮疹发热或寒战"
        );
        var res15 = await _client.PostAsJsonAsync($"/api/v4/blood-bank/executions/{execId}/fifteen-minute-check", checkDto);
        Assert.Equal(HttpStatusCode.OK, res15.StatusCode);

        // 2. Record empty blood bag recovery (24h retention)
        var recoveryDto = new BloodBankController.BagRecoveryDto(
            BagBarcode: bagBarcode,
            ReturnNurseId: "USER-ADMIN",
            RecoveryReceiverId: "USER-ADMIN",
            StorageLocation: "专用2-6℃留样冰箱-01",
            RetentionHours: 24,
            Remark: "血袋无渗漏破损，已密封入库留样24小时"
        );
        var resRec = await _client.PostAsJsonAsync($"/api/v4/blood-bank/executions/{execId}/bag-recovery", recoveryDto);
        Assert.Equal(HttpStatusCode.OK, resRec.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var bagStatus = await conn.ExecuteScalarAsync<string>("SELECT status FROM bld_inventory_bags WHERE bag_barcode = @Barcode", new { Barcode = bagBarcode });
            Assert.Equal("RECOVERED", bagStatus);

            var reqStatus = await conn.ExecuteScalarAsync<int>("SELECT status FROM mtc_blood_requests WHERE id = 'REQ-BT-01'");
            Assert.Equal(4, reqStatus); // Status 4: 已回收血袋留样
        }
    }

    [Fact]
    public async Task InterWardTransfer_OrdersReorganization_And_DailyCostSheet_Works()
    {
        string encId = "ENC-GAP-02";
        string patId = "PAT-GAP-02";

        await EnsureTestEncounterAsync(encId, patId);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await conn.ExecuteAsync(@"
                INSERT OR REPLACE INTO ord_medical_orders (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id, exec_target_dept_id, order_term_type, group_no, order_category, item_code, item_name, dosage, frequency_code, route_code, plan_start_at_ms, status, order_doctor_id, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('ORD-REORG-01', 'ORD-R-01', @EncounterId, @PatientId, 'DEPT-IPD-01', 'DEPT-WARD-02', 'DEPT-WARD-02', 1, 1, 'DRUG', 'DRUG-01', '阿莫西林胶囊', 500, 'TID', 'PO', @Now, 1, 'USER-ADMIN', 1, @Now, @Now, 1);

                INSERT OR REPLACE INTO fin_inpatient_fee_items (id, encounter_id, patient_id, fee_date_int, item_category, item_code, item_name, unit_name, unit_price, quantity, total_amount, self_pay_ratio, reimburse_type, charge_dept_id, exec_dept_id, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('FEE-ITEM-01', @EncounterId, @PatientId, 20261124, 'MEDICINE', 'DRUG-01', '阿莫西林胶囊', '盒', 25.00, 2, 50.00, 0, 'CLASS_A', 'DEPT-IPD-01', 'DEPT-IPD-01', 1, @Now, @Now, 1);
            ", new { EncounterId = encId, PatientId = patId, Now = now });
        }

        // 1. Transfer reorganize: batch stop standing orders
        var reorgReq = new InpatientController.TransferReorganizeRequestDto(
            EncounterId: encId,
            DoctorId: "USER-ADMIN",
            ReorganizeReason: "转出普外科，进入心内科病区重整医嘱"
        );
        var resReorg = await _client.PostAsJsonAsync($"/api/v4/ipd/encounters/{encId}/transfer-reorganize", reorgReq);
        Assert.Equal(HttpStatusCode.OK, resReorg.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var status = await conn.ExecuteScalarAsync<int>("SELECT status FROM ord_medical_orders WHERE id = 'ORD-REORG-01'");
            Assert.Equal(3, status); // Status 3: STOPPED on transfer reorganization
        }

        // 2. Query inpatient daily cost sheet
        var resSheet = await _client.GetFromJsonAsync<ApiResponse<FinanceController.InpatientDailyCostSheetDto>>(
            $"/api/v4/finance/inpatient-fee-items/{encId}/daily-cost-sheet?feeDateInt=20261124");
        Assert.NotNull(resSheet?.Data);
        Assert.Equal(50.00m, resSheet.Data.TodayTotalAmount);
        Assert.Equal("NORMAL", resSheet.Data.ArrearsStatus);
        Assert.Single(resSheet.Data.Categories);
        Assert.Equal("西药费", resSheet.Data.Categories[0].CategoryName);
    }
}
