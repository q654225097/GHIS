using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.MedTech.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.MedTech.Controllers;

/// <summary>
/// 医技报告书写审签工作站与分科室自定义编辑/打印模板引擎
/// </summary>
[ApiController]
[Route("api/v4/medtech-reports")]
public sealed class MedTechReportController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<MtcReportTemplate> _tplRepo;
    private readonly IRepository<MtcDiagnosticReport> _rptRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IClinicalAlertPublisher? _alertPublisher;

    public MedTechReportController(
        IDbConnectionFactory connFactory,
        IRepository<MtcReportTemplate> tplRepo,
        IRepository<MtcDiagnosticReport> rptRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null,
        IClinicalAlertPublisher? alertPublisher = null)
    {
        _connFactory = connFactory;
        _tplRepo = tplRepo;
        _rptRepo = rptRepo;
        _session = session;
        _auditTrail = auditTrail;
        _alertPublisher = alertPublisher;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record SaveReportTemplateDto(
        string? Id,
        string TemplateCode,
        string TemplateName,
        string MedtechDeptCode,
        string MedtechDeptName,
        string ModalityType,
        string EditorSchemaJson,
        decimal PrintPaperWidthMm,
        decimal PrintPaperHeightMm,
        string PrintHeaderTitle,
        string? PrintSubTitle,
        string? PrintFooterNote,
        string? PrintTemplateId,
        bool IsDefault = false,
        string? Remark = null);

    public sealed record CreateManualReportDto(
        string? ServiceRequestId,
        string? RequestNo,
        string PatientName,
        string Gender,
        int Age,
        string? MedicalRecordNo,
        string? OrderDeptName,
        string MedtechDeptCode,
        string MedtechDeptName,
        string? ExamItemCode,
        string ExamItemName,
        string? TemplateId = null);

    public sealed record DeviceIngestResultDto(
        string? ServiceRequestId,
        string? RequestNo,
        string PatientName,
        string Gender,
        int Age,
        string? MedicalRecordNo,
        string? OrderDeptName,
        string MedtechDeptCode,
        string MedtechDeptName,
        string? ExamItemCode,
        string ExamItemName,
        string DeviceName,
        string ContentJson,
        string FindingsSummary,
        string DiagnosisImpression,
        bool IsCriticalValue = false);

    public sealed record SaveReportContentDto(
        string? TemplateId,
        string? TemplateName,
        string ContentJson,
        string? FindingsSummary,
        string? DiagnosisImpression,
        bool IsCriticalValue = false);

    public sealed record RejectReportDto(string Reason);

    [HttpGet("summary")]
    public async Task<ApiResponse<object>> GetSummary([FromQuery] string? deptCode = null)
    {
        using var conn = _connFactory.CreateConnection();
        string where = string.IsNullOrWhiteSpace(deptCode) || deptCode == "ALL" ? "" : " AND medtech_dept_code = @Dept";
        var p = new { Dept = deptCode?.Trim() };

        int pendingWrite = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM mtc_diagnostic_reports WHERE status IN ('PENDING_WRITE','DRAFT','REJECTED'){where}", p);
        int pendingReview = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM mtc_diagnostic_reports WHERE status = 'PENDING_REVIEW'{where}", p);
        int devicePendingReview = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM mtc_diagnostic_reports WHERE status = 'PENDING_REVIEW' AND result_source = 'DEVICE_AUTO'{where}", p);
        int signed = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM mtc_diagnostic_reports WHERE status = 'SIGNED'{where}", p);
        int critical = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM mtc_diagnostic_reports WHERE is_critical_value = 1{where}", p);

        return ApiResponse<object>.Success(new
        {
            PendingWriteCount = pendingWrite,
            PendingReviewCount = pendingReview,
            DeviceAutoPendingCount = devicePendingReview,
            SignedCount = signed,
            CriticalCount = critical
        });
    }

    // ===================== 1. 分科室自定义编辑与打印模板管理 =====================

    [HttpGet("templates")]
    public async Task<ApiResponse<IReadOnlyList<MtcReportTemplate>>> GetTemplates([FromQuery] string? deptCode = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = "SELECT * FROM mtc_report_templates WHERE is_active = 1";
        if (!string.IsNullOrWhiteSpace(deptCode) && deptCode != "ALL")
            sql += " AND medtech_dept_code = @Dept";
        sql += " ORDER BY medtech_dept_code ASC, is_default DESC, updated_at_ms DESC";
        var rows = (await conn.QueryAsync<MtcReportTemplate>(sql, new { Dept = deptCode?.Trim() })).ToList();
        return ApiResponse<IReadOnlyList<MtcReportTemplate>>.Success(rows);
    }

    [HttpPost("templates")]
    public async Task<ApiResponse<MtcReportTemplate>> SaveTemplate([FromBody] SaveReportTemplateDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.TemplateName))
            return ApiResponse<MtcReportTemplate>.Fail(400, "模板名称不能为空");
        if (req.PrintPaperWidthMm < 40 || req.PrintPaperHeightMm < 40)
            return ApiResponse<MtcReportTemplate>.Fail(400, "打印纸张长宽不得小于 40mm");

        long now = SystemTime.UtcNowMs;
        var existing = string.IsNullOrWhiteSpace(req.Id) ? null : await _tplRepo.GetByIdAsync(req.Id, ct);
        string deptCode = string.IsNullOrWhiteSpace(req.MedtechDeptCode) ? "RAD" : req.MedtechDeptCode.Trim().ToUpperInvariant();

        if (req.IsDefault)
        {
            using var conn = _connFactory.CreateConnection();
            await conn.ExecuteAsync("UPDATE mtc_report_templates SET is_default = 0 WHERE medtech_dept_code = @Dept", new { Dept = deptCode });
        }

        if (existing == null)
        {
            string code = string.IsNullOrWhiteSpace(req.TemplateCode)
                ? $"TPL-{deptCode}-{SystemTime.ShanghaiNow:yyMMddHHmm}"
                : req.TemplateCode.Trim().ToUpperInvariant();

            var tpl = new MtcReportTemplate
            {
                Id = "MTC-TPL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                TemplateCode = code,
                TemplateName = req.TemplateName.Trim(),
                MedtechDeptCode = deptCode,
                MedtechDeptName = string.IsNullOrWhiteSpace(req.MedtechDeptName) ? deptCode : req.MedtechDeptName.Trim(),
                ModalityType = string.IsNullOrWhiteSpace(req.ModalityType) ? deptCode : req.ModalityType.Trim(),
                EditorSchemaJson = string.IsNullOrWhiteSpace(req.EditorSchemaJson) ? "[]" : req.EditorSchemaJson,
                PrintPaperWidthMm = req.PrintPaperWidthMm,
                PrintPaperHeightMm = req.PrintPaperHeightMm,
                PrintHeaderTitle = string.IsNullOrWhiteSpace(req.PrintHeaderTitle) ? "GHIS 示范医院 医技诊断报告单" : req.PrintHeaderTitle.Trim(),
                PrintSubTitle = req.PrintSubTitle?.Trim(),
                PrintFooterNote = req.PrintFooterNote?.Trim(),
                PrintTemplateId = req.PrintTemplateId?.Trim(),
                IsDefault = (short)(req.IsDefault ? 1 : 0),
                IsActive = 1,
                Remark = req.Remark?.Trim(),
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            };
            await _tplRepo.InsertAsync(tpl, ct);
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("MTC_TEMPLATE_CREATE", "mtc_report_templates", tpl.Id, $"新增医技科室模板：{tpl.MedtechDeptName} - {tpl.TemplateName}", ActorId, ActorName, null, ct);
            return ApiResponse<MtcReportTemplate>.Success(tpl, "医技报告模板已创建");
        }
        else
        {
            existing.TemplateName = req.TemplateName.Trim();
            if (!string.IsNullOrWhiteSpace(req.TemplateCode))
                existing.TemplateCode = req.TemplateCode.Trim().ToUpperInvariant();
            existing.MedtechDeptCode = deptCode;
            existing.MedtechDeptName = string.IsNullOrWhiteSpace(req.MedtechDeptName) ? existing.MedtechDeptName : req.MedtechDeptName.Trim();
            existing.ModalityType = string.IsNullOrWhiteSpace(req.ModalityType) ? existing.ModalityType : req.ModalityType.Trim();
            existing.EditorSchemaJson = string.IsNullOrWhiteSpace(req.EditorSchemaJson) ? "[]" : req.EditorSchemaJson;
            existing.PrintPaperWidthMm = req.PrintPaperWidthMm;
            existing.PrintPaperHeightMm = req.PrintPaperHeightMm;
            existing.PrintHeaderTitle = string.IsNullOrWhiteSpace(req.PrintHeaderTitle) ? existing.PrintHeaderTitle : req.PrintHeaderTitle.Trim();
            existing.PrintSubTitle = req.PrintSubTitle?.Trim();
            existing.PrintFooterNote = req.PrintFooterNote?.Trim();
            existing.PrintTemplateId = req.PrintTemplateId?.Trim();
            existing.IsDefault = (short)(req.IsDefault ? 1 : 0);
            existing.Remark = req.Remark?.Trim();
            existing.UpdatedAtMs = now;
            existing.UpdatedBy = ActorId;
            await _tplRepo.UpdateAsync(existing, ct);
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("MTC_TEMPLATE_UPDATE", "mtc_report_templates", existing.Id, $"更新医技科室模板：{existing.MedtechDeptName} - {existing.TemplateName}", ActorId, ActorName, null, ct);
            return ApiResponse<MtcReportTemplate>.Success(existing, "医技报告模板已更新");
        }
    }

    [HttpDelete("templates/{id}")]
    public async Task<ApiResponse<object>> DisableTemplate(string id, CancellationToken ct)
    {
        var existing = await _tplRepo.GetByIdAsync(id, ct);
        if (existing == null) return ApiResponse<object>.Fail(404, "模板不存在");
        existing.IsActive = 0;
        existing.UpdatedAtMs = SystemTime.UtcNowMs;
        existing.UpdatedBy = ActorId;
        await _tplRepo.UpdateAsync(existing, ct);
        return ApiResponse<object>.Success(new { id }, "医技报告模板已停用");
    }

    // ===================== 2. 工作站报告队列、同步申请单与机器直连直出 =====================

    [HttpGet("queue")]
    public async Task<ApiResponse<IReadOnlyList<MtcDiagnosticReport>>> GetWorklistQueue(
        [FromQuery] string? deptCode = null,
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = "SELECT * FROM mtc_diagnostic_reports WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(deptCode) && deptCode != "ALL")
            sql += " AND medtech_dept_code = @Dept";
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            if (status == "WRITE_GROUP")
                sql += " AND status IN ('PENDING_WRITE','DRAFT','REJECTED')";
            else
                sql += " AND status = @Status";
        }
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (patient_name LIKE @Kw OR report_no LIKE @Kw OR request_no LIKE @Kw OR exam_item_name LIKE @Kw OR medical_record_no LIKE @Kw)";

        sql += " ORDER BY CASE status WHEN 'PENDING_REVIEW' THEN 1 WHEN 'REJECTED' THEN 2 WHEN 'PENDING_WRITE' THEN 3 WHEN 'DRAFT' THEN 4 ELSE 5 END, updated_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<MtcDiagnosticReport>(sql, new
        {
            Dept = deptCode?.Trim(),
            Status = status?.Trim(),
            Kw = $"%{keyword?.Trim()}%"
        })).ToList();
        return ApiResponse<IReadOnlyList<MtcDiagnosticReport>>.Success(rows);
    }

    /// <summary>
    /// 从全院已开立的医技申请单 (mtc_service_requests) 自动拉取尚未建报告的记录进入待书写队列
    /// </summary>
    [HttpPost("sync-from-requests")]
    public async Task<ApiResponse<object>> SyncFromServiceRequests(CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        var pendingReqs = (await conn.QueryAsync<dynamic>(@"
            SELECT r.id AS Id, r.request_no AS RequestNo, r.patient_id AS PatientId,
                   r.med_tech_sub_type AS SubType, r.service_item_code AS ItemCode,
                   COALESCE(p.full_name, '待核对患者') AS PatientName,
                   COALESCE(p.gender_code, '男') AS Gender,
                   COALESCE(p.medical_record_no, '') AS MedicalRecordNo,
                   COALESCE(d.dept_name, r.order_dept_id) AS OrderDeptName
            FROM mtc_service_requests r
            LEFT JOIN pat_patients p ON p.id = r.patient_id
            LEFT JOIN sys_departments d ON d.id = r.order_dept_id
            WHERE NOT EXISTS (SELECT 1 FROM mtc_diagnostic_reports dr WHERE dr.service_request_id = r.id)
            ORDER BY r.created_at_ms DESC LIMIT 50")).ToList();

        int created = 0;
        long now = SystemTime.UtcNowMs;
        foreach (var req in pendingReqs)
        {
            string sub = ((string?)req.SubType ?? "RAD").ToUpperInvariant();
            (string dCode, string dName) = sub switch
            {
                "LIS" or "LAB" => ("LAB", "检验科"),
                "US" or "ULTRASOUND" => ("US", "超声科"),
                "ECG" => ("ECG", "心电图室"),
                "ENDO" => ("ENDO", "内镜中心"),
                _ => ("RAD", "放射科")
            };
            var tpl = await conn.QueryFirstOrDefaultAsync<MtcReportTemplate>(
                "SELECT * FROM mtc_report_templates WHERE medtech_dept_code = @D AND is_active = 1 ORDER BY is_default DESC LIMIT 1",
                new { D = dCode });

            await _rptRepo.InsertAsync(new MtcDiagnosticReport
            {
                Id = Guid.NewGuid().ToString("D"),
                ReportNo = "RPT-" + dCode + "-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
                ServiceRequestId = (string)req.Id,
                RequestNo = (string?)req.RequestNo,
                PatientId = (string?)req.PatientId,
                PatientName = (string)req.PatientName,
                Gender = (string)req.Gender,
                Age = 40,
                MedicalRecordNo = (string?)req.MedicalRecordNo,
                OrderDeptName = (string?)req.OrderDeptName,
                MedtechDeptCode = dCode,
                MedtechDeptName = dName,
                ExamItemCode = (string?)req.ItemCode,
                ExamItemName = (string?)req.ItemCode ?? "医技检查项目",
                TemplateId = tpl?.Id,
                TemplateName = tpl?.TemplateName,
                ResultSource = "MANUAL",
                Status = "PENDING_WRITE",
                ContentJson = "{}",
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            }, ct);
            created++;
        }

        return ApiResponse<object>.Success(new { SyncedCount = created }, created > 0 ? $"已从医技申请单同步 {created} 份待书写报告" : "当前无新增待同步的医技申请单");
    }

    /// <summary>
    /// 登记/分诊新医技报告任务（直接进入待书写队列）
    /// </summary>
    [HttpPost("create-manual")]
    public async Task<ApiResponse<MtcDiagnosticReport>> CreateManualReport([FromBody] CreateManualReportDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.ExamItemName))
            return ApiResponse<MtcDiagnosticReport>.Fail(400, "患者姓名与检查/检验项目名称不能为空");

        string dCode = string.IsNullOrWhiteSpace(req.MedtechDeptCode) ? "RAD" : req.MedtechDeptCode.Trim().ToUpperInvariant();
        using var conn = _connFactory.CreateConnection();
        var tpl = string.IsNullOrWhiteSpace(req.TemplateId)
            ? await conn.QueryFirstOrDefaultAsync<MtcReportTemplate>(
                "SELECT * FROM mtc_report_templates WHERE medtech_dept_code = @D AND is_active = 1 ORDER BY is_default DESC LIMIT 1",
                new { D = dCode })
            : await _tplRepo.GetByIdAsync(req.TemplateId, ct);

        long now = SystemTime.UtcNowMs;
        var report = new MtcDiagnosticReport
        {
            Id = Guid.NewGuid().ToString("D"),
            ReportNo = "RPT-" + dCode + "-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            ServiceRequestId = req.ServiceRequestId,
            RequestNo = string.IsNullOrWhiteSpace(req.RequestNo) ? "REQ-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmm") : req.RequestNo.Trim(),
            PatientName = req.PatientName.Trim(),
            Gender = string.IsNullOrWhiteSpace(req.Gender) ? "男" : req.Gender.Trim(),
            Age = Math.Clamp(req.Age, 0, 120),
            MedicalRecordNo = req.MedicalRecordNo?.Trim(),
            OrderDeptName = req.OrderDeptName?.Trim(),
            MedtechDeptCode = dCode,
            MedtechDeptName = string.IsNullOrWhiteSpace(req.MedtechDeptName) ? (tpl?.MedtechDeptName ?? dCode) : req.MedtechDeptName.Trim(),
            ExamItemCode = req.ExamItemCode?.Trim(),
            ExamItemName = req.ExamItemName.Trim(),
            TemplateId = tpl?.Id,
            TemplateName = tpl?.TemplateName,
            ResultSource = "MANUAL",
            Status = "PENDING_WRITE",
            ContentJson = "{}",
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        await _rptRepo.InsertAsync(report, ct);
        return ApiResponse<MtcDiagnosticReport>.Success(report, "已分配至待书写队列");
    }

    /// <summary>
    /// 仪器设备直连结果回传接口：机器直接出结果时，自动跳过书写阶段，直接进入 PENDING_REVIEW（待审签）队列
    /// </summary>
    [HttpPost("device-ingest")]
    public async Task<ApiResponse<MtcDiagnosticReport>> IngestDeviceResult([FromBody] DeviceIngestResultDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.ExamItemName))
            return ApiResponse<MtcDiagnosticReport>.Fail(400, "患者姓名与检测项目不能为空");

        string dCode = string.IsNullOrWhiteSpace(req.MedtechDeptCode) ? "LAB" : req.MedtechDeptCode.Trim().ToUpperInvariant();
        using var conn = _connFactory.CreateConnection();
        var tpl = await conn.QueryFirstOrDefaultAsync<MtcReportTemplate>(
            "SELECT * FROM mtc_report_templates WHERE medtech_dept_code = @D AND is_active = 1 ORDER BY is_default DESC LIMIT 1",
            new { D = dCode });

        long now = SystemTime.UtcNowMs;
        var report = new MtcDiagnosticReport
        {
            Id = Guid.NewGuid().ToString("D"),
            ReportNo = "RPT-" + dCode + "-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            ServiceRequestId = req.ServiceRequestId,
            RequestNo = string.IsNullOrWhiteSpace(req.RequestNo) ? "DEV-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmm") : req.RequestNo.Trim(),
            PatientName = req.PatientName.Trim(),
            Gender = string.IsNullOrWhiteSpace(req.Gender) ? "男" : req.Gender.Trim(),
            Age = Math.Clamp(req.Age, 0, 120),
            MedicalRecordNo = req.MedicalRecordNo?.Trim(),
            OrderDeptName = req.OrderDeptName?.Trim(),
            MedtechDeptCode = dCode,
            MedtechDeptName = string.IsNullOrWhiteSpace(req.MedtechDeptName) ? (tpl?.MedtechDeptName ?? dCode) : req.MedtechDeptName.Trim(),
            ExamItemCode = req.ExamItemCode?.Trim(),
            ExamItemName = req.ExamItemName.Trim(),
            TemplateId = tpl?.Id,
            TemplateName = tpl?.TemplateName,
            ResultSource = "DEVICE_AUTO",
            DeviceName = string.IsNullOrWhiteSpace(req.DeviceName) ? "全自动检验/检查分析仪" : req.DeviceName.Trim(),
            Status = "PENDING_REVIEW", // 核心业务规则：机器直出结果直接进入待审签！
            ContentJson = string.IsNullOrWhiteSpace(req.ContentJson) ? "{}" : req.ContentJson,
            FindingsSummary = req.FindingsSummary,
            DiagnosisImpression = req.DiagnosisImpression,
            IsCriticalValue = (short)(req.IsCriticalValue ? 1 : 0),
            WriterName = $"[仪器直传] {req.DeviceName}",
            WrittenAtMs = now,
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        await _rptRepo.InsertAsync(report, ct);
        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("MTC_DEVICE_INGEST", "mtc_diagnostic_reports", report.Id, $"仪器直传结果进入待审签：{report.PatientName} - {report.ExamItemName}（设备：{report.DeviceName}）", ActorId, ActorName, null, ct);

        return ApiResponse<MtcDiagnosticReport>.Success(report, "仪器结果已接入，自动跳过书写直接进入【待审签】队列");
    }

    // ===================== 3. 报告书写保存、提交审签、审核双签发布与驳回 =====================

    [HttpPut("{id}/save-draft")]
    public async Task<ApiResponse<MtcDiagnosticReport>> SaveDraft(string id, [FromBody] SaveReportContentDto req, CancellationToken ct)
    {
        var rpt = await _rptRepo.GetByIdAsync(id, ct);
        if (rpt == null) return ApiResponse<MtcDiagnosticReport>.Fail(404, "医技报告不存在");
        if (rpt.Status == "SIGNED") return ApiResponse<MtcDiagnosticReport>.Fail(409, "已双签发布的报告禁止直接修改");

        long now = SystemTime.UtcNowMs;
        if (!string.IsNullOrWhiteSpace(req.TemplateId)) rpt.TemplateId = req.TemplateId.Trim();
        if (!string.IsNullOrWhiteSpace(req.TemplateName)) rpt.TemplateName = req.TemplateName.Trim();
        rpt.ContentJson = string.IsNullOrWhiteSpace(req.ContentJson) ? "{}" : req.ContentJson;
        rpt.FindingsSummary = req.FindingsSummary;
        rpt.DiagnosisImpression = req.DiagnosisImpression;
        rpt.IsCriticalValue = (short)(req.IsCriticalValue ? 1 : 0);
        rpt.WriterId = ActorId;
        rpt.WriterName = ActorName;
        rpt.WrittenAtMs = now;
        if (rpt.Status == "PENDING_WRITE" || rpt.Status == "REJECTED") rpt.Status = "DRAFT";
        rpt.UpdatedAtMs = now;
        rpt.UpdatedBy = ActorId;
        await _rptRepo.UpdateAsync(rpt, ct);
        return ApiResponse<MtcDiagnosticReport>.Success(rpt, "报告草稿已保存");
    }

    [HttpPost("{id}/submit-review")]
    public async Task<ApiResponse<MtcDiagnosticReport>> SubmitForReview(string id, [FromBody] SaveReportContentDto req, CancellationToken ct)
    {
        var rpt = await _rptRepo.GetByIdAsync(id, ct);
        if (rpt == null) return ApiResponse<MtcDiagnosticReport>.Fail(404, "医技报告不存在");
        if (rpt.Status == "SIGNED") return ApiResponse<MtcDiagnosticReport>.Fail(409, "报告已发布");

        long now = SystemTime.UtcNowMs;
        if (!string.IsNullOrWhiteSpace(req.TemplateId)) rpt.TemplateId = req.TemplateId.Trim();
        if (!string.IsNullOrWhiteSpace(req.TemplateName)) rpt.TemplateName = req.TemplateName.Trim();
        rpt.ContentJson = string.IsNullOrWhiteSpace(req.ContentJson) ? "{}" : req.ContentJson;
        rpt.FindingsSummary = req.FindingsSummary;
        rpt.DiagnosisImpression = req.DiagnosisImpression;
        rpt.IsCriticalValue = (short)(req.IsCriticalValue ? 1 : 0);
        rpt.WriterId = ActorId;
        rpt.WriterName = ActorName;
        rpt.WrittenAtMs = now;
        rpt.RejectReason = null;
        rpt.Status = "PENDING_REVIEW";
        rpt.UpdatedAtMs = now;
        rpt.UpdatedBy = ActorId;
        await _rptRepo.UpdateAsync(rpt, ct);
        return ApiResponse<MtcDiagnosticReport>.Success(rpt, "报告已提交上级医师审签");
    }

    [HttpPost("{id}/approve")]
    public async Task<ApiResponse<MtcDiagnosticReport>> ApproveAndSign(string id, [FromBody] SaveReportContentDto? req, CancellationToken ct)
    {
        string activeRole = RoleCode.Normalize(_session?.RoleCode);
        if (string.IsNullOrWhiteSpace(_session?.UserId) || activeRole is not (RoleCode.MedTech or RoleCode.Admin))
            return ApiResponse<MtcDiagnosticReport>.Fail(403, "当前岗位无权审签医技报告");
        var rpt = await _rptRepo.GetByIdAsync(id, ct);
        if (rpt == null) return ApiResponse<MtcDiagnosticReport>.Fail(404, "医技报告不存在");
        if (rpt.Status != "PENDING_REVIEW") return ApiResponse<MtcDiagnosticReport>.Fail(409, "仅待审签的报告可以发布");

        long now = SystemTime.UtcNowMs;
        if (req != null)
        {
            if (!string.IsNullOrWhiteSpace(req.ContentJson)) rpt.ContentJson = req.ContentJson;
            if (req.FindingsSummary != null) rpt.FindingsSummary = req.FindingsSummary;
            if (req.DiagnosisImpression != null) rpt.DiagnosisImpression = req.DiagnosisImpression;
            rpt.IsCriticalValue = (short)(req.IsCriticalValue ? 1 : 0);
        }

        string? reportSummary = string.IsNullOrWhiteSpace(rpt.DiagnosisImpression) ? rpt.FindingsSummary : rpt.DiagnosisImpression;
        if (string.IsNullOrWhiteSpace(reportSummary))
            return ApiResponse<MtcDiagnosticReport>.Fail(400, "须填写实际报告所见或诊断结论后才能审签");

        rpt.ReviewerId = ActorId;
        rpt.ReviewerName = ActorName;
        rpt.ReviewedAtMs = now;
        rpt.Status = "SIGNED";
        rpt.SignatureHash = CryptoHelper.ComputeSm3($"{rpt.Id}|{rpt.ReportNo}|{rpt.ContentJson}|{rpt.WriterId}|{ActorId}|{now}");
        rpt.UpdatedAtMs = now;
        rpt.UpdatedBy = ActorId;
        MedtechCriticalValue? alert = null;
        MtcServiceRequest? sourceRequest = null;
        ApiResponse<MtcDiagnosticReport>? rejected;
        try
        {
            rejected = await _connFactory.ExecuteInTransactionAsync<ApiResponse<MtcDiagnosticReport>?>(async token =>
            {
                using var conn = _connFactory.CreateConnection();
                var tx = _connFactory.CurrentTransaction;
                if (!string.IsNullOrWhiteSpace(rpt.ServiceRequestId))
                {
                    sourceRequest = await conn.QuerySingleOrDefaultAsync<MtcServiceRequest>(new CommandDefinition(
                        "SELECT * FROM mtc_service_requests WHERE id = @Id", new { Id = rpt.ServiceRequestId }, tx, cancellationToken: token));
                    if (sourceRequest == null || sourceRequest.PatientId != rpt.PatientId || string.IsNullOrWhiteSpace(sourceRequest.SourceVisitId))
                        return ApiResponse<MtcDiagnosticReport>.Fail(409, "报告、申请和患者未正确关联，请重新核对原申请");
                    if (activeRole != RoleCode.Admin && sourceRequest.ExecMedTechDeptId != _session.ActiveDeptId)
                        return ApiResponse<MtcDiagnosticReport>.Fail(403, "不能审签其他医技科室的申请报告");
                    if (sourceRequest.Status != 2 || sourceRequest.IsFeeConfirmed != 1)
                        return ApiResponse<MtcDiagnosticReport>.Fail(409, "医技申请必须完成真实核收且尚未发布报告");

                    const string sourceCheck = """
                        SELECT COUNT(*) FROM (
                            SELECT id FROM opd_visits WHERE @Source = 'OPD' AND id = @Visit AND patient_id = @Patient
                            UNION ALL
                            SELECT id FROM ipd_encounters WHERE @Source = 'IPD' AND id = @Visit AND patient_id = @Patient
                        ) AS linked_visit
                        """;
                    var sourceMatches = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sourceCheck,
                        new { Source = sourceRequest.SourceType, Visit = sourceRequest.SourceVisitId, Patient = rpt.PatientId },
                        tx, cancellationToken: token));
                    if (sourceMatches != 1)
                        return ApiResponse<MtcDiagnosticReport>.Fail(409, "申请未关联到同一患者的有效就诊记录");

                    int updated = await conn.ExecuteAsync(new CommandDefinition("""
                        UPDATE mtc_service_requests
                        SET status = 3, report_summary = @Summary, is_critical_value = @Critical,
                            executed_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                        WHERE id = @Id AND status = 2 AND is_fee_confirmed = 1 AND patient_id = @Patient
                        """, new { Id = sourceRequest.Id, Summary = reportSummary, Critical = rpt.IsCriticalValue, Now = now,
                            Actor = ActorId, Patient = rpt.PatientId }, tx, cancellationToken: token));
                    if (updated != 1)
                        return ApiResponse<MtcDiagnosticReport>.Fail(409, "医技申请状态已变化，请刷新后重试");

                    if (rpt.IsCriticalValue == 1)
                    {
                        alert = new MedtechCriticalValue
                        {
                            ReportId = rpt.Id, SourceType = sourceRequest.SourceType, VisitOrEncounterId = sourceRequest.SourceVisitId,
                            PatientId = sourceRequest.PatientId, ItemCode = sourceRequest.ServiceItemCode, ItemName = rpt.ExamItemName,
                            CriticalValueText = reportSummary.Trim(), PublishedBy = ActorId, PublishedAtMs = now,
                            CreatedBy = ActorId, UpdatedBy = ActorId
                        };
                        var alertRepo = new DapperRepository<MedtechCriticalValue>(_connFactory, "medtech_critical_values");
                        await alertRepo.InsertAsync(alert, token);
                    }
                }
                if (await _rptRepo.UpdateAsync(rpt, token) != 1)
                    throw new DbConcurrencyException("mtc_diagnostic_reports", id, "医技报告状态已变化");
                return null;
            }, ct);
        }
        catch (DbConcurrencyException)
        {
            return ApiResponse<MtcDiagnosticReport>.Fail(409, "报告已被其他人处理，请刷新后重试");
        }
        if (rejected != null) return rejected;

        if (_alertPublisher != null && alert != null && sourceRequest != null)
        {
            try
            {
                await _alertPublisher.PublishReportCriticalValueAsync(alert.Id, sourceRequest.OrderDeptId, sourceRequest.OrderWardId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Critical value notification failed: {0}", ex.GetType().Name);
            }
        }
        // 联动预住院评估闭环：双签发布后自动更新院前检查进度；若命中危急值或P0规则则立即中断并锁停未做检查
        using (var paConn = _connFactory.CreateConnection())
        {
            string? preId = await paConn.ExecuteScalarAsync<string?>(
                "SELECT pre_admission_id FROM ipd_pre_admission_orders WHERE report_id = @Rid LIMIT 1",
                new { Rid = rpt.Id });
            if (!string.IsNullOrWhiteSpace(preId))
            {
                string summaryText = rpt.DiagnosisImpression ?? rpt.FindingsSummary ?? "已完成报告";
                await paConn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET exec_status = 'DONE', is_critical = @Crit, result_summary = @Sum, updated_at_ms = @Now WHERE report_id = @Rid",
                    new { Crit = rpt.IsCriticalValue, Sum = summaryText, Now = now, Rid = rpt.Id });

                bool hitP0 = rpt.IsCriticalValue == 1;
                string? hitRuleName = null;
                if (!hitP0)
                {
                    var p0Rules = await paConn.QueryAsync<dynamic>(
                        "SELECT rule_name AS RuleName, match_keyword AS Keywords FROM ipd_pre_admission_rules WHERE is_active = 1 AND rule_level = 'P0'");
                    foreach (var rule in p0Rules)
                    {
                        string kws = (string?)rule.Keywords ?? string.Empty;
                        foreach (var kw in kws.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (summaryText.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            {
                                hitP0 = true;
                                hitRuleName = (string?)rule.RuleName;
                                break;
                            }
                        }
                        if (hitP0) break;
                    }
                }

                int doneCnt = await paConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admission_orders WHERE pre_admission_id = @Pid AND exec_status = 'DONE'", new { Pid = preId });
                int totalCnt = await paConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admission_orders WHERE pre_admission_id = @Pid AND exec_status != 'CANCELLED'", new { Pid = preId });

                if (hitP0)
                {
                    await paConn.ExecuteAsync(
                        "UPDATE ipd_pre_admission_orders SET exec_status = 'HOLD_BY_CRITICAL', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status = 'PENDING'",
                        new { Now = now, Pid = preId });
                    await paConn.ExecuteAsync(@"
                        UPDATE ipd_pre_admissions
                        SET done_order_count = @Done, has_critical_alert = 1,
                            critical_diag_name = COALESCE(@RName, @ItemName),
                            critical_summary = @Sum, critical_triggered_at_ms = @Now,
                            status = 'CRITICAL_INTERRUPTED', updated_at_ms = @Now
                        WHERE id = @Pid AND status NOT IN ('CONVERTED_IPD','CONVERTED_OPD')",
                        new { Done = doneCnt, RName = hitRuleName, rpt.ExamItemName, Sum = summaryText, Now = now, Pid = preId });
                }
                else
                {
                    string nextSt = (totalCnt > 0 && doneCnt >= totalCnt) ? "PENDING_EVALUATION" : "CHECKING";
                    await paConn.ExecuteAsync(@"
                        UPDATE ipd_pre_admissions
                        SET done_order_count = @Done,
                            status = CASE WHEN status IN ('REGISTERED','CHECKING') THEN @NextSt ELSE status END,
                            updated_at_ms = @Now
                        WHERE id = @Pid",
                        new { Done = doneCnt, NextSt = nextSt, Now = now, Pid = preId });
                }
            }
        }

                // 联动体检系统：医技报告双签发布后自动回写体检分科结果与异常统计，免去体检医生二次手工录入
        using (var chkConn = _connFactory.CreateConnection())
        {
            string? chkItemId = await chkConn.ExecuteScalarAsync<string?>(
                "SELECT chk_order_item_id FROM mtc_diagnostic_reports WHERE id = @Rid LIMIT 1",
                new { Rid = rpt.Id });
            if (!string.IsNullOrWhiteSpace(chkItemId))
            {
                string resText = rpt.DiagnosisImpression ?? rpt.FindingsSummary ?? "已完成";
                string abnFlag = rpt.IsCriticalValue == 1 ? "POSITIVE" : "NORMAL";
                await chkConn.ExecuteAsync(@"
                    UPDATE chk_order_items
                    SET result_value = @Val, is_abnormal = @Crit, abnormal_flag = @Flag,
                        exam_doctor_name = @Doc, status = 'DONE', checked_at_ms = @Now, updated_at_ms = @Now
                    WHERE id = @ItemId",
                    new { Val = resText, Crit = rpt.IsCriticalValue, Flag = abnFlag, Doc = ActorName, Now = now, ItemId = chkItemId });

                string? orderId = await chkConn.ExecuteScalarAsync<string?>("SELECT order_id FROM chk_order_items WHERE id = @Id", new { Id = chkItemId });
                if (!string.IsNullOrWhiteSpace(orderId))
                {
                    int doneCnt = await chkConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid AND status IN ('DONE','WAIVED')", new { Oid = orderId });
                    int totalCnt = await chkConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid", new { Oid = orderId });
                    int abnCnt = await chkConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid AND is_abnormal = 1", new { Oid = orderId });
                    string nextSt = (totalCnt > 0 && doneCnt >= totalCnt) ? "PENDING_SUMMARY" : "IN_PROGRESS";
                    await chkConn.ExecuteAsync(
                        "UPDATE chk_orders SET done_item_count = @Done, abnormal_count = @Abn, status = CASE WHEN status = 'COMPLETED' THEN 'COMPLETED' ELSE @St END, updated_at_ms = @Now WHERE id = @Oid",
                        new { Done = doneCnt, Abn = abnCnt, St = nextSt, Now = now, Oid = orderId });
                }
            }
        }

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("MTC_REPORT_SIGNED", "mtc_diagnostic_reports", rpt.Id, $"审签发布医技报告：{rpt.PatientName} - {rpt.ExamItemName}（报告号 {rpt.ReportNo}，SM3签名 {rpt.SignatureHash[..12]}）", ActorId, ActorName, null, ct);

        return ApiResponse<MtcDiagnosticReport>.Success(rpt, "报告已双签审核发布");
    }

    [HttpPost("{id}/reject")]
    public async Task<ApiResponse<MtcDiagnosticReport>> RejectReport(string id, [FromBody] RejectReportDto req, CancellationToken ct)
    {
        var rpt = await _rptRepo.GetByIdAsync(id, ct);
        if (rpt == null) return ApiResponse<MtcDiagnosticReport>.Fail(404, "医技报告不存在");
        if (rpt.Status == "SIGNED") return ApiResponse<MtcDiagnosticReport>.Fail(409, "已发布报告不可驳回");

        long now = SystemTime.UtcNowMs;
        rpt.Status = "REJECTED";
        rpt.RejectReason = string.IsNullOrWhiteSpace(req.Reason) ? "请核对影像/检验所见后重新提交" : req.Reason.Trim();
        rpt.ReviewerId = ActorId;
        rpt.ReviewerName = ActorName;
        rpt.UpdatedAtMs = now;
        rpt.UpdatedBy = ActorId;
        await _rptRepo.UpdateAsync(rpt, ct);
        return ApiResponse<MtcDiagnosticReport>.Success(rpt, "已退回报告医师修改");
    }
}
