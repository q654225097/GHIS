using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 角色三（门诊输液 + 住院病区护士）安全闭环控制器：
/// 1) 皮试 20 分钟倒计时与双人独立判定（严禁同人复核；阳性自动同步回写 pat_patients.allergy_history_text 阻断同类药）
/// 2) 突发输液反应应急处置（立即停液 + 留样封存编码 + 原子写入药学部 adr_reports 不良反应台账）
/// 3) 住院口服药分时段单剂量 (UD) 领摆发与停药实物退药核销
/// 4) 床旁健康宣教签字留痕
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "WARD_NURSE", "PHARMACIST", "IPD_ATTENDING", "IPD_RESIDENT")]
[Route("api/v4/nurse-closure")]
public sealed class NurseSafetyClosureController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public NurseSafetyClosureController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record InjectSkinTestDto(string EncounterOrVisitId, string PatientId, string PatientName, string DrugCode, string DrugName, string? BatchNo, int? WaitMinutes);
    public sealed record JudgeSkinTestDto(string Result, string ReaderName, string CheckerName, string? Note);
    public sealed record EmergencyStopInfusionDto(string InfusionOrderId, string PatientId, string PatientName, string DrugName, string? BatchNo, decimal RemainingMl, string Symptoms, string? RescueAction, string? NotifiedDoctor);
    public sealed record PackUdDto(string EncounterId, string PatientId, string PatientName, string WardName, string? BedCode, int TakeDateInt, string TakeSlot, string DrugSummary);
    public sealed record ReturnUdDto(string Reason);
    public sealed record BedsideEducationDto(string EncounterId, string PatientId, string PatientName, string EduStage, string EduContent, string? MasteryLevel);

    [HttpGet("skin-tests")]
    public async Task<ApiResponse<object>> ListSkinTests([FromQuery] string? result = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM nur_skin_test_records WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(result))
        {
            sql += " AND result = @Res";
            p.Add("Res", result.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY due_at_ms ASC LIMIT 200";
        return ApiResponse<object>.Success((await conn.QueryAsync(sql, p)).ToList());
    }

    [HttpPost("skin-tests/inject")]
    public async Task<ApiResponse<object>> InjectSkinTest([FromBody] InjectSkinTestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.DrugName))
            return ApiResponse<object>.Fail(400, "患者ID与皮试药品名称不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int waitMin = dto.WaitMinutes is > 0 and <= 60 ? dto.WaitMinutes.Value : 20;
        long dueAt = now + (waitMin * 60_000L);
        string id = Guid.NewGuid().ToString("N");
        string testNo = $"ST{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO nur_skin_test_records (
    id, test_no, encounter_or_visit_id, patient_id, patient_name, drug_code, drug_name, batch_no,
    injected_at_ms, due_at_ms, injector_name, result, allergy_synced, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @EncId, @PatId, @PatName, @Code, @Drug, @Batch,
    @Now, @Due, @Inj, 'PENDING', 0, @Now, @Now
);", new
        {
            Id = id, No = testNo, EncId = dto.EncounterOrVisitId.Trim(), PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(), Code = dto.DrugCode.Trim().ToUpperInvariant(),
            Drug = dto.DrugName.Trim(), Batch = dto.BatchNo, Now = now, Due = dueAt, Inj = ActorName
        });

        return ApiResponse<object>.Success(new { id, testNo, waitMinutes = waitMin, dueAtMs = dueAt, result = "PENDING" });
    }

    [HttpPost("skin-tests/{id}/judge")]
    public async Task<ApiResponse<object>> JudgeSkinTest(string id, [FromBody] JudgeSkinTestDto dto, CancellationToken ct)
    {
        string res = (dto.Result ?? string.Empty).Trim().ToUpperInvariant();
        if (res is not ("NEGATIVE" or "POSITIVE"))
            return ApiResponse<object>.Fail(400, "皮试判定结果仅支持 NEGATIVE(阴性) 或 POSITIVE(阳性)。");
        if (string.IsNullOrWhiteSpace(dto.ReaderName) || string.IsNullOrWhiteSpace(dto.CheckerName))
            return ApiResponse<object>.Fail(400, "皮试结果必须双人核对，判定人与复核人姓名不能为空。");
        if (string.Equals(dto.ReaderName.Trim(), dto.CheckerName.Trim(), StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.Fail(400, "严格执行皮试双人独立判定制度：判定人与复核人不能为同一人！");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var rec = await conn.QueryFirstOrDefaultAsync("SELECT * FROM nur_skin_test_records WHERE id = @Id", new { Id = id });
        if (rec == null) return ApiResponse<object>.Fail(404, "皮试记录不存在。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool isPositive = res == "POSITIVE";

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(@"
UPDATE nur_skin_test_records
SET result = @Res, reader_name = @Reader, checker_name = @Checker, read_at_ms = @Now,
    allergy_synced = @Synced, note = @Note, updated_at_ms = @Now
WHERE id = @Id;", new
        {
            Res = res, Reader = dto.ReaderName.Trim(), Checker = dto.CheckerName.Trim(),
            Now = now, Synced = isPositive ? 1 : 0, Note = dto.Note, Id = id
        }, tx);

        // 阳性自动同步写入患者主索引过敏史 (pat_patients.allergy_history_text)
        if (isPositive)
        {
            string patId = (string)rec.patient_id;
            string drugName = (string)rec.drug_name;
            string existing = await conn.ExecuteScalarAsync<string?>(
                "SELECT allergy_history_text FROM pat_patients WHERE id = @Pid", new { Pid = patId }, tx) ?? string.Empty;
            string addition = $"{drugName}(皮试阳性-{DateTime.UtcNow:yyyy-MM-dd})";
            string merged = string.IsNullOrWhiteSpace(existing) ? addition : $"{existing}; {addition}";
            await conn.ExecuteAsync(
                "UPDATE pat_patients SET allergy_history_text = @Allergy, updated_at_ms = @Now WHERE id = @Pid",
                new { Allergy = merged, Now = now, Pid = patId }, tx);
        }

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new
        {
            id,
            result = res,
            allergySynced = isPositive,
            message = isPositive ? "皮试阳性已双签确认，并自动写入患者主索引过敏史阻断同类用药。" : "皮试阴性已双签确认，准予用药。"
        });
    }

    [HttpGet("infusion-reactions")]
    public async Task<ApiResponse<object>> ListInfusionReactions(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        return ApiResponse<object>.Success((await conn.QueryAsync("SELECT * FROM nur_infusion_reactions ORDER BY stopped_at_ms DESC LIMIT 200")).ToList());
    }

    [HttpPost("infusion-reactions/emergency-stop")]
    public async Task<ApiResponse<object>> EmergencyStopInfusion([FromBody] EmergencyStopInfusionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.InfusionOrderId) || string.IsNullOrWhiteSpace(dto.Symptoms))
            return ApiResponse<object>.Fail(400, "输液单ID与不良反应临床症状不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string reactId = Guid.NewGuid().ToString("N");
        string reactNo = $"IR{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";
        string sealCode = $"SEAL-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        string adrId = Guid.NewGuid().ToString("N");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) 同步中止门诊输液单（若存在）
        await conn.ExecuteAsync(
            "UPDATE opd_infusion_orders SET status = 'STOPPED_REACTION', updated_at_ms = @Now WHERE id = @Id",
            new { Now = now, Id = dto.InfusionOrderId.Trim() }, tx);

        // 2) 自动联动写入药学部 ADR 不良反应监测表 (adr_reports)
        await conn.ExecuteAsync(@"
INSERT INTO adr_reports (
    id, patient_id, encounter_or_visit_id, order_or_rx_id, drug_id, drug_name, batch_no,
    reaction_name, onset_time_ms, severity, outcome, causality_grade, report_status,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @AdrId, @PatId, @InfId, @InfId, 'INFUSION', @Drug, @Batch,
    @Symptoms, @Now, 'SEVERE', 'UNDER_TREATMENT', 'PROBABLE', 'REPORTED',
    1, @Now, @Actor, @Now, @Actor, 1
);", new
        {
            AdrId = adrId, PatId = dto.PatientId.Trim(), InfId = dto.InfusionOrderId.Trim(),
            Drug = dto.DrugName.Trim(), Batch = dto.BatchNo ?? "UNKNOWN", Symptoms = dto.Symptoms.Trim(),
            Now = now, Actor = ActorId
        }, tx);

        // 3) 写入输液反应应急封存记录
        await conn.ExecuteAsync(@"
INSERT INTO nur_infusion_reactions (
    id, reaction_no, infusion_order_id, patient_id, patient_name, drug_name, batch_no,
    stopped_at_ms, remaining_ml, seal_code, symptoms, rescue_action, notified_doctor,
    linked_adr_id, reporter_name, status, created_at_ms
) VALUES (
    @Id, @No, @InfId, @PatId, @PatName, @Drug, @Batch,
    @Now, @RemMl, @Seal, @Symptoms, @Rescue, @Doc,
    @AdrId, @Reporter, 'SEALED_AND_REPORTED', @Now
);", new
        {
            Id = reactId, No = reactNo, InfId = dto.InfusionOrderId.Trim(), PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(), Drug = dto.DrugName.Trim(), Batch = dto.BatchNo,
            Now = now, RemMl = dto.RemainingMl, Seal = sealCode, Symptoms = dto.Symptoms.Trim(),
            Rescue = dto.RescueAction, Doc = dto.NotifiedDoctor, AdrId = adrId, Reporter = ActorName
        }, tx);

        await tx.CommitAsync(ct);

        return ApiResponse<object>.Success(new
        {
            reactionId = reactId,
            reactionNo = reactNo,
            sealCode,
            linkedAdrId = adrId,
            status = "SEALED_AND_REPORTED"
        }, "已紧急中止输液、生成封存编号并自动上报药学部 ADR。");
    }

    [HttpGet("ud-packages")]
    public async Task<ApiResponse<object>> ListUdPackages([FromQuery] string? wardName = null, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM phm_ward_ud_packages WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(wardName)) { sql += " AND ward_name = @W"; p.Add("W", wardName.Trim()); }
        if (!string.IsNullOrWhiteSpace(status)) { sql += " AND status = @S"; p.Add("S", status.Trim().ToUpperInvariant()); }
        sql += " ORDER BY take_date_int DESC, created_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success((await conn.QueryAsync(sql, p)).ToList());
    }

    [HttpPost("ud-packages/pack")]
    public async Task<ApiResponse<object>> PackUdOralMedication([FromBody] PackUdDto dto, CancellationToken ct)
    {
        string slot = (dto.TakeSlot ?? "MORNING").Trim().ToUpperInvariant();
        if (slot is not ("MORNING" or "NOON" or "EVENING" or "BEDTIME"))
            return ApiResponse<object>.Fail(400, "服药时段仅支持 MORNING/NOON/EVENING/BEDTIME。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        string barcode = $"UD-{dto.TakeDateInt}-{slot[..2]}-{id[..6].ToUpperInvariant()}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO phm_ward_ud_packages (
    id, package_barcode, encounter_id, patient_id, patient_name, ward_name, bed_code,
    take_date_int, take_slot, drug_summary, status, packed_by, created_at_ms, updated_at_ms
) VALUES (
    @Id, @Bc, @EncId, @PatId, @PatName, @Ward, @Bed,
    @DateInt, @Slot, @Summary, 'PACKED', @By, @Now, @Now
);", new
        {
            Id = id, Bc = barcode, EncId = dto.EncounterId.Trim(), PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(), Ward = dto.WardName.Trim(), Bed = dto.BedCode ?? "-",
            DateInt = dto.TakeDateInt, Slot = slot, Summary = dto.DrugSummary.Trim(), By = ActorName, Now = now
        });

        return ApiResponse<object>.Success(new { id, packageBarcode = barcode, takeSlot = slot, status = "PACKED" });
    }

    [HttpPost("ud-packages/{id}/ward-receive")]
    public async Task<ApiResponse<object>> WardReceiveUdPackage(string id, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        int n = await conn.ExecuteAsync(@"
UPDATE phm_ward_ud_packages
SET status = 'WARD_RECEIVED', ward_received_by = @By, ward_received_at_ms = @Now, updated_at_ms = @Now
WHERE id = @Id AND status = 'PACKED';", new { By = ActorName, Now = now, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(400, "仅待核收(PACKED)的单剂量口服药包允许病区核收。");
        return ApiResponse<object>.Success(new { id, status = "WARD_RECEIVED" });
    }

    [HttpPost("ud-packages/{id}/pda-administer")]
    public async Task<ApiResponse<object>> AdministerUdPackageByPda(string id, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        int n = await conn.ExecuteAsync(@"
UPDATE phm_ward_ud_packages
SET status = 'ADMINISTERED', administered_by = @By, administered_at_ms = @Now, updated_at_ms = @Now
WHERE id = @Id AND status = 'WARD_RECEIVED';", new { By = ActorName, Now = now, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(400, "仅已核收(WARD_RECEIVED)的药包允许床旁 PDA 扫码发药。");
        return ApiResponse<object>.Success(new { id, status = "ADMINISTERED" });
    }

    [HttpPost("ud-packages/{id}/return")]
    public async Task<ApiResponse<object>> ReturnUnusedUdPackage(string id, [FromBody] ReturnUdDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        int n = await conn.ExecuteAsync(@"
UPDATE phm_ward_ud_packages
SET status = 'RETURNED_TO_PHARMACY', returned_reason = @Reason, returned_at_ms = @Now, updated_at_ms = @Now
WHERE id = @Id AND status IN ('PACKED', 'WARD_RECEIVED');", new { Reason = dto.Reason, Now = now, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(400, "已服药或不存在的 UD 药包无法退药。");
        return ApiResponse<object>.Success(new { id, status = "RETURNED_TO_PHARMACY" });
    }

    [HttpPost("bedside-education")]
    public async Task<ApiResponse<object>> RecordBedsideEducation([FromBody] BedsideEducationDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO nur_bedside_education_logs (
    id, encounter_id, patient_id, patient_name, edu_stage, edu_content, mastery_level, educator_name, patient_acknowledged, educated_at_ms
) VALUES (
    @Id, @EncId, @PatId, @PatName, @Stage, @Content, @Mastery, @By, 1, @Now
);", new
        {
            Id = id, EncId = dto.EncounterId.Trim(), PatId = dto.PatientId.Trim(), PatName = dto.PatientName.Trim(),
            Stage = dto.EduStage.Trim().ToUpperInvariant(), Content = dto.EduContent.Trim(),
            Mastery = string.IsNullOrWhiteSpace(dto.MasteryLevel) ? "UNDERSTOOD" : dto.MasteryLevel.Trim().ToUpperInvariant(),
            By = ActorName, Now = now
        });
        return ApiResponse<object>.Success(new { id, educatedAtMs = now });
    }
}
