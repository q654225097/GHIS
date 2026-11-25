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
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Finance.Models;
using HIS.Modules.Inpatient.Services;
using HIS.Modules.Inventory.Controllers;
using HIS.Modules.Inventory.Models;
using HIS.Modules.MedTech.Controllers;
using HIS.Modules.Ordering.Controllers;
using HIS.Modules.Ordering.Models;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Pharmacy.Controllers;
using HIS.Modules.Pharmacy.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HIS.IntegrationTests;

public class GapClosure2026IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public GapClosure2026IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SetFullClinicalAuth(_factory, _client);
    }

    private static void SetFullClinicalAuth(WebApplicationFactory<Program> factory, HttpClient client)
    {
        using var scope = factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var sessionVersion = conn.ExecuteScalar<int>(
            "SELECT session_version FROM sys_users WHERE id = 'USER-ADMIN'");

        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var token = jwt.GenerateAccessToken(
            userId: "USER-ADMIN",
            userName: "admin",
            displayName: "系统管理员",
            campusId: "CAMPUS-01",
            activeDeptCategory: DepartmentCategory.ADMIN,
            activeDeptId: "DEPT-ADMIN-01",
            activeWardId: "DEPT-WARD-02",
            linkedWardIds: new[] { "DEPT-WARD-01", "DEPT-WARD-02" },
            roles: new[]
            {
                "ROLE-ADMIN", "ADMIN",
                "ROLE-IPD-RESIDENT", "IPD_RESIDENT", "ROLE-IPD-ATTENDING", "IPD_ATTENDING",
                "ROLE-WARD-NURSE", "WARD_NURSE",
                "ROLE-CASHIER", "CASHIER",
                "ROLE-PHARMACIST", "PHARMACIST", "PHARMACY", "PHM"
            },
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

    private async Task EnsureEncounterAndDrugsAsync(string encId, string patId, decimal prepaymentBalance = 5000.00m)
    {
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            DELETE FROM fin_inpatient_fee_items WHERE encounter_id = @EncId;
            DELETE FROM fin_charge_ledgers WHERE visit_or_encounter_id = @EncId;
            DELETE FROM ipd_discharge_medications WHERE encounter_id = @EncId;
            DELETE FROM ord_execution_plans WHERE encounter_id = @EncId;
            DELETE FROM ord_medical_orders WHERE encounter_id = @EncId;
            DELETE FROM mtc_service_requests WHERE patient_id = @PatId;

            INSERT OR REPLACE INTO pat_patients (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_cipher, id_card_hash, phone_masked, allergy_tags_json, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@PatId, 'MRN-' || substr(@PatId, 1, 8), '闭环测试患者', 'BHCS', '1', 19880512, 'ID_CARD', NULL, 'HASH-' || @PatId, '139****0000', '[]', 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO ipd_encounters (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id, is_borrowed_bed, bed_code, active_bed_token, attending_doctor_id, primary_nurse_id, nursing_level_code, prepayment_balance, status, triage_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@EncId, 'ADM-' || substr(@EncId, 1, 8), @PatId, 'CAMPUS-01', 'DEPT-IPD-01', 'DEPT-WARD-02', 0, '08', 'TOKEN-' || @EncId, 'USER-ADMIN', 'USER-ADMIN', 'LEVEL_1', @Prepay, 1, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES ('DRUG-G26-NORM', 'DRUG-G26-NORM', '葡萄糖氯化钠注射液', '250ml', '瓶', 18.00, 10.00, 0, 0, 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES ('DRUG-G26-NORM2', 'DRUG-G26-NORM2', '氯化钾注射液', '10ml:1g', '支', 4.50, 2.00, 0, 0, 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES ('DRUG-G26-ABX2', 'DRUG-G26-ABX2', '头孢哌酮舒巴坦钠', '1.5g', '瓶', 85.00, 50.00, 2, 0, 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, quantity, is_frozen, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES ('BAT-G26-NORM', 'DEPT-PHM-01', 'DRUG-G26-NORM', 'BG26N01', 20291231, 500, 0, 1, @Now, 'TEST', @Now, 'TEST', 1);

            INSERT OR REPLACE INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, quantity, is_frozen, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES ('BAT-G26-NORM2', 'DEPT-PHM-01', 'DRUG-G26-NORM2', 'BG26N02', 20291231, 500, 0, 1, @Now, 'TEST', @Now, 'TEST', 1);
        ", new { EncId = encId, PatId = patId, Prepay = prepaymentBalance, Now = now });
    }

    [Fact]
    public async Task Ordering_ArrearsLockout_AntibioticPrereq_GroupUngroup_And_DischargeMedSync()
    {
        string encId = "ENC-G26-ORD-" + Guid.NewGuid().ToString("N")[..6];
        string patId = "PAT-G26-ORD-" + Guid.NewGuid().ToString("N")[..6];
        await EnsureEncounterAndDrugsAsync(encId, patId, prepaymentBalance: 100.00m);

        // Seed unbilled ledger charge of 200.00 -> availableBalance = 100 - 200 = -100 (arrears)
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await conn.ExecuteAsync(@"
                INSERT INTO fin_charge_ledgers (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id, item_code, item_name, unit_price, quantity, total_amount, refund_amount, status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@Id, @PatId, @EncId, 'DEPT-IPD-01', 'DEPT-WARD-02', 'DEPT-WARD-02', 'FEE-01', '住院床位观察费', 200.00, 1, 200.00, 0, 1, 1, @Now, @Now, 1)",
                new { Id = Guid.NewGuid().ToString("D"), PatId = patId, EncId = encId, Now = now });
        }

        // 1. Non-urgent order while in arrears -> blocked with ARREARS_LOCKOUT (409)
        var nonUrgentReq = new OrderingController.CreateOrderRequestDto(
            encId, patId, "DEPT-IPD-01", "DEPT-WARD-02", "DEPT-WARD-02",
            1, 1, "DRUG", "DRUG-G26-NORM", "葡萄糖氯化钠注射液", 250m, "QD", "IV_DRIP", "USER-ADMIN",
            "NORMAL", null, IsUrgent: false, AllowArrearsOverride: false);
        var arrearsRes = await _client.PostAsJsonAsync("/api/v4/orders", nonUrgentReq);
        var arrearsEnv = await arrearsRes.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>();
        Assert.NotNull(arrearsEnv);
        Assert.Equal(409, arrearsEnv.Code);
        Assert.Contains("ARREARS_LOCKOUT", arrearsEnv.Message);

        // 2. Urgent emergency order bypasses arrears lockout -> succeeds
        var urgentReq = nonUrgentReq with { IsUrgent = true, OrderTermType = 2, FrequencyCode = "ST" };
        var urgentRes = await _client.PostAsJsonAsync("/api/v4/orders", urgentReq);
        var urgentEnv = await urgentRes.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>();
        Assert.NotNull(urgentEnv);
        Assert.Equal(200, urgentEnv.Code);
        Assert.NotNull(urgentEnv.Data);

        // Top up balance via clearing ledger so remaining tests aren't blocked by arrears
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE ipd_encounters SET prepayment_balance = 5000.00 WHERE id = @EncId", new { EncId = encId });
        }

        // 3. Restricted antibiotic (>= 2) without microbiology test -> blocked (409)
        var abxReq = new OrderingController.CreateOrderRequestDto(
            encId, patId, "DEPT-IPD-01", "DEPT-WARD-02", "DEPT-PHM-01",
            1, 2, "DRUG", "DRUG-G26-ABX2", "头孢哌酮舒巴坦钠", 1.5m, "BID", "IV_DRIP", "USER-ADMIN",
            "NORMAL", null, IsUrgent: false, AllowArrearsOverride: false);
        var abxBlockedRes = await _client.PostAsJsonAsync("/api/v4/orders", abxReq);
        var abxBlockedEnv = await abxBlockedRes.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>();
        Assert.NotNull(abxBlockedEnv);
        Assert.Equal(409, abxBlockedEnv.Code);

        // Insert microbiology lab request for patient -> restricted antibiotic order now succeeds
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await conn.ExecuteAsync(@"
                INSERT INTO mtc_service_requests (id, request_no, source_type, source_visit_id, patient_id, order_dept_id, exec_med_tech_dept_id, med_tech_sub_type, service_item_code, fee_amount, is_fee_confirmed, status, is_critical_value, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@Id, @ReqNo, 'IPD', @EncId, @PatId, 'DEPT-IPD-01', 'DEPT-LAB-01', 'MICROBIOLOGY', 'LAB_BLOOD_CULTURE', 120.00, 1, 1, 0, 1, @Now, @Now, 1)",
                new { Id = Guid.NewGuid().ToString("D"), ReqNo = "REQ-MICRO-" + Guid.NewGuid().ToString("N")[..8], EncId = encId, PatId = patId, Now = now });
        }
        var abxAllowedRes = await _client.PostAsJsonAsync("/api/v4/orders", abxReq);
        var abxAllowedEnv = await abxAllowedRes.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>();
        Assert.NotNull(abxAllowedEnv);
        Assert.Equal(200, abxAllowedEnv.Code);

        // 4. Create 2 orders -> group -> verify shared GroupNo -> ungroup -> verify reset
        var gOrd1 = (await (await _client.PostAsJsonAsync("/api/v4/orders", new OrderingController.CreateOrderRequestDto(
            encId, patId, "DEPT-IPD-01", "DEPT-WARD-02", "DEPT-PHM-01",
            1, 10, "DRUG", "DRUG-G26-NORM", "葡萄糖氯化钠注射液", 100m, "QD", "IV_DRIP", "USER-ADMIN"))).Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;
        var gOrd2 = (await (await _client.PostAsJsonAsync("/api/v4/orders", new OrderingController.CreateOrderRequestDto(
            encId, patId, "DEPT-IPD-01", "DEPT-WARD-02", "DEPT-PHM-01",
            1, 11, "DRUG", "DRUG-G26-NORM2", "氯化钾注射液", 10m, "QD", "IV_DRIP", "USER-ADMIN"))).Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;

        var grpRes = await _client.PostAsJsonAsync("/api/v4/orders/group", new OrderingController.AssignGroupRequestDto(new[] { gOrd1.Id, gOrd2.Id }, GroupNo: 777));
        Assert.Equal(HttpStatusCode.OK, grpRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var grpCount = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM ord_medical_orders WHERE id IN (@A, @B) AND group_no = 777", new { A = gOrd1.Id, B = gOrd2.Id });
            Assert.Equal(2, grpCount);
        }

        var ungrpRes = await _client.PostAsJsonAsync("/api/v4/orders/ungroup", new OrderingController.AssignGroupRequestDto(new[] { gOrd1.Id, gOrd2.Id }));
        Assert.Equal(HttpStatusCode.OK, ungrpRes.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var distinctGroups = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM ord_medical_orders WHERE id IN (@A, @B) AND group_no = 777", new { A = gOrd1.Id, B = gOrd2.Id });
            Assert.Equal(0, distinctGroups);
        }

        // 5. Discharge medication order -> Verify syncs into ipd_discharge_medications -> WardDispense marks DISPENSED -> DischargeChecklist passes
        var disOrd = (await (await _client.PostAsJsonAsync("/api/v4/orders", new OrderingController.CreateOrderRequestDto(
            encId, patId, "DEPT-IPD-01", "DEPT-WARD-02", "DEPT-PHM-01",
            2, 50, "DRUG", "DRUG-G26-NORM2", "氯化钾注射液", 1m, "ST", "PO", "USER-ADMIN",
            OrderSubType: "DISCHARGE_MED"))).Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;

        var verifyRes = await _client.PostAsJsonAsync($"/api/v4/orders/{disOrd.Id}/verify", new OrderingController.VerifyOrderRequestDto("USER-ADMIN", "DEPT-WARD-02"));
        var verifyEnv = await verifyRes.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>();
        Assert.NotNull(verifyEnv);
        Assert.Equal(200, verifyEnv.Code);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var disMedStatus = await conn.ExecuteScalarAsync<string>("SELECT status FROM ipd_discharge_medications WHERE encounter_id = @EncId LIMIT 1", new { EncId = encId });
            Assert.Equal("PRESCRIBED", disMedStatus);
        }

        var dispRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/ward-dispense", new PharmacyController.WardDispenseRequestDto(
            "DEPT-PHM-01", encId, new[] { new PharmacyController.WardDispenseLineRequestDto(disOrd.Id, 1) }));
        var dispEnv = await dispRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.NotNull(dispEnv);
        Assert.Equal(200, dispEnv.Code);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var disMedStatus = await conn.ExecuteScalarAsync<string>("SELECT status FROM ipd_discharge_medications WHERE encounter_id = @EncId LIMIT 1", new { EncId = encId });
            Assert.Equal("DISPENSED", disMedStatus);
        }
    }

    [Fact]
    public async Task Finance_DailyCostSheet_SettleInClinic_And_InfusionInspections_Work()
    {
        string encId = "ENC-G26-FIN-" + Guid.NewGuid().ToString("N")[..6];
        string patId = "PAT-G26-FIN-" + Guid.NewGuid().ToString("N")[..6];
        string visitId = "VIS-G26-FIN-" + Guid.NewGuid().ToString("N")[..6];
        string rxId = "RX-G26-FIN-" + Guid.NewGuid().ToString("N")[..6];
        await EnsureEncounterAndDrugsAsync(encId, patId, prepaymentBalance: 1000.00m);

        // 1. Insert 1 ledger item and 1 fee item -> verify /daily-cost-sheet sums both
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int todayInt = int.Parse(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMdd"));

            await conn.ExecuteAsync(@"
                INSERT INTO fin_charge_ledgers (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id, item_code, item_name, unit_price, quantity, total_amount, refund_amount, status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@LId, @PatId, @EncId, 'DEPT-IPD-01', 'DEPT-WARD-02', 'DEPT-WARD-02', 'ITEM-F1', '静脉输液费', 45.00, 1, 45.00, 0, 1, 1, @Now, @Now, 1);

                INSERT INTO fin_inpatient_fee_items (id, encounter_id, patient_id, fee_date_int, item_category, item_code, item_name, unit_name, unit_price, quantity, total_amount, charge_dept_id, exec_dept_id, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@FId, @EncId, @PatId, @TodayInt, 'BED', 'BED-F1', '双人间床位费', '日', 80.00, 1, 80.00, 'DEPT-IPD-01', 'DEPT-WARD-02', 1, @Now, @Now, 1);

                INSERT OR REPLACE INTO opd_visits (id, visit_no, campus_id, opd_dept_id, clinic_room_id, patient_id, schedule_id, doctor_id, is_emergency, is_green_channel, is_return_visit, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@VisitId, 'VN-' || @VisitId, 'CAMPUS-01', 'DEPT-OPD-01', 'ROOM-OPD-101', @PatId, NULL, 'USER-ADMIN', 0, 0, 0, 2, 1, @Now, 'USER-ADMIN', @Now, 'USER-ADMIN', 1);

                INSERT OR REPLACE INTO opd_prescriptions (id, prescription_no, visit_id, patient_id, opd_dept_id, target_pharmacy_id, rx_type, total_amount, doctor_id, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@RxId, 'RX-' || @RxId, @VisitId, @PatId, 'DEPT-OPD-01', 'DEPT-PHM-01', 'WESTERN', 36.00, 'USER-ADMIN', 1, 1, @Now, 'TEST', @Now, 'TEST', 1);

                INSERT OR REPLACE INTO opd_prescription_items (id, prescription_id, group_no, drug_id, drug_name_snapshot, dose_once, dose_unit, frequency_code, route_code, days, total_quantity, unit_price, skin_test_flag, is_deleted, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@RxiId, @RxId, 1, 'DRUG-G26-NORM', '葡萄糖氯化钠注射液', 250, 'ml', 'QD', 'IV_DRIP', 1, 2, 18.00, 0, 0, 1, @Now, @Now, 1);
            ", new { LId = Guid.NewGuid().ToString("D"), FId = Guid.NewGuid().ToString("D"), EncId = encId, PatId = patId, TodayInt = todayInt, VisitId = visitId, RxId = rxId, RxiId = "RXI-" + rxId, Now = now });
        }

        var costRes = await _client.GetFromJsonAsync<ApiResponse<FinanceController.InpatientDailyCostSheetDto>>($"/api/v4/ipd/encounters/{encId}/daily-cost-sheet");
        Assert.NotNull(costRes?.Data);
        Assert.Equal(200, costRes.Code);
        Assert.True(costRes.Data.CumulativeTotalAmount >= 125.00m);

        // 2. Recharge outpatient account & settle IV prescription in-clinic -> verify ledger, dispense queue & auto infusion order + GET inspections
        var recRes = await _client.PostAsJsonAsync("/api/v4/opd/accounts/recharge", new FinanceController.OpdRechargeRequestDto(patId, 500.00m, "CASH"));
        Assert.Equal(HttpStatusCode.OK, recRes.StatusCode);

        var settleRes = await _client.PostAsJsonAsync("/api/v4/opd/accounts/settle-in-clinic", new FinanceController.OpdInClinicSettleRequestDto(patId, rxId, "USER-ADMIN"));
        var settleEnv = await settleRes.Content.ReadFromJsonAsync<ApiResponse<FinPaymentOrder>>();
        Assert.NotNull(settleEnv?.Data);
        Assert.Equal(200, settleEnv.Code);

        string infusionOrderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            int ledgerCnt = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM fin_charge_ledgers WHERE visit_or_encounter_id = @VisitId", new { VisitId = visitId });
            Assert.True(ledgerCnt >= 1);

            infusionOrderId = await conn.ExecuteScalarAsync<string>("SELECT id FROM opd_infusion_orders WHERE prescription_id = @RxId LIMIT 1", new { RxId = rxId }) ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(infusionOrderId));
        }

        var inspRes = await _client.GetFromJsonAsync<ApiResponse<List<object>>>($"/api/v4/opd/infusion/orders/{infusionOrderId}/inspections");
        Assert.NotNull(inspRes);
        Assert.Equal(200, inspRes.Code);
    }

    [Fact]
    public async Task Inventory_PoApprove_GspDualAcceptance_And_AutoFreezeExpired_Work()
    {
        string whId = "WH-MAIN";
        string drugId = "DRUG-G26-NORM";
        await EnsureEncounterAndDrugsAsync("ENC-G26-INV-DUMMY", "PAT-G26-INV-DUMMY");

        var supRes = await _client.GetFromJsonAsync<ApiResponse<List<InventoryController.SupplierDto>>>("/api/v4/inventory/suppliers");
        Assert.NotNull(supRes?.Data);
        Assert.Equal(200, supRes.Code);
        Assert.NotEmpty(supRes.Data);
        string supId = supRes.Data[0].Id;

        var poCreateRes = await _client.PostAsJsonAsync("/api/v4/inventory/purchase-orders", new InventoryController.CreatePurchaseOrderRequestDto(
            whId, supId, new[] { new InventoryController.CreatePurchaseOrderLineDto(drugId, 40, 10.00m) }, AutoApprove: false, Remark: "GSP集成测试采购"));
        var poEnv = await poCreateRes.Content.ReadFromJsonAsync<ApiResponse<InventoryController.PurchaseOrderViewDto>>();
        Assert.NotNull(poEnv?.Data);
        Assert.Equal(200, poEnv.Code);
        Assert.Equal("DRAFT", poEnv.Data.Status);

        var poApproveRes = await _client.PostAsync($"/api/v4/inventory/purchase-orders/{poEnv.Data.Id}/approve", null);
        var approveEnv = await poApproveRes.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.True(approveEnv?.Data);

        // Unqualified GSP acceptance -> quarantine (is_frozen = 1)
        string badBatchNo = "BAD-" + Guid.NewGuid().ToString("N")[..6];
        var badGspRes = await _client.PostAsJsonAsync("/api/v4/inventory/gsp-acceptance", new InventoryController.GspAcceptanceRequestDto(
            poEnv.Data.Id, whId, drugId, badBatchNo, 20291231, 10, "REC-01", "CHK-02",
            AppearanceOk: false, PackageIntact: true, LabelConsistent: true, ColdChainTemp: 12.0m, MaxAllowedColdChainTemp: 8.0m, Remark: "冷链超温拒收"));
        var badGspEnv = await badGspRes.Content.ReadFromJsonAsync<ApiResponse<InventoryController.GspAcceptanceViewDto>>();
        Assert.NotNull(badGspEnv?.Data);
        Assert.Equal("QUARANTINE_REJECTED", badGspEnv.Data.Conclusion);

        // Qualified GSP acceptance -> active batch
        string goodBatchNo = "GOOD-" + Guid.NewGuid().ToString("N")[..6];
        var goodGspRes = await _client.PostAsJsonAsync("/api/v4/inventory/gsp-acceptance", new InventoryController.GspAcceptanceRequestDto(
            poEnv.Data.Id, whId, drugId, goodBatchNo, 20291231, 30, "REC-01", "CHK-02",
            AppearanceOk: true, PackageIntact: true, LabelConsistent: true, ColdChainTemp: 4.5m, MaxAllowedColdChainTemp: 8.0m, Remark: "GSP双人验收合格"));
        var goodGspEnv = await goodGspRes.Content.ReadFromJsonAsync<ApiResponse<InventoryController.GspAcceptanceViewDto>>();
        Assert.NotNull(goodGspEnv?.Data);
        Assert.Equal("QUALIFIED", goodGspEnv.Data.Conclusion);

        // Seed expired batch and verify auto-freeze
        string expBatchId = "EXP-BAT-" + Guid.NewGuid().ToString("N")[..6];
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, quantity, is_frozen, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @WhId, @DrugId, @Id, 20200101, 15, 0, 1, @Now, 'TEST', @Now, 'TEST', 1)",
                new { Id = expBatchId, WhId = whId, DrugId = drugId, Now = now });
        }

        var freezeRes = await _client.PostAsJsonAsync("/api/v4/inventory/batches/auto-freeze-expired", new InventoryController.AutoFreezeExpiryRequestDto(whId, NearExpiryDays: 0));
        Assert.Equal(HttpStatusCode.OK, freezeRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            int frozen = await conn.ExecuteScalarAsync<int>("SELECT is_frozen FROM inv_drug_batches WHERE id = @Id", new { Id = expBatchId });
            Assert.Equal(1, frozen);
        }
    }

    [Fact]
    public async Task Outpatient_SubHourSlotGeneration_And_ReserveCheckinFlow_Work()
    {
        string doctorId;
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            doctorId = await conn.ExecuteScalarAsync<string>(
                "SELECT id FROM sys_users WHERE is_active = 1 AND id IN (SELECT user_id FROM sys_user_dept_posts WHERE dept_id = 'DEPT-OPD-01') LIMIT 1")
                ?? "USER-DOC-OPD";
        }

        // Create template with 30-min sub-hour intervals from 08:00 (480) to 10:00 (600)
        var tplReq = new OutpatientController.SaveScheduleTemplateRequestDto(
            TemplateName: "分时测试模板-" + Guid.NewGuid().ToString("N")[..4],
            OpdDeptId: "DEPT-OPD-01",
            ClinicRoomId: "ROOM-OPD-101",
            DoctorId: doctorId,
            WeekdayCodes: "1,2,3,4,5,6,7",
            SlotPeriodCode: "MORNING",
            StartMinute: 480,
            EndMinute: 600,
            RegLevelCode: "NORMAL",
            RegistrationFee: 15.00m,
            TotalCapacity: 12,
            IsActive: 1,
            CampusId: "CAMPUS-01",
            SlotIntervalMinutes: 30);

        var tplRes = await _client.PostAsJsonAsync("/api/v4/opd/schedule-templates", tplReq);
        var tplEnv = await tplRes.Content.ReadFromJsonAsync<ApiResponse<OpdScheduleTemplate>>();
        Assert.NotNull(tplEnv?.Data);
        Assert.Equal(200, tplEnv.Code);

        var targetDateOffset = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date.AddDays(5);
        var targetDate = targetDateOffset.ToString("yyyy-MM-dd");
        int dateInt = int.Parse(targetDateOffset.ToString("yyyyMMdd"));

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            await conn.ExecuteAsync(
                "DELETE FROM opd_schedule_slots WHERE opd_dept_id = 'DEPT-OPD-01' AND doctor_id = @DocId AND schedule_date_int = @D",
                new { DocId = doctorId, D = dateInt });
        }

        var genRes = await _client.PostAsJsonAsync($"/api/v4/opd/schedule-templates/{tplEnv.Data.Id}/generate", new
        {
            templateId = tplEnv.Data.Id,
            startDate = targetDate,
            endDate = targetDate
        });
        Assert.Equal(HttpStatusCode.OK, genRes.StatusCode);

        string slotId;
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var subSlots = (await conn.QueryAsync<OpdScheduleSlot>(
                "SELECT * FROM opd_schedule_slots WHERE opd_dept_id = 'DEPT-OPD-01' AND doctor_id = @DocId AND schedule_date_int = @D AND slot_period_code LIKE 'MORNING_%' ORDER BY starts_at_ms",
                new { DocId = doctorId, D = dateInt })).ToList();
            Assert.Equal(4, subSlots.Count); // 08:00-08:30, 08:30-09:00, 09:00-09:30, 09:30-10:00
            Assert.Equal("MORNING_0800", subSlots[0].SlotPeriodCode);
            Assert.Equal("MORNING_0830", subSlots[1].SlotPeriodCode);
            slotId = subSlots[0].Id;
        }

        string patId = "PAT-G26-REG-" + Guid.NewGuid().ToString("N")[..6];
        await EnsureEncounterAndDrugsAsync("ENC-G26-REG-DUMMY", patId);

        // Two-step Reserve -> Checkin
        var reserveRes = await _client.PostAsJsonAsync("/api/v4/opd/registration/registrations/reserve",
            new OpdRegistrationController.ReserveRegistrationRequestDto(patId, slotId));
        var reserveEnv = await reserveRes.Content.ReadFromJsonAsync<ApiResponse<OpdRegistrationController.RegistrationListItem>>();
        Assert.NotNull(reserveEnv?.Data);
        Assert.Equal(200, reserveEnv.Code);

        var reservedList = await _client.GetFromJsonAsync<ApiResponse<List<OpdRegistrationController.RegistrationListItem>>>(
            $"/api/v4/opd/registration/registrations/reserved?date={targetDate}");
        Assert.Contains(reservedList?.Data ?? new(), r => r.Id == reserveEnv.Data.Id);

        var checkinRes = await _client.PostAsJsonAsync($"/api/v4/opd/registration/registrations/{reserveEnv.Data.Id}/checkin",
            new OpdRegistrationController.CheckinRegistrationRequestDto("CASH"));
        var checkinEnv = await checkinRes.Content.ReadFromJsonAsync<ApiResponse<OpdRegistrationController.RegistrationListItem>>();
        Assert.NotNull(checkinEnv?.Data);
        Assert.Equal(200, checkinEnv.Code);
        Assert.NotNull(checkinEnv.Data.VisitId);
    }

    [Fact]
    public async Task MedTech_SpecimenCourierChain_And_DicomStudies_Work()
    {
        string patId = "PAT-G26-MTC-" + Guid.NewGuid().ToString("N")[..6];
        string barcode1 = "BC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string barcode2 = "BC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string reqId = "REQ-G26-MTC-" + Guid.NewGuid().ToString("N")[..6];

        // 1. Draw -> Dispatch -> Receive
        var drawRes = await _client.PostAsJsonAsync("/api/v4/medtech/specimens/draw",
            new MedTechController.SpecimenDrawRequestDto(reqId, patId, barcode1, "DEPT-WARD-02", "常规采血"));
        var drawEnv = await drawRes.Content.ReadFromJsonAsync<ApiResponse<MedTechController.SpecimenHandoverDto>>();
        Assert.NotNull(drawEnv?.Data);
        Assert.Equal("DRAWN", drawEnv.Data.Status);

        var dispRes = await _client.PostAsJsonAsync($"/api/v4/medtech/specimens/{drawEnv.Data.Id}/dispatch",
            new MedTechController.SpecimenDispatchRequestDto("COURIER-007", "冷链转运"));
        var dispEnv = await dispRes.Content.ReadFromJsonAsync<ApiResponse<MedTechController.SpecimenHandoverDto>>();
        Assert.Equal("IN_TRANSIT", dispEnv?.Data?.Status);

        var recvRes = await _client.PostAsync($"/api/v4/medtech/specimens/{drawEnv.Data.Id}/receive", null);
        var recvEnv = await recvRes.Content.ReadFromJsonAsync<ApiResponse<MedTechController.SpecimenHandoverDto>>();
        Assert.Equal("RECEIVED", recvEnv?.Data?.Status);

        // 2. Draw -> Reject
        var draw2 = (await (await _client.PostAsJsonAsync("/api/v4/medtech/specimens/draw",
            new MedTechController.SpecimenDrawRequestDto(reqId, patId, barcode2, "DEPT-WARD-02"))).Content.ReadFromJsonAsync<ApiResponse<MedTechController.SpecimenHandoverDto>>())!.Data!;
        var rejectRes = await _client.PostAsJsonAsync($"/api/v4/medtech/specimens/{draw2.Id}/reject",
            new MedTechController.SpecimenRejectRequestDto("标本溶血退回重采"));
        var rejectEnv = await rejectRes.Content.ReadFromJsonAsync<ApiResponse<MedTechController.SpecimenHandoverDto>>();
        Assert.Equal("REJECTED", rejectEnv?.Data?.Status);

        // 3. DICOM Study query
        string uid = "1.2.840.113619.2." + Guid.NewGuid().ToString("N")[..12];
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await conn.ExecuteAsync(@"
                INSERT INTO mtc_dicom_studies (id, study_instance_uid, accession_no, patient_id, modality, series_count, instance_count, wado_rs_base_url, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@Id, @Uid, @ReqId, @PatId, 'CT', 4, 180, 'http://localhost:8042/dicom-web', 1, @Now, @Now, 1)",
                new { Id = Guid.NewGuid().ToString("D"), Uid = uid, ReqId = reqId, PatId = patId, Now = now });
        }

        var dicomEnv = await _client.GetFromJsonAsync<ApiResponse<List<MedTechController.DicomStudySummaryDto>>>(
            $"/api/v4/medtech/exam/studies?patientId={patId}");
        Assert.NotNull(dicomEnv?.Data);
        Assert.Single(dicomEnv.Data);
        Assert.Equal("CT", dicomEnv.Data[0].Modality);
    }

    [Fact]
    public async Task AuditLogicFixes_2026_AllCriticalAndHighGuards_Work()
    {
        string encId = "ENC-AUD-" + Guid.NewGuid().ToString("N")[..6];
        string patId = "PAT-AUD-" + Guid.NewGuid().ToString("N")[..6];
        await EnsureEncounterAndDrugsAsync(encId, patId, prepaymentBalance: 5000.00m);

        // 1. Ungrouped StopOrder does NOT stop sibling ungrouped orders (GroupNo=0)
        var u1 = (await (await _client.PostAsJsonAsync("/api/v4/orders", new OrderingController.CreateOrderRequestDto(
            encId, patId, "DEPT-IPD-01", "DEPT-WARD-02", "DEPT-PHM-01",
            1, 0, "DRUG", "DRUG-G26-NORM", "葡萄糖氯化钠注射液", 1m, "QD", "IV_DRIP", "USER-ADMIN"))).Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;
        var u2 = (await (await _client.PostAsJsonAsync("/api/v4/orders", new OrderingController.CreateOrderRequestDto(
            encId, patId, "DEPT-IPD-01", "DEPT-WARD-02", "DEPT-PHM-01",
            1, 0, "DRUG", "DRUG-G26-NORM2", "氯化钾注射液", 1m, "QD", "IV_DRIP", "USER-ADMIN"))).Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>())!.Data!;
        await _client.PostAsJsonAsync("/api/v4/orders/ungroup", new OrderingController.AssignGroupRequestDto(new[] { u1.Id, u2.Id }));

        var stopRes = await _client.PostAsync($"/api/v4/orders/{u1.Id}/stop?doctorId=USER-ADMIN", null);
        Assert.Equal(HttpStatusCode.OK, stopRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            int u2Status = await conn.ExecuteScalarAsync<int>("SELECT status FROM ord_medical_orders WHERE id = @Id", new { Id = u2.Id });
            Assert.Equal(0, u2Status); // u2 must still be unstopped!
        }

        // 2. Finance RecordInpatientFeeItem rejects non-positive quantity or negative unit price
        var badFeeRes = await _client.PostAsJsonAsync("/api/v4/finance/inpatient-fee-items",
            new FinanceController.RecordInpatientFeeItemRequestDto(
                encId, patId, 20261125, "MEDICINE", "DRUG-G26-NORM", "葡萄糖氯化钠注射液", "250ml", "瓶", 18m, -2m, 0m, "CLASS_A", "DEPT-IPD-01", "DEPT-PHM-01"));
        var badFeeEnv = await badFeeRes.Content.ReadFromJsonAsync<ApiResponse<FinInpatientFeeItem>>();
        Assert.Equal(400, badFeeEnv?.Code);

        // 3. StockTake: duplicate line blocked (409), ApproveStockTake updates actual batch quantity & writes INV_ADJUST ledger
        string whId = "WH-AUD-" + Guid.NewGuid().ToString("N")[..6];
        string stBatchId = "BAT-ST-" + Guid.NewGuid().ToString("N")[..6];
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, quantity, is_frozen, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @WhId, 'DRUG-G26-NORM', 'BST01', 20291231, 50, 0, 1, @Now, 'TEST', @Now, 'TEST', 1)",
                new { Id = stBatchId, WhId = whId, Now = now });
        }

        var stRes = await _client.PostAsJsonAsync("/api/v4/inventory/stock-takes",
            new InventoryController.CreateStockTakeRequestDto(whId, "审计测试药库", "CYCLE_COUNT", "AUD盘点"));
        var stEnv = await stRes.Content.ReadFromJsonAsync<ApiResponse<InvStockTake>>();
        Assert.NotNull(stEnv?.Data);

        var lineReq = new InventoryController.AddStockTakeLineRequestDto("DRUG-G26-NORM", stBatchId, "DRUG-G26-NORM", "葡萄糖氯化钠注射液", "250ml", "瓶", "BST01", 50, 42, 10m, "损耗");
        var l1 = await (await _client.PostAsJsonAsync($"/api/v4/inventory/stock-takes/{stEnv.Data.Id}/lines", lineReq)).Content.ReadFromJsonAsync<ApiResponse<InvStockTakeLine>>();
        Assert.Equal(200, l1?.Code);
        var lDup = await (await _client.PostAsJsonAsync($"/api/v4/inventory/stock-takes/{stEnv.Data.Id}/lines", lineReq)).Content.ReadFromJsonAsync<ApiResponse<InvStockTakeLine>>();
        Assert.Equal(409, lDup?.Code);

        var stApp = await (await _client.PostAsJsonAsync($"/api/v4/inventory/stock-takes/{stEnv.Data.Id}/approve", new InventoryController.ApproveStockTakeRequestDto("核准"))).Content.ReadFromJsonAsync<ApiResponse<InvStockTake>>();
        Assert.Equal("APPROVED", stApp?.Data?.Status);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            int newQty = await conn.ExecuteScalarAsync<int>("SELECT quantity FROM inv_drug_batches WHERE id = @Id", new { Id = stBatchId });
            Assert.Equal(42, newQty);
            int adjLedger = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM inv_stock_ledgers WHERE batch_id = @Id AND change_type = 'INV_ADJUST' AND change_quantity = -8", new { Id = stBatchId });
            Assert.Equal(1, adjLedger);
        }
    }

    [Fact]
    public async Task MasterDataGovernance_15Domains_And_StandardValidations_Work()
    {
        // 1. Summary across all 15 MDM domains
        var sumRes = await _client.GetFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>("/api/v4/mdm/summary");
        Assert.Equal(200, sumRes?.Code);
        Assert.True(sumRes!.Data.GetProperty("organizations").GetInt32() >= 2);
        Assert.True(sumRes.Data.GetProperty("tcmDiagnoses").GetInt32() >= 6);
        Assert.True(sumRes.Data.GetProperty("icd9Operations").GetInt32() >= 4);
        Assert.True(sumRes.Data.GetProperty("examStdItems").GetInt32() >= 5);

        // 2. Organization USCC 18-char GB 32100-2015 validation + save
        var badOrg = await _client.PostAsJsonAsync("/api/v4/mdm/organizations", new
        {
            OrgCode = "ORG-BAD", Uscc = "12345", OrgName = "无效信用代码院区"
        });
        var badOrgEnv = await badOrg.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(400, badOrgEnv?.Code);

        string orgTag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var goodOrg = await _client.PostAsJsonAsync("/api/v4/mdm/organizations", new
        {
            OrgCode = "ORG-" + orgTag,
            Uscc = $"12110000400001{orgTag}",
            YbOrgCode = "H11010109999",
            OrgName = "GHIS 主数据测试分院",
            CampusTag = "WEST",
            OrgLevel = "三级甲等",
            OrgCategoryCode = "A100",
            AdminRegionCode = "110108",
            PracticeLicenseNo = "PDY00999110108"
        });
        var goodOrgEnv = await goodOrg.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(200, goodOrgEnv?.Code);

        // 3. Bed utilization & authorized vs open bed
        var bedSave = await _client.PostAsJsonAsync("/api/v4/mdm/beds", new
        {
            CampusId = "CAMPUS-01",
            WardId = "DEPT-WARD-02",
            RoomCode = "R801",
            BedCode = "B-" + orgTag,
            BedType = "EXTRA",
            DailyFee = 35m,
            ChargeLevel = "NORMAL",
            GenderLimit = "ALL",
            IsAuthorizedBed = false,
            IsOpenBed = true
        });
        Assert.Equal(HttpStatusCode.OK, bedSave.StatusCode);
        var utilEnv = await _client.GetFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>("/api/v4/mdm/beds/utilization");
        Assert.Equal(200, utilEnv?.Code);
        Assert.True(utilEnv!.Data.GetProperty("openBeds").GetInt32() >= 1);

        // 4. Drugs & Consumables full standard fields + OrderItems + ChargeMappings
        var drugSave = await _client.PostAsJsonAsync("/api/v4/mdm/drugs", new
        {
            DrugCode = "MDM-DRG-" + orgTag,
            DrugName = "阿莫西林克拉维酸钾片",
            GenericName = "阿莫西林克拉维酸钾",
            TradeName = "奥先",
            DosageForm = "片剂",
            Spec = "0.375g*12片",
            UnitName = "盒",
            PackUnit = "盒",
            BaseDoseUnit = "g",
            ConversionRatio = 4.5m,
            Price = 28.50m,
            CostPrice = 16.00m,
            ApprovalNo = "国药准字H20051234",
            Manufacturer = "华北制药",
            AtcCode = "J01CR02",
            IsEssentialDrug = true,
            IsCentralProcured = true,
            AntibioticLevel = 1,
            IsNarcotic = false,
            IsSkinTestRequired = true,
            DddValue = 1.0m,
            YbDrugCode = "X-J01CR-001",
            DrugStandardCode = "86900001000123"
        });
        Assert.Equal(HttpStatusCode.OK, drugSave.StatusCode);

        var csmSave = await _client.PostAsJsonAsync("/api/v4/mdm/consumables", new
        {
            Code = "MDM-CSM-" + orgTag,
            Name = "药物洗脱冠脉支架",
            Spec = "3.0mm*18mm",
            Unit = "枚",
            Category = "心血管植入物",
            Price = 780.00m,
            UdiDi = "06901234567890",
            IsImplant = true,
            IsDisposable = true,
            RegCertNo = "国械注准20213130001",
            Manufacturer = "微创医疗",
            IsChargeable = true,
            YbConsumableCode = "C010101001",
            IsHighValue = true,
            IsSterile = true
        });
        Assert.Equal(HttpStatusCode.OK, csmSave.StatusCode);

        var ordItemSave = await _client.PostAsJsonAsync("/api/v4/mdm/order-items", new
        {
            OrderItemCode = "ORDI-" + orgTag,
            OrderItemName = "胸部高分辨CT平扫",
            OrderCategory = "EXAM",
            ExecDeptId = "DEPT-RAD-01",
            SampleOrBodyPart = "胸部",
            YbServiceCode = "210200001"
        });
        Assert.Equal(HttpStatusCode.OK, ordItemSave.StatusCode);

        var mapSave = await _client.PostAsJsonAsync("/api/v4/mdm/charge-mappings", new
        {
            ChargeItemId = "PI-EXM-DR",
            InsType = "RESIDENT_INS_" + orgTag,
            EffDateInt = 20250101,
            ExpDateInt = 20991231,
            YbCatalogCode = "210101001000002",
            InsGrade = "CLASS_A",
            SelfPayRatio = 0.05m
        });
        Assert.Equal(HttpStatusCode.OK, mapSave.StatusCode);

        // 5. TCM, ICD-9-CM-3, Exam Std Items & Standard Dict Mapping
        var tcmSave = await _client.PostAsJsonAsync("/api/v4/mdm/tcm-diagnoses", new
        {
            StdVer = "TCM-GB95", TcmCode = "B" + orgTag + "0", TcmName = "消渴病", PinyinCode = "XKB", TcmType = "DISEASE"
        });
        Assert.Equal(HttpStatusCode.OK, tcmSave.StatusCode);

        var icd9Save = await _client.PostAsJsonAsync("/api/v4/mdm/icd9-operations", new
        {
            StdVer = "ICD9-CM3-YB", Icd9Code = "54.11" + orgTag[..2], OperationName = "腹腔镜探查术", SurgeryLevel = 2, IsMinimallyInvasive = true, IsDaySurgery = true
        });
        Assert.Equal(HttpStatusCode.OK, icd9Save.StatusCode);

        var examSave = await _client.PostAsJsonAsync("/api/v4/mdm/exam-std-items", new
        {
            StdCode = "STD-" + orgTag, StdName = "血清降钙素原 (PCT)", ItemKind = "LAB", SpecimenType = "VENOUS_BLOOD", ResultUnit = "ng/mL", RefRange = "<0.05", CriticalHigh = "10.0", LoincCode = "33959-8"
        });
        Assert.Equal(HttpStatusCode.OK, examSave.StatusCode);

        var dictSave = await _client.PostAsJsonAsync("/api/v4/config/dictionaries/items", new
        {
            DictType = "ETHNICITY", ItemCode = "9" + orgTag[..1], ItemName = "测试民族", PinyinCode = "CSMZ", SortOrder = 99, IsDefault = false, StdMappingCode = "GB3304-99"
        });
        Assert.Equal(HttpStatusCode.OK, dictSave.StatusCode);
    }

    [Fact]
    public async Task MasterDataPhase2_19Domains_And_YbEncodingRules_Work()
    {
        string tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

        // 1. Phase 2 summary populated
        var p2Sum = await _client.GetFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>("/api/v4/mdm/summary-phase2");
        Assert.Equal(200, p2Sum?.Code);
        Assert.True(p2Sum!.Data.GetProperty("ybDiseases").GetInt32() >= 4);
        Assert.True(p2Sum.Data.GetProperty("warehouses").GetInt32() >= 3);
        Assert.True(p2Sum.Data.GetProperty("cssdPacks").GetInt32() >= 2);
        Assert.True(p2Sum.Data.GetProperty("orRooms").GetInt32() >= 2);
        Assert.True(p2Sum.Data.GetProperty("caCertificates").GetInt32() >= 1);

        // 2. YB Disease code validation (M/B/R prefix)
        var badYbDis = await (await _client.PostAsJsonAsync("/api/v4/mdm/yb-diseases", new
        {
            DiseaseCode = "X99999", DiseaseName = "非法前缀病种"
        })).Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(400, badYbDis?.Code);

        var okYbDis = await (await _client.PostAsJsonAsync("/api/v4/mdm/yb-diseases", new
        {
            DiseaseCode = "M09" + tag[..3], DiseaseName = "慢性肾脏病(门诊慢特病)", DiseaseCategory = "OPD_CHRONIC", QuotaLimitAmount = 6000m
        })).Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(200, okYbDis?.Code);

        // 3. DRG/DIP 3.0 & IVD & PayerPolicy
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/drg-rules", new
        {
            GroupCode = "DRG-" + tag, GroupName = "呼吸系统感染伴严重并发症", PayMode = "DRG", MdcCode = "MDCE", AdrgCode = "ER1", RwWeight = 1.65m, BenchmarkFee = 14500m, StdVer = "CHS-DRG-3.0"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/ivd-reagents", new
        {
            ReagentCode = "IVD-" + tag, GenericName = "肌钙蛋白I检测试剂盒", Methodology = "化学发光", YbIvdCode = "IVD2509" + tag, UdiDi = "0690123499" + tag
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/payer-policies", new
        {
            FeeTypeCode = "WORK_INJURY_" + tag, FeeTypeName = "工伤保险", PoolAreaCode = "110100", ContractUnitName = "市社保中心"
        })).StatusCode);

        // 4. Equipment, Warehouse, CSSD, Logistics
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/equipments", new
        {
            Code = "EQP-" + tag, Name = "256排超高端螺旋CT", ModelSpec = "Revolution CT", DeptId = "DEPT-RAD-01", SerialNo = "SN-" + tag, DepreciationYears = 8, YbImagingDeviceCode = "IMG-CT-" + tag
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/warehouses", new
        {
            WarehouseCode = "WH-" + tag, WarehouseName = "高值耗材二级库", WarehouseType = "CSM_SUB", TempHumidityReq = "18-26℃"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/cssd-packs", new
        {
            PackCode = "CSSD-" + tag, PackName = "骨科关节置换包", SterilizationMethod = "PLASMA", ValidityDays = 180
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/logistics-items", new
        {
            ItemCode = "LOG-" + tag, ItemName = "医用防护服备品", Category = "SPARE_PART", UnitName = "套", RefPrice = 65m
        })).StatusCode);

        // 5. Clinical execution, Microbiology, Imaging, OR, Nutrition
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/medication-rules", new
        {
            RuleCode = "MR-" + tag, RuleName = "头孢类皮试后静滴", RouteCode = "IVGTT", FrequencyCode = "BID", SkinTestRequired = true
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/microbiology", new
        {
            MicroCode = "MIC-" + tag, MicroName = "鲍曼不动杆菌(CRAB)", MicroType = "BACTERIA", IsMdrMonitored = true
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/imaging-mappings", new
        {
            MappingCode = "IM-" + tag, Modality = "MR", BodyPartName = "膝关节", ExamRoomCode = "RAD-MR-02", DicomAeTitle = "GHIS_MR_02"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/or-rooms", new
        {
            RoomCode = "OR-" + tag, RoomName = "复合杂交手术间", CleanLevel = "CLASS_100"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/nutrition-items", new
        {
            ItemCode = "NUT-" + tag, ItemName = "短肽型肠内营养剂", NutritionType = "EN", KcalPerUnit = 500m
        })).StatusCode);

        // 6. Location, MedicalGroup, RegLevel, Accounting, Terminals, CA, YB Pharmacist/Pharmacy
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/locations", new
        {
            LocationCode = "LOC-" + tag, LocationName = "特需门诊301室", LocationLevel = "ROOM", QueueScreenId = "SCR-301"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/medical-groups", new
        {
            GroupCode = "MG-" + tag, GroupName = "心内科二组", DeptId = "DEPT-CARD-01", LeaderStaffId = "USER-DOC-ATTENDING"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/reg-levels", new
        {
            RegLevelCode = "VIP_" + tag, RegLevelName = "特需专家号", RegFee = 200m
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/accounting-subjects", new
        {
            SubjectCode = "4001" + tag, SubjectName = "住院手术收入", IncomeExpenseType = "REVENUE", CostCenterType = "DIRECT_MED"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/system-terminals", new
        {
            SysCode = "KIOSK-" + tag, SysName = "门诊一楼自助机", EntryType = "KIOSK", EndpointOrIp = "10.10.1.55"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/v4/mdm/ca-certificates", new
        {
            CertSerial = "BJCA-" + tag, StaffId = "USER-ADMIN", ValidDays = 365
        })).StatusCode);

        // Y 13-char Pharmacist & P 12-char Pharmacy validation
        var badPhm = await (await _client.PostAsJsonAsync("/api/v4/mdm/yb-ext-entities", new
        {
            YbCode = "Y123", EntityType = "PHARMACIST", EntityName = "短编码药师"
        })).Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(400, badPhm?.Code);

        var okPhm = await (await _client.PostAsJsonAsync("/api/v4/mdm/yb-ext-entities", new
        {
            YbCode = "Y11010100" + tag, EntityType = "PHARMACIST", EntityName = "李执业药师"
        })).Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(200, okPhm?.Code);
    }

    [Fact]
    public async Task MasterData_BusinessLinkages_Enforce_BedGender_MedicationRuleTimes_And_RegFee()
    {
        string tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        string maleBedCode = "BM-" + tag;
        string closedBedCode = "BC-" + tag;
        string femalePatId = "PAT-F-" + tag;

        // 1. Bed master data (gender_limit=M and is_open_bed=0) enforces Inpatient AdmitPatient
        await _client.PostAsJsonAsync("/api/v4/mdm/beds", new
        {
            CampusId = "CAMPUS-01", WardId = "DEPT-WARD-02", RoomCode = "R901", BedCode = maleBedCode,
            BedType = "AUTHORIZED", DailyFee = 50m, ChargeLevel = "NORMAL", GenderLimit = "M", IsAuthorizedBed = true, IsOpenBed = true
        });
        await _client.PostAsJsonAsync("/api/v4/mdm/beds", new
        {
            CampusId = "CAMPUS-01", WardId = "DEPT-WARD-02", RoomCode = "R902", BedCode = closedBedCode,
            BedType = "AUTHORIZED", DailyFee = 50m, ChargeLevel = "NORMAL", GenderLimit = "ALL", IsAuthorizedBed = true, IsOpenBed = false
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await conn.ExecuteAsync(@"
                INSERT OR REPLACE INTO pat_patients (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_hash, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@PatId, @Mrn, '女性患者', 'NXHZ', 'F', 19900101, 'ID_CARD', @Mrn, 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1)",
                new { PatId = femalePatId, Mrn = "MRN-F-" + tag, Now = now });
        }

        var admitOpposite = await _client.PostAsJsonAsync("/api/v4/ipd/admit", new
        {
            PatientId = femalePatId, CampusId = "CAMPUS-01", AdmitDeptId = "DEPT-IPD-01", AdmitWardId = "DEPT-WARD-02",
            IsBorrowedBed = false, BedCode = maleBedCode, PrepaymentAmount = 1000m
        });
        var admitOppositeEnv = await admitOpposite.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(409, admitOppositeEnv?.Code);

        var admitClosed = await _client.PostAsJsonAsync("/api/v4/ipd/admit", new
        {
            PatientId = femalePatId, CampusId = "CAMPUS-01", AdmitDeptId = "DEPT-IPD-01", AdmitWardId = "DEPT-WARD-02",
            IsBorrowedBed = false, BedCode = closedBedCode, PrepaymentAmount = 1000m
        });
        var admitClosedEnv = await admitClosed.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(409, admitClosedEnv?.Code);

        // 2. Schedule template with fee=0 auto-resolves RegistrationFee from mdm_reg_levels
        string doctorId;
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            doctorId = await conn.ExecuteScalarAsync<string>(
                "SELECT id FROM sys_users WHERE is_active = 1 AND id IN (SELECT user_id FROM sys_user_dept_posts WHERE dept_id = 'DEPT-OPD-01') LIMIT 1")
                ?? "USER-DOC-OPD";
        }
        var tplRes = await _client.PostAsJsonAsync("/api/v4/opd/schedule-templates",
            new OutpatientController.SaveScheduleTemplateRequestDto(
                TemplateName: "主数据号别联动-" + tag,
                OpdDeptId: "DEPT-OPD-01",
                ClinicRoomId: "ROOM-OPD-101",
                DoctorId: doctorId,
                WeekdayCodes: "1,2,3,4,5",
                SlotPeriodCode: "MORNING",
                StartMinute: 480,
                EndMinute: 720,
                RegLevelCode: "EXPERT",
                RegistrationFee: 0m,
                TotalCapacity: 10,
                IsActive: 1,
                CampusId: "CAMPUS-01",
                SlotIntervalMinutes: 30));
        var tplEnv = await tplRes.Content.ReadFromJsonAsync<ApiResponse<OpdScheduleTemplate>>();
        Assert.Equal(200, tplEnv?.Code);
        Assert.Equal(50.00m, tplEnv!.Data!.RegistrationFee);
    }

    [Fact]
    public async Task MasterData_Unified_Upsert_And_Toggle_Works_Across_Domains()
    {
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // Test create -> in-place update -> toggle off -> toggle on across Phase 1 & Phase 2 domains
        var cases = new[]
        {
            (DomainKey: "yb_diseases", Code: $"M99{tag[..3]}", Name: $"慢病测试-{tag}", StdCode: "CHRONIC_SPECIAL", Cat: "3500", Extra: ""),
            (DomainKey: "departments", Code: $"DPT-{tag}", Name: $"测试科室-{tag}", StdCode: "0301", Cat: "CLINICAL", Extra: $"CC-{tag}"),
            (DomainKey: "vendors", Code: $"VND-{tag}", Name: $"测试供应商-{tag}", StdCode: "91110000100000001X", Cat: "DRUG_DISTRIBUTOR", Extra: "GSP-2026"),
            (DomainKey: "tcm", Code: $"B99{tag[..3]}", Name: $"中医测试病名-{tag}", StdCode: "TCM-GB95", Cat: "DISEASE", Extra: "ZYCS"),
            (DomainKey: "icd9", Code: $"99.{tag[..2]}01", Name: $"测试微创手术-{tag}", StdCode: "ICD9-CM3-YB", Cat: "2", Extra: "I"),
            (DomainKey: "locations", Code: $"LOC-{tag}", Name: $"诊室-{tag}", StdCode: "ROOM", Cat: $"SCR-{tag}", Extra: ""),
            (DomainKey: "or_rooms", Code: $"OR-{tag}", Name: $"洁净手术间-{tag}", StdCode: "百级", Cat: "全麻", Extra: "I类"),
            (DomainKey: "ca_certs", Code: $"CA-{tag}", Name: $"国密CA-{tag}", StdCode: "DOC-001", Cat: "", Extra: $"/seals/{tag}.png")
        };

        foreach (var c in cases)
        {
            var createRes = await _client.PostAsJsonAsync("/api/v4/mdm/upsert", new
            {
                c.DomainKey,
                Id = (string?)null,
                c.Code,
                c.Name,
                c.StdCode,
                CategoryOrSpec = c.Cat,
                ExtraAttributes = c.Extra
            });
            var createRaw = await createRes.Content.ReadAsStringAsync();
            Assert.True(createRes.IsSuccessStatusCode, $"HTTP {(int)createRes.StatusCode} for {c.DomainKey}: {createRaw}");
            var createEnv = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<System.Text.Json.JsonElement>>(createRaw, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.True(createEnv?.Code == 200, $"Create failed for {c.DomainKey}: {createEnv?.Message} | {createRaw}");
            string id = createEnv!.Data.GetProperty("id").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(id));

            var updateRes = await _client.PostAsJsonAsync("/api/v4/mdm/upsert", new
            {
                c.DomainKey,
                Id = id,
                c.Code,
                Name = c.Name + "-已修改",
                c.StdCode,
                CategoryOrSpec = c.Cat,
                ExtraAttributes = c.Extra
            });
            var updateEnv = await updateRes.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>();
            Assert.True(updateEnv?.Code == 200, $"Update failed for {c.DomainKey}: {updateEnv?.Message}");
            Assert.Equal(id, updateEnv!.Data.GetProperty("id").GetString());
            Assert.True(updateEnv.Data.GetProperty("updated").GetBoolean());

            var toggleOff = await _client.PostAsJsonAsync("/api/v4/mdm/toggle", new { c.DomainKey, Id = id });
            var offEnv = await toggleOff.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>();
            Assert.True(offEnv?.Code == 200, $"Toggle off failed for {c.DomainKey}: {offEnv?.Message}");

            var toggleOn = await _client.PostAsJsonAsync("/api/v4/mdm/toggle", new { c.DomainKey, Id = id });
            var onEnv = await toggleOn.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>();
            Assert.True(onEnv?.Code == 200, $"Toggle on failed for {c.DomainKey}: {onEnv?.Message}");
        }

        // Verify persisted name and restored active/valid state in DB
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var deptName = await conn.ExecuteScalarAsync<string>("SELECT dept_name FROM sys_departments WHERE dept_code = @Code", new { Code = $"DPT-{tag}" });
        var deptActive = await conn.ExecuteScalarAsync<int>("SELECT is_active FROM sys_departments WHERE dept_code = @Code", new { Code = $"DPT-{tag}" });
        Assert.Equal($"测试科室-{tag}-已修改", deptName);
        Assert.Equal(1, deptActive);

        var caStatus = await conn.ExecuteScalarAsync<string>("SELECT revoke_status FROM mdm_ca_certificates WHERE cert_serial = @Code", new { Code = $"CA-{tag}" });
        Assert.Equal("VALID", caStatus);
    }

    [Fact]
    public async Task All_Business_Modules_Link_To_Master_Data_Without_Split()
    {
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // 1. UserAdmin -> HR -> MDM Staff -> SysUsers bidirectional sync
        var createUserRes = await _client.PostAsJsonAsync("/api/v4/platform/users", new
        {
            UserName = $"u_{tag.ToLowerInvariant()}",
            DisplayName = $"同步医师-{tag}",
            EmployeeNo = $"EMP-{tag}",
            Password = "Password@123",
            DeptId = "DEPT-OPD-01",
            RoleId = "ROLE-OPD-DOC",
            PrescribeLevel = 2,
            SurgeryLevel = 1,
            EmrSignLevel = 1
        });
        var createUserEnv = await createUserRes.Content.ReadFromJsonAsync<ApiResponse<string>>();
        Assert.Equal(200, createUserEnv?.Code);
        string userId = createUserEnv!.Data!;

        string? empId;
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            empId = await conn.ExecuteScalarAsync<string?>("SELECT id FROM hr_employees WHERE employee_no = @EmpNo", new { EmpNo = $"EMP-{tag}" });
            Assert.False(string.IsNullOrWhiteSpace(empId));
        }

        // MDM staff upsert + toggle must sync sys_users.display_name and sys_users.is_active
        var staffUpsert = await _client.PostAsJsonAsync("/api/v4/mdm/upsert", new
        {
            DomainKey = "staff",
            Id = empId,
            Code = $"EMP-{tag}",
            Name = $"主数据改名-{tag}",
            StdCode = $"YB-{tag}",
            CategoryOrSpec = "DOCTOR",
            ExtraAttributes = $"CERT-{tag}"
        });
        Assert.Equal(200, (await staffUpsert.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>())?.Code);

        var staffToggle = await _client.PostAsJsonAsync("/api/v4/mdm/toggle", new { DomainKey = "staff", Id = empId });
        Assert.Equal(200, (await staffToggle.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>())?.Code);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var userRow = await conn.QuerySingleAsync<dynamic>("SELECT display_name AS DisplayName, is_active AS IsActive FROM sys_users WHERE id = @Id", new { Id = userId });
            Assert.Equal($"主数据改名-{tag}", (string)userRow.DisplayName);
            Assert.Equal(0, Convert.ToInt32(userRow.IsActive));
        }

        // 2. Inactive mdm_or_rooms blocks AIMS emergency insert
        var orUpsert = await _client.PostAsJsonAsync("/api/v4/mdm/upsert", new
        {
            DomainKey = "or_rooms",
            Id = (string?)null,
            Code = $"OR-BLK-{tag}",
            Name = "停用手术间",
            StdCode = "百级",
            CategoryOrSpec = "全麻",
            ExtraAttributes = "I类"
        });
        var orId = (await orUpsert.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>())!.Data.GetProperty("id").GetString()!;
        await _client.PostAsJsonAsync("/api/v4/mdm/toggle", new { DomainKey = "or_rooms", Id = orId });

        var aimsBlocked = await _client.PostAsJsonAsync("/api/v4/aims/or-board/emergency-insert", new
        {
            EncounterId = $"ENC-{tag}",
            OrRoomCode = $"OR-BLK-{tag}",
            SurgeryCode = "EMG_SURG",
            SurgeryName = "急诊测试手术",
            SurgeryLevel = 2,
            SurgeonName = "测试主刀",
            AnesthesiologistName = "测试麻醉"
        });
        Assert.Equal(409, (await aimsBlocked.Content.ReadFromJsonAsync<ApiResponse<object>>())?.Code);

        // 3. Inactive cfg_tcm_diagnoses blocks TCM prescription
        var tcmUpsert = await _client.PostAsJsonAsync("/api/v4/mdm/upsert", new
        {
            DomainKey = "tcm",
            Id = (string?)null,
            Code = $"TCM-B-{tag}",
            Name = $"停用中医病名-{tag}",
            StdCode = "TCM-GB95",
            CategoryOrSpec = "DISEASE",
            ExtraAttributes = "TYZY"
        });
        var tcmId = (await tcmUpsert.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>())!.Data.GetProperty("id").GetString()!;
        await _client.PostAsJsonAsync("/api/v4/mdm/toggle", new { DomainKey = "tcm", Id = tcmId });

        var tcmBlocked = await _client.PostAsJsonAsync("/api/v4/opd/tcm/prescriptions", new
        {
            VisitId = "V-TEST",
            PatientId = "P-TEST",
            TcmDiagnosis = $"TCM-B-{tag}",
            TcmSyndrome = "气虚证",
            DoseCount = 3,
            UsageRoute = "水煎服",
            Frequency = "每日1剂",
            IsDecoctionRequired = true,
            DecoctionPackCount = 6,
            DecoctionUnitFee = 5m,
            Items = new[] { new { HerbId = "HERB-01", HerbName = "黄芪", SingleDoseG = 15m, UnitPricePerG = 0.2m } }
        });
        Assert.Equal(409, (await tcmBlocked.Content.ReadFromJsonAsync<ApiResponse<object>>())?.Code);

        // 4. Inactive mdm_accounting_subjects blocks CostCenter allocation
        var accUpsert = await _client.PostAsJsonAsync("/api/v4/mdm/upsert", new
        {
            DomainKey = "acc_subjects",
            Id = (string?)null,
            Code = $"SUBJ-{tag}",
            Name = "停用核算科目",
            StdCode = "EXPENSE",
            CategoryOrSpec = "CLINICAL",
            ExtraAttributes = "0.5"
        });
        var accId = (await accUpsert.Content.ReadFromJsonAsync<ApiResponse<System.Text.Json.JsonElement>>())!.Data.GetProperty("id").GetString()!;
        await _client.PostAsJsonAsync("/api/v4/mdm/toggle", new { DomainKey = "acc_subjects", Id = accId });

        var ccBlocked = await _client.PostAsJsonAsync("/api/v4/finance/cost-centers/allocate", new
        {
            DeptId = "DEPT-OPD-01",
            Period = "2026-11",
            DirectCost = 1000m,
            AllocatedCost = 0m,
            Revenue = 2000m,
            AllocationMethod = $"SUBJ-{tag}"
        });
        Assert.Equal(409, (await ccBlocked.Content.ReadFromJsonAsync<ApiResponse<object>>())?.Code);
    }
}
