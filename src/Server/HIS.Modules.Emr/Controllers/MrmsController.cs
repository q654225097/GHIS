using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Emr.Controllers;

/// <summary>
/// 病案室管理与临床编码工作台 (MRMS)：出院病案回收、ICD-10/ICD-9-CM-3 编码复核、DRG 预分组、归档上架、依法封存与借阅复印登记
/// </summary>
[ApiController]
[Route("api/v4/mrms")]
public sealed class MrmsController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public MrmsController(
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record CreateCaseDto(string PatientName, string Gender, int Age, string DischargeDeptName, string PrimaryIcd10Code, string PrimaryIcd10Name);
    public sealed record UpdateCodingDto(string PrimaryIcd10Code, string PrimaryIcd10Name, string? PrimaryIcd9Code, string? PrimaryIcd9Name, string? DrgGroupCode, string Status, string? ShelfLocation, string? QcNote);
    public sealed record CreateBorrowDto(string CaseId, string ActionType, string ApplicantName, string Purpose, int DueDateInt);

    [HttpGet("cases")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetCases([FromQuery] string? status = null, [FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT id AS Id, case_no AS CaseNo, encounter_id AS EncounterId, patient_name AS PatientName,
                           gender AS Gender, age AS Age, discharge_dept_name AS DischargeDeptName,
                           discharge_date_int AS DischargeDateInt, primary_icd10_code AS PrimaryIcd10Code,
                           primary_icd10_name AS PrimaryIcd10Name, primary_icd9_code AS PrimaryIcd9Code,
                           primary_icd9_name AS PrimaryIcd9Name, drg_group_code AS DrgGroupCode,
                           status AS Status, shelf_location AS ShelfLocation, qc_note AS QcNote, coder_name AS CoderName
                    FROM mrms_case_records WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL") sql += " AND status = @St";
        if (!string.IsNullOrWhiteSpace(keyword)) sql += " AND (case_no LIKE @Kw OR patient_name LIKE @Kw OR primary_icd10_name LIKE @Kw)";
        sql += " ORDER BY updated_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { St = status?.Trim(), Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    /// <summary>
    /// 从已出院住院流水一键同步待回收病案
    /// </summary>
    [HttpPost("cases/sync-discharged")]
    public async Task<ApiResponse<object>> SyncDischargedEncounters()
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<dynamic>(@"
            SELECT e.id AS Id, e.admission_no AS AdmNo, COALESCE(p.full_name, e.patient_id) AS PatientName,
                   COALESCE(p.gender_code, '男') AS Gender, COALESCE(d.dept_name, e.admit_dept_id) AS DeptName
            FROM ipd_encounters e
            LEFT JOIN pat_patients p ON p.id = e.patient_id
            LEFT JOIN sys_departments d ON d.id = e.admit_dept_id
            WHERE e.status >= 2 AND NOT EXISTS (SELECT 1 FROM mrms_case_records m WHERE m.encounter_id = e.id)
            LIMIT 50")).ToList();

        int synced = 0;
        long now = SystemTime.UtcNowMs;
        int todayInt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));
        foreach (var e in list)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO mrms_case_records
                (id, case_no, encounter_id, patient_name, gender, age, discharge_dept_name, discharge_date_int, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @No, @Eid, @Name, @Gender, 45, @Dept, @Dt, 'PENDING_RETRIEVE', 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = Guid.NewGuid().ToString("D"), No = "BA-" + (string)e.AdmNo, Eid = (string)e.Id, Name = (string)e.PatientName, Gender = (string)e.Gender, Dept = (string?)e.DeptName, Dt = todayInt, Now = now, Actor = ActorId });
            synced++;
        }
        return ApiResponse<object>.Success(new { SyncedCount = synced }, synced > 0 ? $"已同步 {synced} 份出院待回收病案" : "当前无新增待回收出院病案");
    }

    [HttpPost("cases")]
    public async Task<ApiResponse<object>> CreateCase([FromBody] CreateCaseDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName)) return ApiResponse<object>.Fail(400, "患者姓名不能为空");
        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string caseNo = "BA" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO mrms_case_records
            (id, case_no, patient_name, gender, age, discharge_dept_name, discharge_date_int, primary_icd10_code, primary_icd10_name, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Name, @Gender, @Age, @Dept, @Dt, @Icd, @IcdName, 'PENDING_RETRIEVE', 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id, No = caseNo, Name = req.PatientName.Trim(), req.Gender, req.Age,
                Dept = req.DischargeDeptName, Dt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd")),
                Icd = req.PrimaryIcd10Code, IcdName = req.PrimaryIcd10Name, Now = now, Actor = ActorId
            });
        return ApiResponse<object>.Success(new { Id = id, CaseNo = caseNo }, $"病案 [{caseNo}] 已登记入待回收队列");
    }

    [HttpPut("cases/{id}/coding")]
    public async Task<ApiResponse<object>> UpdateCodingAndArchive(string id, [FromBody] UpdateCodingDto req, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        string drg = string.IsNullOrWhiteSpace(req.DrgGroupCode) && !string.IsNullOrWhiteSpace(req.PrimaryIcd10Code)
            ? "DRG-" + req.PrimaryIcd10Code.Trim().ToUpperInvariant()[..Math.Min(3, req.PrimaryIcd10Code.Trim().Length)] + (string.IsNullOrWhiteSpace(req.PrimaryIcd9Code) ? "9" : "1")
            : (req.DrgGroupCode ?? string.Empty);

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE mrms_case_records
            SET primary_icd10_code = @Icd10, primary_icd10_name = @Icd10Name,
                primary_icd9_code = @Icd9, primary_icd9_name = @Icd9Name,
                drg_group_code = @Drg, status = @St, shelf_location = @Shelf,
                qc_note = @Qc, coder_name = @Coder,
                archived_at_ms = CASE WHEN @St IN ('ARCHIVED','SEALED') THEN @Now ELSE archived_at_ms END,
                updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new
            {
                Icd10 = req.PrimaryIcd10Code, Icd10Name = req.PrimaryIcd10Name,
                Icd9 = req.PrimaryIcd9Code, Icd9Name = req.PrimaryIcd9Name,
                Drg = drg, St = req.Status, Shelf = req.ShelfLocation, Qc = req.QcNote,
                Coder = ActorName, Now = now, Actor = ActorId, Id = id
            });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("MRMS_CODING_ARCHIVE", "mrms_case_records", id, $"病案编码/状态变更：{req.Status}（ICD10={req.PrimaryIcd10Code}，DRG={drg}）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { id, DrgGroupCode = drg, req.Status }, $"病案已更新为 [{req.Status}]（DRG预分组：{drg}）");
    }

    [HttpGet("borrows")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetBorrowLogs()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, case_id AS CaseId, case_no AS CaseNo, patient_name AS PatientName,
                   action_type AS ActionType, applicant_name AS ApplicantName, purpose AS Purpose,
                   due_date_int AS DueDateInt, status AS Status, handled_by AS HandledBy
            FROM mrms_borrow_logs ORDER BY created_at_ms DESC LIMIT 150")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("borrows")]
    public async Task<ApiResponse<object>> CreateBorrowLog([FromBody] CreateBorrowDto req)
    {
        using var conn = _connFactory.CreateConnection();
        var c = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT case_no AS CaseNo, patient_name AS PatientName FROM mrms_case_records WHERE id = @Id", new { Id = req.CaseId });
        if (c == null) return ApiResponse<object>.Fail(404, "病案记录不存在");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        await conn.ExecuteAsync(@"
            INSERT INTO mrms_borrow_logs
            (id, case_id, case_no, patient_name, action_type, applicant_name, purpose, due_date_int, status, handled_by, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Cid, @No, @Name, @Type, @App, @Purp, @Due, 'ACTIVE', @ActorName, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Cid = req.CaseId, No = (string)c.CaseNo, Name = (string)c.PatientName, Type = req.ActionType, App = req.ApplicantName, Purp = req.Purpose, Due = req.DueDateInt, ActorName, Now = now, Actor = ActorId });

        if (req.ActionType == "SEAL")
            await conn.ExecuteAsync("UPDATE mrms_case_records SET status = 'SEALED', updated_at_ms = @Now WHERE id = @Id", new { Now = now, Id = req.CaseId });

        return ApiResponse<object>.Success(new { Id = id }, $"已登记病案 [{req.ActionType}] 记录");
    }
}
