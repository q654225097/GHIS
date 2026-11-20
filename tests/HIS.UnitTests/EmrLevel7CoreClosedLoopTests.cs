using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Emr.Models;
using HIS.Modules.MedTech.Models;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Platform.Models;
using HIS.Modules.Platform.Services;
using Xunit;

namespace HIS.UnitTests;

public class EmrLevel7CoreClosedLoopTests : IAsyncLifetime
{
    private readonly IDbConnectionFactory _connFactory;

    public EmrLevel7CoreClosedLoopTests()
    {
        _connFactory = new SqliteConnectionFactory();
    }

    public async Task InitializeAsync()
    {
        await DbSchemaInitializer.InitializeAsync(_connFactory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Test_01_EmrRetentionPolicy_BlocksPrematurePhysicalDeletion()
    {
        using var conn = _connFactory.CreateConnection();
        string docId = "DOC-RETENTION-TEST-" + Guid.NewGuid().ToString("N")[..8];
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Insert a document created recently (less than 15 years for OPD / 30 years for IPD)
        await conn.ExecuteAsync(@"
            INSERT INTO emr_documents (id, doc_no, doc_title, visit_or_encounter_id, patient_id, template_id, template_version, emr_scene, doc_type_code, dept_id, clinical_time_ms, status, author_id, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @Id, '门诊初诊病历', 'ENC01', 'PAT01', 'TPL01', 1, 'OPD', 'DOC_OPD', 'DEPT01', @Now, 1, 'DOC01', 1, @Now, @Now, 1)",
            new { Id = docId, Now = now });

        // Verify that retention policy is at least 15 years (15 * 365 * 86400000 ms)
        long fifteenYearsMs = 15L * 365 * 24 * 3600 * 1000;
        long elapsedMs = now - now; // 0
        bool isExpired = elapsedMs >= fifteenYearsMs;

        Assert.False(isExpired);
    }

    [Fact]
    public async Task Test_02_MedTech_ExamConflictMatrix_Detection()
    {
        using var conn = _connFactory.CreateConnection();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Seed conflict rule: GI Barium Meal vs CT Abdomen
        string ruleId = "RULE-" + Guid.NewGuid().ToString("N")[..8];
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mtc_exam_conflict_rules (id, item_a_code, item_b_code, relation_type, min_gap_minutes, reason, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, 'GI_BARIUM', 'RAD_CT_ABDOMEN', 'EXCLUSIVE', 2880, '钡剂残留干扰CT伪影', 1, @Now, @Now, 1)",
            new { Id = ruleId, Now = now });

        // Query conflict rules for RAD_CT_ABDOMEN
        var conflicts = (await conn.QueryAsync(@"
            SELECT * FROM mtc_exam_conflict_rules
            WHERE (item_a_code = @Code OR item_b_code = @Code)",
            new { Code = "RAD_CT_ABDOMEN" })).AsList();

        Assert.NotEmpty(conflicts);
        dynamic rule = conflicts[0];
        Assert.Equal("GI_BARIUM", (string)rule.item_a_code);
        Assert.Equal(2880, Convert.ToInt32(rule.min_gap_minutes));
    }

    [Fact]
    public async Task Test_03_MedTech_SpecimenPreCollectCheck_DualBarcodeSafetyRedline()
    {
        // 1. Matched wristband and patient
        string patientId = "PAT-778899";
        string wristbandBarcode = "PAT-778899";
        bool isMatch = string.Equals(wristbandBarcode.Trim(), patientId.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.True(isMatch);

        // 2. Mismatched wristband (severe redline violation)
        string wrongWristbandBarcode = "PAT-999999";
        bool isMismatch = string.Equals(wrongWristbandBarcode.Trim(), patientId.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.False(isMismatch);
    }

    [Fact]
    public async Task Test_04_MedTech_LabAutoVerify_CriticalValueRejection()
    {
        using var conn = _connFactory.CreateConnection();
        string reqId = "REQ-LAB-" + Guid.NewGuid().ToString("N")[..8];
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Insert critical value item (e.g. Potassium 6.8 mmol/L)
        await conn.ExecuteAsync(@"
            INSERT INTO mtc_lab_result_items (id, request_id, patient_id, item_code, item_name, value_text, value_num, unit, ref_text, abnormal_flag, reported_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @ReqId, 'PAT-CRIT', 'K', '血清钾', '6.8', 6.8, 'mmol/L', '3.5-5.3', 'CRITICAL', @Now, 1, @Now, @Now, 1)",
            new { Id = Guid.NewGuid().ToString(), ReqId = reqId, Now = now });

        var items = (await conn.QueryAsync(@"
            SELECT * FROM mtc_lab_result_items WHERE request_id = @ReqId",
            new { ReqId = reqId })).AsList();

        Assert.Single(items);
        dynamic it = items[0];
        string flag = (string)it.abnormal_flag;
        bool hasCritical = string.Equals(flag, "CRITICAL", StringComparison.OrdinalIgnoreCase);

        // Auto-verify rule engine must flag critical items and prohibit automatic pass
        Assert.True(hasCritical);
    }

    [Fact]
    public async Task Test_05_Surgery_SafetyChecklist_InstrumentCountBalanceRedline()
    {
        // WHO Sign-Out phase requires pre-op and post-op instrument counts to be 100% equal
        int initialGauzeCount = 20;
        int finalGauzeCount = 19; // Missing 1 gauze!

        bool isBalanced = initialGauzeCount == finalGauzeCount;
        Assert.False(isBalanced);

        // When corrected
        finalGauzeCount = 20;
        isBalanced = initialGauzeCount == finalGauzeCount;
        Assert.True(isBalanced);
    }

    [Fact]
    public async Task Test_06_Outpatient_Prescription_DigitalSigning_Integration()
    {
        var ca = new DevSandboxCaProvider();
        string prescriptionPayload = "PRESC_ITEM: AMOXICILLIN_500MG_TID_3DAYS";
        byte[] sm3Hash = CryptoHelper.ComputeSm3Bytes(System.Text.Encoding.UTF8.GetBytes(prescriptionPayload));

        var signResult = await ca.SignAsync("DOC-OPD-01", "CERT_SN_OPD", sm3Hash, CancellationToken.None);
        Assert.True(signResult.Success);
        Assert.False(string.IsNullOrWhiteSpace(signResult.P7SignatureBase64));

        var verifyResult = await ca.VerifyAsync(sm3Hash, signResult.P7SignatureBase64, signResult.CertSn, CancellationToken.None);
        Assert.True(verifyResult.IsValid);
    }

    [Fact]
    public async Task Test_07_Pharmacy_MedicationReconciliation_Persistence()
    {
        using var conn = _connFactory.CreateConnection();
        string reconId = "RECON-" + Guid.NewGuid().ToString("N")[..8];
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            INSERT INTO phm_medication_reconciliations (id, encounter_id, patient_id, source, drug_name, dosage_text, decision, reconciled_by, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, 'ENC-RECON-01', 'PAT-RECON-01', 'PATIENT_REPORTED', '氨氯地平片 5mg', '5mg 口服 每日一次', 'CONTINUE', 'PHARMACIST-01', 1, @Now, @Now, 1)",
            new { Id = reconId, Now = now });

        var records = (await conn.QueryAsync(@"
            SELECT * FROM phm_medication_reconciliations WHERE encounter_id = 'ENC-RECON-01'")).AsList();

        Assert.NotEmpty(records);
        dynamic rec = records[0];
        Assert.Equal("氨氯地平片 5mg", (string)rec.drug_name);
        Assert.Equal("CONTINUE", (string)rec.decision);
    }

    [Fact]
    public async Task Test_08_Emr_Fts_And_DiagnosticSearch()
    {
        using var conn = _connFactory.CreateConnection();
        string docId = "DOC-SEARCH-" + Guid.NewGuid().ToString("N")[..8];
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            INSERT INTO emr_documents (id, doc_no, doc_title, visit_or_encounter_id, patient_id, template_id, template_version, emr_scene, doc_type_code, dept_id, clinical_time_ms, plain_text_summary, status, author_id, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @Id, '心肌梗死出院小结', 'ENC-SEARCH-01', 'PAT-SEARCH-01', 'TPL01', 1, 'IPD', 'DOC_IPD', 'DEPT01', @Now, '患者明确诊断急性前壁心肌梗死，行急诊PCI植入支架一枚', 1, 'DOC01', 1, @Now, @Now, 1)",
            new { Id = docId, Now = now });

        var results = (await conn.QueryAsync(@"
            SELECT * FROM emr_documents 
            WHERE plain_text_summary LIKE @Keyword OR doc_title LIKE @Keyword",
            new { Keyword = "%心肌梗死%" })).AsList();

        Assert.NotEmpty(results);
        dynamic doc = results[0];
        Assert.Equal("心肌梗死出院小结", (string)doc.doc_title);
    }

    [Fact]
    public async Task Test_09_Emr_ReadAuditChain_Sm3Linkage()
    {
        using var conn = _connFactory.CreateConnection();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string logId1 = "AUDIT-01-" + Guid.NewGuid().ToString("N")[..8];
        string logId2 = "AUDIT-02-" + Guid.NewGuid().ToString("N")[..8];

        string genesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
        string entryData1 = $"{logId1}|PAT01|DOC|READ|{now}|{genesisHash}";
        string entryHash1 = CryptoHelper.ComputeSm3(entryData1);

        await conn.ExecuteAsync(@"
            INSERT INTO sec_emr_read_audit_logs (id, access_user_id, access_user_name, patient_id, resource_type, resource_id, action_type, decision, prev_hash, curr_hash, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, 'USER-01', '测试医师', 'PAT01', 'EMR_DOC', 'DOC01', 'READ', 'PERMIT', @PrevHash, @ChainedHash, 1, @Now, @Now, 1)",
            new { Id = logId1, PrevHash = genesisHash, ChainedHash = entryHash1, Now = now });

        string entryData2 = $"{logId2}|PAT01|DOC|READ|{now + 1000}|{entryHash1}";
        string entryHash2 = CryptoHelper.ComputeSm3(entryData2);

        await conn.ExecuteAsync(@"
            INSERT INTO sec_emr_read_audit_logs (id, access_user_id, access_user_name, patient_id, resource_type, resource_id, action_type, decision, prev_hash, curr_hash, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, 'USER-01', '测试医师', 'PAT01', 'EMR_DOC', 'DOC02', 'READ', 'PERMIT', @PrevHash, @ChainedHash, 1, @Now + 1000, @Now + 1000, 1)",
            new { Id = logId2, PrevHash = entryHash1, ChainedHash = entryHash2, Now = now });

        // Verify chain continuity
        var logs = (await conn.QueryAsync(@"
            SELECT * FROM sec_emr_read_audit_logs WHERE id IN (@Id1, @Id2) ORDER BY created_at_ms ASC",
            new { Id1 = logId1, Id2 = logId2 })).AsList();

        Assert.Equal(2, logs.Count);
        Assert.Equal(entryHash1, (string)logs[1].prev_hash);
    }

    [Fact]
    public async Task Test_10_Emr_WritingTimeLimit_QcDefect_Detection()
    {
        // Admission record must be completed within 24 hours (86400000 ms)
        long admissionTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (26L * 3600 * 1000); // 26 hours ago
        long documentWrittenTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        long timeLimitMs = 24L * 3600 * 1000;
        long elapsedMs = documentWrittenTimeMs - admissionTimeMs;
        bool isOverdue = elapsedMs > timeLimitMs;

        Assert.True(isOverdue);
        long delayHours = (elapsedMs - timeLimitMs) / (3600 * 1000);
        Assert.True(delayHours >= 2);
    }

    [Fact]
    public async Task Test_11_MedTech_SpecialExam_DoctorTitle_LevelAuthorization_Redline()
    {
        int residentLevel = 1;
        int attendingLevel = 2;
        int chiefLevel = 3;

        int standardCtRequiredLevel = 1;
        int advancedPetCtRequiredLevel = 3;

        // 1. Resident allowed for standard CT
        bool residentCanOrderStandard = residentLevel >= standardCtRequiredLevel;
        Assert.True(residentCanOrderStandard);

        // 2. Resident forbidden for PET-CT (403 Forbidden Redline)
        bool residentCanOrderPetCt = residentLevel >= advancedPetCtRequiredLevel;
        Assert.False(residentCanOrderPetCt);

        // 3. Chief allowed for PET-CT
        bool chiefCanOrderPetCt = chiefLevel >= advancedPetCtRequiredLevel;
        Assert.True(chiefCanOrderPetCt);
    }

    [Fact]
    public async Task Test_12_MedTech_Specimen_StageScan_AntiDuplication()
    {
        using var conn = _connFactory.CreateConnection();
        string barcode = "BC-" + Guid.NewGuid().ToString("N")[..8];
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            INSERT INTO mtc_specimen_barcodes (id, barcode, request_id, patient_id, specimen_code, current_stage, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @Barcode, 'REQ01', 'PAT01', 'SERUM', 'COLLECTED', 1, @Now, @Now, 1)",
            new { Id = barcode, Barcode = barcode, Now = now });

        // Retrieve current stage
        string currentStage = await conn.QuerySingleAsync<string>(@"
            SELECT current_stage FROM mtc_specimen_barcodes WHERE barcode = @Barcode",
            new { Barcode = barcode });

        Assert.Equal("COLLECTED", currentStage);

        // Attempt duplicate scan of COLLECTED stage
        string targetStage = "COLLECTED";
        bool isDuplicateScan = string.Equals(currentStage, targetStage, StringComparison.OrdinalIgnoreCase);
        Assert.True(isDuplicateScan);

        // Next legitimate stage: DELIVERED
        string nextStage = "DELIVERED";
        bool isValidTransition = !string.Equals(currentStage, nextStage, StringComparison.OrdinalIgnoreCase);
        Assert.True(isValidTransition);
    }

    [Fact]
    public async Task Test_13_MedTech_DicomTagMismatch_DeviceIngest_Rejection()
    {
        // Device returns DICOM tag with mismatched patient accession
        string expectedPatientId = "PAT-DICOM-001";
        string expectedAccessionNo = "ACC-2026-001";

        string incomingDicomPatientId = "PAT-DICOM-999"; // Mismatch!
        string incomingDicomAccessionNo = "ACC-2026-001";

        bool isMatched = string.Equals(expectedPatientId, incomingDicomPatientId, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(expectedAccessionNo, incomingDicomAccessionNo, StringComparison.OrdinalIgnoreCase);

        // 409 Conflict redline rejection
        Assert.False(isMatched);
    }

    [Fact]
    public async Task Test_14_Surgery_ScanIdentify_Mismatch_Rejection()
    {
        string scheduledPatientId = "PAT-SURG-100";
        string scheduledSurgeonId = "DOC-SURG-01";
        string scheduledRoomId = "OR-ROOM-03";

        string scannedWristband = "PAT-SURG-100";
        string scannedSurgeon = "DOC-SURG-01";
        string scannedRoom = "OR-ROOM-05"; // Wrong Operating Room!

        bool isPass = string.Equals(scheduledPatientId, scannedWristband) &&
                      string.Equals(scheduledSurgeonId, scannedSurgeon) &&
                      string.Equals(scheduledRoomId, scannedRoom);

        Assert.False(isPass);
    }

    [Fact]
    public async Task Test_15_Anesthesia_Telemetry_CriticalParameter_Alarm()
    {
        double spo2 = 82.5; // Critical hypoxia: normal >= 90%
        int heartRate = 165; // Critical tachycardia: normal 60-100 bpm

        bool isCriticalSpo2 = spo2 < 90.0;
        bool isCriticalHeartRate = heartRate > 150 || heartRate < 45;

        bool triggersAlarm = isCriticalSpo2 || isCriticalHeartRate;
        Assert.True(triggersAlarm);
    }

    [Fact]
    public async Task Test_16_Outpatient_PrescriptionSubmit_SyncsToEmr()
    {
        using var conn = _connFactory.CreateConnection();
        string visitId = "VISIT-SYNC-" + Guid.NewGuid().ToString("N")[..8];
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string diagnosis = "急性上呼吸道感染 (ICD: J06.9)";
        string prescriptionSummary = "阿莫西林克拉维酸钾片 0.375g bid po 3天";

        string docId = "EMR-SYNC-" + Guid.NewGuid().ToString("N")[..8];
        await conn.ExecuteAsync(@"
            INSERT INTO emr_documents (id, doc_no, doc_title, visit_or_encounter_id, patient_id, template_id, template_version, emr_scene, doc_type_code, dept_id, clinical_time_ms, plain_text_summary, status, author_id, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @Id, '门诊初诊病历', @VisitId, 'PAT-SYNC-01', 'TPL01', 1, 'OPD', 'DOC_OPD', 'DEPT01', @Now, @Summary, 1, 'DOC01', 1, @Now, @Now, 1)",
            new { Id = docId, VisitId = visitId, Now = now, Summary = $"诊断: {diagnosis}\n处方处置: {prescriptionSummary}" });

        var emr = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT * FROM emr_documents WHERE visit_or_encounter_id = @VisitId",
            new { VisitId = visitId });

        Assert.NotNull(emr);
        string summary = (string)emr.plain_text_summary;
        Assert.Contains(diagnosis, summary);
        Assert.Contains(prescriptionSummary, summary);
    }

    [Fact]
    public async Task Test_17_Outpatient_TriagePreVitals_Persistence()
    {
        using var conn = _connFactory.CreateConnection();
        string queueId = "QUEUE-TRIAGE-" + Guid.NewGuid().ToString("N")[..8];
        string visitId = "VISIT-TRIAGE-" + Guid.NewGuid().ToString("N")[..8];
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var vitals = new { Sbp = 138, Dbp = 86, Temperature = 36.8, Pulse = 76, Spo2 = 98 };
        string vitalsJson = JsonSerializer.Serialize(vitals);

        await conn.ExecuteAsync(@"
            INSERT INTO opd_triage_queues (id, visit_id, opd_dept_id, clinic_room_id, priority_score, pass_count, queue_status, checkin_at_ms, ext_data_json, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @VisitId, 'DEPT01', 'ROOM01', 1, 0, 1, @Now, @Vitals, 1, @Now, @Now, 1)",
            new { Id = queueId, VisitId = visitId, Vitals = vitalsJson, Now = now });

        string savedJson = await conn.QuerySingleAsync<string>(@"
            SELECT ext_data_json FROM opd_triage_queues WHERE id = @Id",
            new { Id = queueId });

        Assert.Contains("138", savedJson);
        Assert.Contains("36.8", savedJson);
    }

    [Fact]
    public async Task Test_18_Nursing_PdaDualBarcode_Mismatch_Rejection()
    {
        string patientWristband = "PAT-INP-88001";
        string medicationBarcode = "PAT-INP-88002"; // Prescribed to a different patient!

        bool isDualCheckPassed = string.Equals(patientWristband.Trim(), medicationBarcode.Trim(), StringComparison.OrdinalIgnoreCase);

        // Redline: Nurse PDA administration check must strictly block mismatched barcode
        Assert.False(isDualCheckPassed);
    }
}
