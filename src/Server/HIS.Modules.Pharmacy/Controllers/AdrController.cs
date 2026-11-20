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
using HIS.Modules.Pharmacy.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Pharmacy.Controllers;

[ApiController]
[Route("api/v4/adr")]
public class AdrController : ControllerBase
{
    private readonly IRepository<AdrReport> _adrRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IAuditTrailContract? _auditContract;
    private readonly IUserSession _userSession;

    public AdrController(
        IRepository<AdrReport> adrRepo,
        IDbConnectionFactory connFactory,
        IUserSession userSession,
        IAuditTrailContract? auditContract = null)
    {
        _adrRepo = adrRepo;
        _connFactory = connFactory;
        _userSession = userSession;
        _auditContract = auditContract;
    }

    public record CreateAdrReportDto(
        string PatientId,
        string EncounterOrVisitId,
        string? OrderOrRxId,
        string DrugId,
        string DrugName,
        string? BatchNo,
        string ReactionName,
        long OnsetTimeMs,
        string Severity, // GENERAL / SERIOUS
        string Outcome,
        string CausalityGrade,
        string? ReactionDescription
    );

    [HttpPost("reports")]
    public async Task<IActionResult> CreateReport([FromBody] CreateAdrReportDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.DrugName) || string.IsNullOrWhiteSpace(dto.ReactionName))
        {
            return BadRequest(ApiResponse<object>.Fail(400, "患者ID、药品名称与不良反应名称为必填项"));
        }

        long now = SystemTime.UtcNowMs;
        var report = new AdrReport
        {
            Id = Guid.NewGuid().ToString("D"),
            PatientId = dto.PatientId,
            EncounterOrVisitId = dto.EncounterOrVisitId,
            OrderOrRxId = dto.OrderOrRxId,
            DrugId = dto.DrugId,
            DrugName = dto.DrugName,
            BatchNo = dto.BatchNo,
            ReactionName = dto.ReactionName,
            OnsetTimeMs = dto.OnsetTimeMs > 0 ? dto.OnsetTimeMs : now,
            Severity = dto.Severity?.ToUpperInvariant() is "SERIOUS" ? "SERIOUS" : "GENERAL",
            Outcome = dto.Outcome ?? "好转",
            CausalityGrade = dto.CausalityGrade ?? "肯定",
            ReportStatus = "SUBMITTED_INTERNAL",
            ExtDataJson = System.Text.Json.JsonSerializer.Serialize(new { dto.ReactionDescription }),
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };

        // Auto-generate a drug risk alert rule in kb_clinical_rules
        try
        {
            using var conn = _connFactory.CreateConnection();
            string ruleId = Guid.NewGuid().ToString("D");
            string ruleCode = $"ADR-{dto.DrugName}-{Guid.NewGuid():N}"[..Math.Min(32, 5 + dto.DrugName.Length + 10)];
            string severity = report.Severity == "SERIOUS" ? "BLOCK" : "WARNING";
            string condDsl = System.Text.Json.JsonSerializer.Serialize(new
            {
                trigger_drug = dto.DrugName,
                target_patient_id = dto.PatientId,
                reaction = dto.ReactionName
            });
            string explanation = $"国家药品不良反应警示：患者曾发生对 [{dto.DrugName}] 的 [{dto.ReactionName}] 不良反应（严重程度: {report.Severity}，因果关系: {report.CausalityGrade}）。";
            string suggestion = $"建议停用或避免使用 [{dto.DrugName}]，必要时选用非同类替代药物，并密切监测生命体征与器官功能。";

            await conn.ExecuteAsync(
                """
                INSERT INTO kb_clinical_rules
                (id, rule_code, rule_domain, severity, condition_dsl_json, explanation_text, action_suggestion_text, evidence_doc_id, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @Code, 'ORDER', @Severity, @Dsl, @Exp, @Sugg, @DocId, 1, 1, @Now, 'ADR_SYSTEM', @Now, 'ADR_SYSTEM', 1)
                """,
                new
                {
                    Id = ruleId,
                    Code = ruleCode,
                    Severity = severity,
                    Dsl = condDsl,
                    Exp = explanation,
                    Sugg = suggestion,
                    DocId = report.Id,
                    Now = now
                });
            report.KbRuleGeneratedId = ruleId;
        }
        catch
        {
            // Non-blocking ADR rule registration
        }

        await _adrRepo.InsertAsync(report);

        if (_auditContract != null)
        {
            await _auditContract.RecordAuditAsync(
                "ADR_REPORT_CREATE",
                _userSession.UserId ?? "SYSTEM",
                _userSession.DisplayName ?? "SYSTEM",
                _userSession.ActiveDeptId,
                "adr_reports",
                report.Id,
                $"上报药品不良反应: [{dto.DrugName}] -> [{dto.ReactionName}]",
                ct: ct);
        }

        return Ok(ApiResponse<AdrReport>.Success(report, "药品不良反应上报成功，已自动关联全院临床知识库安全规则"));
    }

    [HttpGet("reports")]
    public async Task<IActionResult> ListReports(
        [FromQuery] string? patientId,
        [FromQuery] string? drugName,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        string sql = "SELECT * FROM adr_reports WHERE 1=1";
        var param = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(patientId))
        {
            sql += " AND patient_id = @PatientId";
            param.Add("PatientId", patientId);
        }
        if (!string.IsNullOrWhiteSpace(drugName))
        {
            sql += " AND drug_name LIKE @DrugName";
            param.Add("DrugName", $"%{drugName}%");
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND report_status = @Status";
            param.Add("Status", status);
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 100";

        var list = await _adrRepo.QueryAsync(sql, param);
        return Ok(ApiResponse<IReadOnlyList<AdrReport>>.Success(list));
    }

    [HttpPost("reports/{id}/submit-external")]
    public async Task<IActionResult> SubmitExternal(string id, CancellationToken ct)
    {
        var report = await _adrRepo.GetByIdAsync(id);
        if (report == null)
        {
            return NotFound(ApiResponse<object>.Fail(404, "未找到该不良反应报告"));
        }

        long now = SystemTime.UtcNowMs;
        report.ReportStatus = "REPORTED_EXTERNAL";
        report.ExternalReportNo = $"NMPA-ADR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..28].ToUpperInvariant();
        report.UpdatedAtMs = now;
        report.UpdatedBy = _userSession.UserId ?? "SYSTEM";
        await _adrRepo.UpdateAsync(report);

        if (_auditContract != null)
        {
            await _auditContract.RecordAuditAsync(
                "ADR_REPORT_EXTERNAL_SUBMIT",
                _userSession.UserId ?? "SYSTEM",
                _userSession.DisplayName ?? "SYSTEM",
                _userSession.ActiveDeptId,
                "adr_reports",
                report.Id,
                $"已向国家不良反应监测系统网关报送: 报告编号 {report.ExternalReportNo}",
                ct: ct);
        }

        return Ok(ApiResponse<object>.Success(new
        {
            reportId = report.Id,
            externalReportNo = report.ExternalReportNo,
            reportStatus = report.ReportStatus
        }, "已成功上报至国家药品不良反应监测中心监管网关"));
    }
}
