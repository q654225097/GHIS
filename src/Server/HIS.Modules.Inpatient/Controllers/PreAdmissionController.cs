using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Inpatient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 医保“预住院”全闭环控制器：
/// 覆盖预住院登记、医保白名单计费拦截、P0危急值抢占锁停、P1/P2准入评估、文书PRE_ADMIT双轨并卷、
/// 转正式住院合并记账、B诊断紧急收治+医生/财务复合拆分以及取消/超期自动转门诊结算。
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "IPD_ATTENDING", "IPD_RESIDENT", "CASHIER", "WARD_NURSE")]
[Route("api/v4/pre-admission")]
public sealed class PreAdmissionController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<IpdPreAdmissionConfig> _cfgRepo;
    private readonly IRepository<IpdPreAdmission> _mainRepo;
    private readonly IRepository<IpdPreAdmissionOrder> _orderRepo;
    private readonly IRepository<IpdPreAdmissionEvaluation> _evalRepo;
    private readonly IRepository<IpdPreAdmissionRule> _ruleRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public PreAdmissionController(
        IDbConnectionFactory connFactory,
        IRepository<IpdPreAdmissionConfig> cfgRepo,
        IRepository<IpdPreAdmission> mainRepo,
        IRepository<IpdPreAdmissionOrder> orderRepo,
        IRepository<IpdPreAdmissionEvaluation> evalRepo,
        IRepository<IpdPreAdmissionRule> ruleRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _cfgRepo = cfgRepo;
        _mainRepo = mainRepo;
        _orderRepo = orderRepo;
        _evalRepo = evalRepo;
        _ruleRepo = ruleRepo;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record UpdateConfigDto(
        int DefaultValidDays,
        int ExpiryWarnHours,
        int CriticalEscalateMinutes,
        int EvalEscalateHours,
        string AllowedFeeCategories,
        string BlockedFeeCategories,
        bool RequireInformedConsent = true,
        bool AllowDoctorPreSplit = true,
        bool RequireFinanceSplitAudit = true);

    public sealed record SaveRuleDto(
        string? Id,
        string RuleCode,
        string RuleName,
        string RuleLevel,
        string SourceDiagPrefix,
        string MatchKeyword,
        string TargetSpecialtyDept,
        string OnHitAction,
        string? Remark = null);

    public sealed record RegisterPreAdmissionDto(
        string PatientName,
        string Gender,
        int Age,
        string? MedicalRecordNo,
        string? SourceOpdVisitId,
        string? TargetDeptId,
        string TargetDeptName,
        string? PrimaryDiagCode,
        string PrimaryDiagName,
        string? PlannedSurgeryName,
        bool ConsentSigned,
        string? BundleId = null,
        string? Remark = null);

    public sealed record AddPreOrderDto(
        string FeeCategory, // LAB, EXAM, PREOP_PREP (BED/NURSING/IPD_CONSULT 将被硬拦截!)
        string ItemCode,
        string ItemName,
        string? ExecDeptName,
        decimal Amount);

    public sealed record TriggerCriticalDto(
        string? OrderId,
        string CriticalDiagName,
        string CriticalSummary);

    public sealed record DoctorSplitItemDto(string OrderId, string SuggestAction); // KEEP_IPD or SPLIT_OPD

    public sealed record EvaluatePreAdmissionDto(
        string Decision, // APPROVE_ADMIT, DEFER_PREP, CANCEL_TO_OPD, ESCALATE_EMERGENCY, SWITCH_TO_B_IPD, EXCLUDE_FALSE_CRITICAL
        string? NewPrimaryDiag,
        string? NewTargetDept,
        string? ReasonNote,
        List<DoctorSplitItemDto>? DoctorSplitSuggestions = null);

    public sealed record ConvertToInpatientDto(
        string? BedCode,
        string? WardName);

    public sealed record FinanceConfirmSplitItemDto(string OrderId, string FinalAction); // KEEP_IPD or SPLIT_OPD
    public sealed record FinanceConfirmSplitDto(List<FinanceConfirmSplitItemDto> Items);

    private async Task<IpdPreAdmissionConfig> GetEffectiveConfigAsync(CancellationToken ct)
    {
        return await _cfgRepo.GetByIdAsync("DEFAULT", ct) ?? new IpdPreAdmissionConfig { Id = "DEFAULT" };
    }

    // ===================== 1. 概览、可配置参数与三级规则 =====================

    [HttpGet("summary")]
    public async Task<ApiResponse<object>> GetSummary(CancellationToken ct)
    {
        var cfg = await GetEffectiveConfigAsync(ct);
        long warnDeadline = SystemTime.UtcNowMs + cfg.ExpiryWarnHours * 3600_000L;
        using var conn = _connFactory.CreateConnection();
        int active = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admissions WHERE status IN ('REGISTERED','CHECKING','PENDING_EVALUATION','CRITICAL_INTERRUPTED','READY_TO_ADMIT','DEFER_PREP')");
        int pendingEval = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admissions WHERE status = 'PENDING_EVALUATION'");
        int critical = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admissions WHERE status = 'CRITICAL_INTERRUPTED'");
        int readyAdmit = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admissions WHERE status = 'READY_TO_ADMIT'");
        int convertedIpd = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admissions WHERE status IN ('CONVERTED_IPD','ESCALATED_EMERGENCY')");
        int convertedOpd = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admissions WHERE status = 'CONVERTED_OPD'");
        int expiringSoon = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ipd_pre_admissions WHERE status IN ('REGISTERED','CHECKING','PENDING_EVALUATION','DEFER_PREP') AND valid_until_ms <= @Warn",
            new { Warn = warnDeadline });

        return ApiResponse<object>.Success(new
        {
            ActiveCount = active,
            PendingEvalCount = pendingEval,
            CriticalInterruptedCount = critical,
            ReadyToAdmitCount = readyAdmit,
            ConvertedIpdCount = convertedIpd,
            ConvertedOpdCount = convertedOpd,
            ExpiringSoonCount = expiringSoon,
            DefaultValidDays = cfg.DefaultValidDays,
            CriticalEscalateMinutes = cfg.CriticalEscalateMinutes,
            EvalEscalateHours = cfg.EvalEscalateHours
        });
    }

    [HttpGet("config")]
    public async Task<ApiResponse<IpdPreAdmissionConfig>> GetConfig(CancellationToken ct)
    {
        return ApiResponse<IpdPreAdmissionConfig>.Success(await GetEffectiveConfigAsync(ct));
    }

    [HttpPut("config")]
    public async Task<ApiResponse<IpdPreAdmissionConfig>> UpdateConfig([FromBody] UpdateConfigDto req, CancellationToken ct)
    {
        if (req.DefaultValidDays < 1 || req.DefaultValidDays > 60)
            return ApiResponse<IpdPreAdmissionConfig>.Fail(400, "预住院默认有效天数须在 1~60 天之间");
        if (req.CriticalEscalateMinutes < 5 || req.EvalEscalateHours < 1)
            return ApiResponse<IpdPreAdmissionConfig>.Fail(400, "超时升级阈值配置无效");

        var cfg = await GetEffectiveConfigAsync(ct);
        cfg.DefaultValidDays = req.DefaultValidDays;
        cfg.ExpiryWarnHours = Math.Clamp(req.ExpiryWarnHours, 6, 168);
        cfg.CriticalEscalateMinutes = req.CriticalEscalateMinutes;
        cfg.EvalEscalateHours = req.EvalEscalateHours;
        cfg.AllowedFeeCategories = string.IsNullOrWhiteSpace(req.AllowedFeeCategories) ? "LAB,EXAM,PREOP_PREP" : req.AllowedFeeCategories.Trim().ToUpperInvariant();
        cfg.BlockedFeeCategories = string.IsNullOrWhiteSpace(req.BlockedFeeCategories) ? "BED,NURSING,IPD_CONSULT" : req.BlockedFeeCategories.Trim().ToUpperInvariant();
        cfg.RequireInformedConsent = (short)(req.RequireInformedConsent ? 1 : 0);
        cfg.AllowDoctorPreSplit = (short)(req.AllowDoctorPreSplit ? 1 : 0);
        cfg.RequireFinanceSplitAudit = (short)(req.RequireFinanceSplitAudit ? 1 : 0);
        cfg.UpdatedAtMs = SystemTime.UtcNowMs;
        cfg.UpdatedBy = ActorId;
        await _cfgRepo.UpdateAsync(cfg, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("PREADMIT_CONFIG_UPDATE", "ipd_pre_admission_config", "DEFAULT", $"更新预住院参数：有效期 {cfg.DefaultValidDays} 天，危急值升级 {cfg.CriticalEscalateMinutes} 分钟，齐套升级 {cfg.EvalEscalateHours} 小时", ActorId, ActorName, null, ct);

        return ApiResponse<IpdPreAdmissionConfig>.Success(cfg, "预住院全局配置已保存并即时生效");
    }

    [HttpGet("rules")]
    public async Task<ApiResponse<IReadOnlyList<IpdPreAdmissionRule>>> GetRules()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<IpdPreAdmissionRule>(
            "SELECT * FROM ipd_pre_admission_rules WHERE is_active = 1 ORDER BY rule_level ASC, rule_code ASC")).ToList();
        return ApiResponse<IReadOnlyList<IpdPreAdmissionRule>>.Success(rows);
    }

    [HttpPost("rules")]
    public async Task<ApiResponse<IpdPreAdmissionRule>> SaveRule([FromBody] SaveRuleDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RuleName) || string.IsNullOrWhiteSpace(req.MatchKeyword))
            return ApiResponse<IpdPreAdmissionRule>.Fail(400, "规则名称和匹配关键词不能为空");

        long now = SystemTime.UtcNowMs;
        var existing = string.IsNullOrWhiteSpace(req.Id) ? null : await _ruleRepo.GetByIdAsync(req.Id, ct);
        if (existing == null)
        {
            var r = new IpdPreAdmissionRule
            {
                Id = "PA-R-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                RuleCode = string.IsNullOrWhiteSpace(req.RuleCode) ? "RULE-" + SystemTime.ShanghaiNow.ToString("HHmmss") : req.RuleCode.Trim().ToUpperInvariant(),
                RuleName = req.RuleName.Trim(),
                RuleLevel = string.IsNullOrWhiteSpace(req.RuleLevel) ? "P0" : req.RuleLevel.Trim().ToUpperInvariant(),
                SourceDiagPrefix = string.IsNullOrWhiteSpace(req.SourceDiagPrefix) ? "*" : req.SourceDiagPrefix.Trim(),
                MatchKeyword = req.MatchKeyword.Trim(),
                TargetSpecialtyDept = string.IsNullOrWhiteSpace(req.TargetSpecialtyDept) ? "急诊科" : req.TargetSpecialtyDept.Trim(),
                OnHitAction = string.IsNullOrWhiteSpace(req.OnHitAction) ? "INTERRUPT_AND_HOLD" : req.OnHitAction.Trim().ToUpperInvariant(),
                IsActive = 1,
                Remark = req.Remark?.Trim(),
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            };
            await _ruleRepo.InsertAsync(r, ct);
            return ApiResponse<IpdPreAdmissionRule>.Success(r, "预住院评估规则已新增");
        }
        else
        {
            existing.RuleName = req.RuleName.Trim();
            existing.RuleLevel = req.RuleLevel.Trim().ToUpperInvariant();
            existing.SourceDiagPrefix = req.SourceDiagPrefix.Trim();
            existing.MatchKeyword = req.MatchKeyword.Trim();
            existing.TargetSpecialtyDept = req.TargetSpecialtyDept.Trim();
            existing.OnHitAction = req.OnHitAction.Trim().ToUpperInvariant();
            existing.Remark = req.Remark?.Trim();
            existing.UpdatedAtMs = now;
            existing.UpdatedBy = ActorId;
            await _ruleRepo.UpdateAsync(existing, ct);
            return ApiResponse<IpdPreAdmissionRule>.Success(existing, "预住院评估规则已更新");
        }
    }

    [HttpDelete("rules/{id}")]
    public async Task<ApiResponse<object>> DisableRule(string id, CancellationToken ct)
    {
        var existing = await _ruleRepo.GetByIdAsync(id, ct);
        if (existing == null) return ApiResponse<object>.Fail(404, "规则不存在");
        existing.IsActive = 0;
        existing.UpdatedAtMs = SystemTime.UtcNowMs;
        await _ruleRepo.UpdateAsync(existing, ct);
        return ApiResponse<object>.Success(new { id }, "规则已停用");
    }

    // ===================== 2. 预住院列表、登记、白名单计费拦截 =====================

    [HttpGet("list")]
    public async Task<ApiResponse<IReadOnlyList<IpdPreAdmission>>> GetList([FromQuery] string? status = null, [FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = "SELECT * FROM ipd_pre_admissions WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            sql += " AND status = @Status";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (patient_name LIKE @Kw OR pre_admit_no LIKE @Kw OR primary_diag_name LIKE @Kw OR target_dept_name LIKE @Kw)";
        sql += " ORDER BY CASE status WHEN 'CRITICAL_INTERRUPTED' THEN 1 WHEN 'PENDING_EVALUATION' THEN 2 WHEN 'READY_TO_ADMIT' THEN 3 WHEN 'CHECKING' THEN 4 ELSE 5 END, registered_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<IpdPreAdmission>(sql, new { Status = status?.Trim(), Kw = $"%{keyword?.Trim()}%" })).ToList();
        return ApiResponse<IReadOnlyList<IpdPreAdmission>>.Success(rows);
    }

    [HttpGet("{id}")]
    public async Task<ApiResponse<object>> GetDetail(string id, CancellationToken ct)
    {
        var main = await _mainRepo.GetByIdAsync(id, ct);
        if (main == null) return ApiResponse<object>.Fail(404, "预住院主单不存在");
        using var conn = _connFactory.CreateConnection();
        var orders = (await conn.QueryAsync<IpdPreAdmissionOrder>("SELECT * FROM ipd_pre_admission_orders WHERE pre_admission_id = @Id ORDER BY created_at_ms ASC", new { Id = id })).ToList();
        var evals = (await conn.QueryAsync<IpdPreAdmissionEvaluation>("SELECT * FROM ipd_pre_admission_evaluations WHERE pre_admission_id = @Id ORDER BY evaluated_at_ms DESC", new { Id = id })).ToList();
        return ApiResponse<object>.Success(new { Main = main, Orders = orders, Evaluations = evals });
    }

    [HttpPost("register")]
    public async Task<ApiResponse<object>> Register([FromBody] RegisterPreAdmissionDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.PrimaryDiagName))
            return ApiResponse<object>.Fail(400, "患者姓名与拟入院主诊断（A诊断）不能为空");

        var cfg = await GetEffectiveConfigAsync(ct);
        if (cfg.RequireInformedConsent == 1 && !req.ConsentSigned)
            return ApiResponse<object>.Fail(400, "依据医保预住院规范，登记前必须签署《预住院患者知情同意书》");

        long now = SystemTime.UtcNowMs;
        string preId = Guid.NewGuid().ToString("D");
        string preNo = "PA" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);
        string consentDocId = "DOC-PAC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var main = new IpdPreAdmission
        {
            Id = preId,
            PreAdmitNo = preNo,
            PatientName = req.PatientName.Trim(),
            Gender = string.IsNullOrWhiteSpace(req.Gender) ? "男" : req.Gender.Trim(),
            Age = Math.Clamp(req.Age, 0, 120),
            MedicalRecordNo = string.IsNullOrWhiteSpace(req.MedicalRecordNo) ? "MR" + SystemTime.ShanghaiNow.ToString("yyMMddHHmm") : req.MedicalRecordNo.Trim(),
            SourceOpdVisitId = req.SourceOpdVisitId?.Trim(),
            TargetDeptId = req.TargetDeptId?.Trim(),
            TargetDeptName = string.IsNullOrWhiteSpace(req.TargetDeptName) ? "普外科" : req.TargetDeptName.Trim(),
            PrimaryDiagCode = req.PrimaryDiagCode?.Trim(),
            PrimaryDiagName = req.PrimaryDiagName.Trim(),
            PlannedSurgeryName = req.PlannedSurgeryName?.Trim(),
            ConsentSigned = (short)(req.ConsentSigned ? 1 : 0),
            ConsentDocId = consentDocId,
            ValidDays = cfg.DefaultValidDays,
            RegisteredAtMs = now,
            ValidUntilMs = now + cfg.DefaultValidDays * 86_400_000L,
            Status = "CHECKING",
            Remark = req.Remark?.Trim(),
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };

        int createdOrders = 0;
        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var conn = _connFactory.CreateConnection();
            await _mainRepo.InsertAsync(main, token);

            // 若选定了术前组合套餐（如肾功能全套），自动拆解生成院前暂挂检查项目 + 医技待书写任务
            if (!string.IsNullOrWhiteSpace(req.BundleId))
            {
                var items = (await conn.QueryAsync<dynamic>(
                    "SELECT * FROM cfg_order_bundle_items WHERE bundle_id = @Bid ORDER BY sort_order ASC",
                    new { Bid = req.BundleId.Trim() })).ToList();
                foreach (var it in items)
                {
                    string rptId = Guid.NewGuid().ToString("D");
                    string deptName = (string?)it.exec_dept_name ?? "检验科";
                    string dCode = deptName.Contains("超声") ? "US" : deptName.Contains("放射") ? "RAD" : deptName.Contains("心电") ? "ECG" : "LAB";
                    string itemCode = (string)it.item_code;
                    string itemName = (string)it.item_name;
                    decimal amt = Convert.ToDecimal(it.subtotal_amount ?? it.unit_price ?? 0m);

                    await _orderRepo.InsertAsync(new IpdPreAdmissionOrder
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        PreAdmissionId = preId,
                        BundleId = req.BundleId.Trim(),
                        ReportId = rptId,
                        FeeCategory = dCode == "LAB" ? "LAB" : "EXAM",
                        ItemCode = itemCode,
                        ItemName = itemName,
                        ExecDeptName = deptName,
                        Amount = amt,
                        ExecStatus = "PENDING",
                        BillingTrack = "PRE_ADMIT_HOLD",
                        DoctorSplitSuggest = "KEEP_IPD",
                        FinanceSplitConfirm = "PENDING",
                        SchemaVersion = 1,
                        CreatedAtMs = now,
                        CreatedBy = ActorId,
                        UpdatedAtMs = now,
                        UpdatedBy = ActorId,
                        VersionNo = 1
                    }, token);

                    // 同步推送到医技报告工作站待书写队列，并绑定 pre_admission_id
                    await conn.ExecuteAsync(@"
                        INSERT INTO mtc_diagnostic_reports
                        (id, report_no, request_no, patient_name, gender, age, medical_record_no, order_dept_name, medtech_dept_code, medtech_dept_name, exam_item_code, exam_item_name, result_source, status, content_json, pre_admission_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                        VALUES
                        (@Id, @RptNo, @ReqNo, @PName, @Gender, @Age, @Mrn, @ODept, @DCode, @DName, @ICode, @IName, 'MANUAL', 'PENDING_WRITE', '{}', @PreId, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new
                        {
                            Id = rptId,
                            RptNo = "RPT-" + dCode + "-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
                            ReqNo = preNo,
                            PName = main.PatientName,
                            main.Gender,
                            main.Age,
                            Mrn = main.MedicalRecordNo,
                            ODept = main.TargetDeptName + "(预住院)",
                            DCode = dCode,
                            DName = deptName,
                            ICode = itemCode,
                            IName = itemName,
                            PreId = preId,
                            Now = now,
                            Actor = ActorId
                        });
                    createdOrders++;
                }
            }

            if (createdOrders > 0)
            {
                main.TotalOrderCount = createdOrders;
                await _mainRepo.UpdateAsync(main, token);
            }

            // 写入 PRE_ADMIT 场景电子病历知情同意书存证（不触碰正式住院病历）
            await conn.ExecuteAsync(@"
                INSERT OR IGNORE INTO emr_documents
                (id, doc_no, emr_scene, visit_or_encounter_id, patient_id, dept_id, template_id, template_version, doc_type_code, doc_title, clinical_time_ms, plain_text_summary, status, author_id, pre_admission_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @DocNo, 'PRE_ADMIT', @PreId, @Mrn, @Dept, 'TPL-PRE-CONSENT', 1, 'PRE_ADMIT_CONSENT', '预住院患者知情同意书', @Now, @Summary, 2, @Actor, @PreId, 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = consentDocId,
                    DocNo = "EMR-PAC-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss"),
                    PreId = preId,
                    Mrn = main.MedicalRecordNo,
                    Dept = main.TargetDeptName,
                    Now = now,
                    Summary = $"患者 {main.PatientName} 已签署《预住院知情同意书》，拟入院诊断：{main.PrimaryDiagName}，有效期 {cfg.DefaultValidDays} 天；已知晓如未转正式住院费用按门诊政策结算。",
                    Actor = ActorId
                });
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("PREADMIT_REGISTER", "ipd_pre_admissions", preId, $"预住院登记：{main.PatientName}（{preNo}，A诊断：{main.PrimaryDiagName}，有效期 {cfg.DefaultValidDays} 天，已拆解 {createdOrders} 项术前检查）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Main = main, OrderCount = createdOrders }, $"预住院登记成功（有效期 {cfg.DefaultValidDays} 天），已生成 {createdOrders} 项院前暂挂检查");
    }

    /// <summary>
    /// 院前加开检查/准备项目（强执行医保白名单/禁收拦截：严禁床位费、护理费、住院诊查费）
    /// </summary>
    [HttpPost("{id}/orders")]
    public async Task<ApiResponse<IpdPreAdmissionOrder>> AddPreOrder(string id, [FromBody] AddPreOrderDto req, CancellationToken ct)
    {
        var main = await _mainRepo.GetByIdAsync(id, ct);
        if (main == null) return ApiResponse<IpdPreAdmissionOrder>.Fail(404, "预住院主单不存在");
        if (main.Status == "CRITICAL_INTERRUPTED")
            return ApiResponse<IpdPreAdmissionOrder>.Fail(409, "当前患者处于【P0危急值中断】状态，禁止继续加开常规术前检查，请先处置危急值！");
        if (main.Status is "CONVERTED_IPD" or "CONVERTED_OPD")
            return ApiResponse<IpdPreAdmissionOrder>.Fail(409, "预住院已终结，无法继续加开院前项目");

        var cfg = await GetEffectiveConfigAsync(ct);
        string cat = (req.FeeCategory ?? "LAB").Trim().ToUpperInvariant();
        var blocked = cfg.BlockedFeeCategories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allowed = cfg.AllowedFeeCategories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (blocked.Contains(cat, StringComparer.OrdinalIgnoreCase) || !allowed.Contains(cat, StringComparer.OrdinalIgnoreCase))
            return ApiResponse<IpdPreAdmissionOrder>.Fail(400, $"医保合规拦截：预住院期间严禁收取 [{cat}] 类别费用（床位/护理/住院诊查费禁收），仅允许 {cfg.AllowedFeeCategories}");

        long now = SystemTime.UtcNowMs;
        var ord = new IpdPreAdmissionOrder
        {
            Id = Guid.NewGuid().ToString("D"),
            PreAdmissionId = id,
            FeeCategory = cat,
            ItemCode = string.IsNullOrWhiteSpace(req.ItemCode) ? "PRE-" + SystemTime.ShanghaiNow.ToString("HHmmss") : req.ItemCode.Trim(),
            ItemName = req.ItemName.Trim(),
            ExecDeptName = string.IsNullOrWhiteSpace(req.ExecDeptName) ? "检验科" : req.ExecDeptName.Trim(),
            Amount = Math.Max(0, req.Amount),
            ExecStatus = "PENDING",
            BillingTrack = "PRE_ADMIT_HOLD",
            DoctorSplitSuggest = "KEEP_IPD",
            FinanceSplitConfirm = "PENDING",
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        await _orderRepo.InsertAsync(ord, ct);

        main.TotalOrderCount++;
        if (main.Status == "PENDING_EVALUATION") main.Status = "CHECKING";
        main.UpdatedAtMs = now;
        await _mainRepo.UpdateAsync(main, ct);

        return ApiResponse<IpdPreAdmissionOrder>.Success(ord, "已开立院前检查并记入 PRE_ADMIT_HOLD 暂挂轨道");
    }

    // ===================== 3. P0 危急值抢占中断（不等剩余检查完成） =====================

    [HttpPost("{id}/trigger-critical")]
    public async Task<ApiResponse<object>> TriggerCriticalInterrupt(string id, [FromBody] TriggerCriticalDto req, CancellationToken ct)
    {
        var main = await _mainRepo.GetByIdAsync(id, ct);
        if (main == null) return ApiResponse<object>.Fail(404, "预住院主单不存在");

        long now = SystemTime.UtcNowMs;
        int heldCount = 0;
        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var conn = _connFactory.CreateConnection();
            if (!string.IsNullOrWhiteSpace(req.OrderId))
            {
                await conn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET exec_status = 'DONE', is_critical = 1, result_summary = @Sum, updated_at_ms = @Now WHERE id = @Oid",
                    new { Sum = req.CriticalSummary, Now = now, Oid = req.OrderId.Trim() });
            }

            // 核心安全红线：立即将所有剩余未执行检查项目加 HOLD_BY_CRITICAL 锁，防止患者带危急值去做高危检查！
            heldCount = await conn.ExecuteAsync(
                "UPDATE ipd_pre_admission_orders SET exec_status = 'HOLD_BY_CRITICAL', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status = 'PENDING'",
                new { Now = now, Pid = id });

            int doneCnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_pre_admission_orders WHERE pre_admission_id = @Pid AND exec_status = 'DONE'", new { Pid = id });
            main.DoneOrderCount = doneCnt;
            main.HasCriticalAlert = 1;
            main.CriticalDiagName = string.IsNullOrWhiteSpace(req.CriticalDiagName) ? "检出B诊断危急值" : req.CriticalDiagName.Trim();
            main.CriticalSummary = req.CriticalSummary?.Trim();
            main.CriticalTriggeredAtMs = now;
            main.Status = "CRITICAL_INTERRUPTED";
            main.UpdatedAtMs = now;
            main.UpdatedBy = ActorId;
            await _mainRepo.UpdateAsync(main, token);
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("PREADMIT_P0_CRITICAL", "ipd_pre_admissions", id, $"P0危急值抢占中断：{main.PatientName}（原A诊断：{main.PrimaryDiagName} → 触发B危急值：{main.CriticalDiagName}），已自动锁停 {heldCount} 项未执行检查", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Main = main, HeldOrderCount = heldCount }, $"已触发 P0 危急值中断并自动锁停 {heldCount} 项未执行检查！");
    }

    // ===================== 4. 结果导向准入评估与危急值处置（含医生建议复合拆分） =====================

    [HttpPost("{id}/evaluate")]
    public async Task<ApiResponse<object>> Evaluate(string id, [FromBody] EvaluatePreAdmissionDto req, CancellationToken ct)
    {
        var main = await _mainRepo.GetByIdAsync(id, ct);
        if (main == null) return ApiResponse<object>.Fail(404, "预住院主单不存在");

        string decision = (req.Decision ?? string.Empty).Trim().ToUpperInvariant();
        long now = SystemTime.UtcNowMs;
        string evalId = Guid.NewGuid().ToString("D");
        string sigHash = CryptoHelper.ComputeSm3($"{id}|{decision}|{req.ReasonNote}|{ActorId}|{now}");

        string msg = "评估决策已生效";
        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var conn = _connFactory.CreateConnection();

            // 1. 记录带 SM3 签名的结构化评估单
            await _evalRepo.InsertAsync(new IpdPreAdmissionEvaluation
            {
                Id = evalId,
                PreAdmissionId = id,
                EvalType = main.Status == "CRITICAL_INTERRUPTED" ? "CRITICAL_HANDLE" : "ROUTINE_EVAL",
                RuleLevel = main.Status == "CRITICAL_INTERRUPTED" ? "P0" : "P2",
                Decision = decision,
                NewPrimaryDiag = req.NewPrimaryDiag?.Trim(),
                NewTargetDept = req.NewTargetDept?.Trim(),
                ReasonNote = req.ReasonNote?.Trim(),
                EvaluatorId = ActorId,
                EvaluatorName = ActorName,
                SignatureHash = sigHash,
                EvaluatedAtMs = now,
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            }, token);

            // 2. 按决策分支执行对应原子闭环动作
            if (decision == "APPROVE_ADMIT")
            {
                main.Status = "READY_TO_ADMIT";
                msg = "评估通过，已推送至入院准备中心待安排正式占床";
            }
            else if (decision == "DEFER_PREP")
            {
                main.Status = "DEFER_PREP";
                msg = "已暂缓住院并进入院前调理，请注意剩余医保有效期";
            }
            else if (decision == "EXCLUDE_FALSE_CRITICAL")
            {
                // 排除假危急值 → 解锁 HOLD_BY_CRITICAL 恢复正常检查
                await conn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET exec_status = 'PENDING', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status = 'HOLD_BY_CRITICAL'",
                    new { Now = now, Pid = id });
                main.HasCriticalAlert = 0;
                main.Status = (main.TotalOrderCount > 0 && main.DoneOrderCount >= main.TotalOrderCount) ? "PENDING_EVALUATION" : "CHECKING";
                msg = "已确认排除假危急值，剩余检查已解锁恢复常规预住院流程";
            }
            else if (decision == "CANCEL_TO_OPD")
            {
                // 无需住院/放弃 → 未做撤单退费，已做全部转回原门诊结算轨道
                await conn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET exec_status = 'CANCELLED', billing_track = 'REFUNDED', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status IN ('PENDING','HOLD_BY_CRITICAL')",
                    new { Now = now, Pid = id });
                await conn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET billing_track = 'SPLIT_TO_OPD', doctor_split_suggest = 'SPLIT_OPD', finance_split_confirm = 'SPLIT_OPD', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status = 'DONE'",
                    new { Now = now, Pid = id });
                main.Status = "CONVERTED_OPD";
                // 闭环：将拆回门诊结算的已执行院前检查费写入 fin_charge_ledgers 供收费处结算
                var opdOrders = await conn.QueryAsync<dynamic>("SELECT id AS Id, item_code AS ItemCode, item_name AS ItemName, amount AS Amount, exec_dept_name AS ExecDept FROM ipd_pre_admission_orders WHERE pre_admission_id = @Pid AND exec_status = 'DONE'", new { Pid = id });
                foreach (var po in opdOrders)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO fin_charge_ledgers
                        (id, patient_id, visit_or_encounter_id, order_dept_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                        VALUES
                        (@Id, @Pat, @Enc, 'PRE_ADMIT_OPD', @Exec, @Code, @Name, 1, @Amt, @Amt, 1, @Ext, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = Guid.NewGuid().ToString("D"), Pat = main.PatientName, Enc = id, Exec = (string?)po.ExecDept ?? "MEDTECH", Code = (string)po.ItemCode, Name = $"[预住院转门诊] {(string)po.ItemName}", Amt = Convert.ToDecimal(po.Amount), Ext = $"{{\"preAdmissionId\":\"{id}\",\"track\":\"SPLIT_TO_OPD\"}}", Now = now, Actor = ActorId });
                }
                msg = "预住院已中止：未执行项已自动退费，已执行项已转回原门诊结算，院前文书归档至门诊病历";
            }
            else if (decision is "SWITCH_TO_B_IPD" or "ESCALATE_EMERGENCY")
            {
                // B诊断紧急收治：批量撤消未做项目 + 保存医生复合拆分建议 + 直接建正式住院流水并切换主诊断/科室
                await conn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET exec_status = 'CANCELLED', billing_track = 'REFUNDED', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status IN ('PENDING','HOLD_BY_CRITICAL')",
                    new { Now = now, Pid = id });

                if (req.DoctorSplitSuggestions != null)
                {
                    foreach (var s in req.DoctorSplitSuggestions)
                    {
                        string sug = s.SuggestAction == "SPLIT_OPD" ? "SPLIT_OPD" : "KEEP_IPD";
                        await conn.ExecuteAsync(
                            "UPDATE ipd_pre_admission_orders SET doctor_split_suggest = @Sug, updated_at_ms = @Now WHERE id = @Oid AND pre_admission_id = @Pid",
                            new { Sug = sug, Now = now, Oid = s.OrderId, Pid = id });
                    }
                }

                // 暂按医生建议初始化 billing_track，等待财务/医保岗复核最终确认（复合拆分）
                await conn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET billing_track = CASE WHEN doctor_split_suggest = 'SPLIT_OPD' THEN 'SPLIT_TO_OPD' ELSE 'MERGED_TO_IPD' END, updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status = 'DONE'",
                    new { Now = now, Pid = id });

                string encId = "ENC-EMG-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss");
                main.ConvertedEncounterId = encId;
                main.ConvertedDeptName = string.IsNullOrWhiteSpace(req.NewTargetDept) ? "急诊科/心内科" : req.NewTargetDept.Trim();
                main.ConvertedPrimaryDiag = string.IsNullOrWhiteSpace(req.NewPrimaryDiag) ? (main.CriticalDiagName ?? main.PrimaryDiagName) : req.NewPrimaryDiag.Trim();
                main.AdmittedAtMs = now;
                main.Status = decision == "ESCALATE_EMERGENCY" ? "ESCALATED_EMERGENCY" : "CONVERTED_IPD";

                // 将 PRE_ADMIT 电子病历文书逻辑并卷到新住院流水（严禁修改原始签名时间戳！）
                await conn.ExecuteAsync(
                    "UPDATE emr_documents SET visit_or_encounter_id = @EncId WHERE pre_admission_id = @Pid",
                    new { EncId = encId, Pid = id });

                msg = $"已按 B 诊断 [{main.ConvertedPrimaryDiag}] 紧急转入 [{main.ConvertedDeptName}]（流水号 {encId}），未执行项已退费，待财务/医保岗复核费用复合拆分";
            }

            main.UpdatedAtMs = now;
            main.UpdatedBy = ActorId;
            await _mainRepo.UpdateAsync(main, token);
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("PREADMIT_EVALUATE", "ipd_pre_admissions", id, $"预住院评估决策 [{decision}]：{main.PatientName} — {msg}", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Main = main, EvalId = evalId }, msg);
    }

    /// <summary>
    /// 常规准予住院后，在入院准备中心一键办理正式入科占床（合并暂挂费用 + 并卷院前文书 + 锚定正式入院时间）
    /// </summary>
    [HttpPost("{id}/convert-to-inpatient")]
    public async Task<ApiResponse<object>> ConvertToInpatient(string id, [FromBody] ConvertToInpatientDto? req, CancellationToken ct)
    {
        var main = await _mainRepo.GetByIdAsync(id, ct);
        if (main == null) return ApiResponse<object>.Fail(404, "预住院主单不存在");
        if (main.Status == "CRITICAL_INTERRUPTED")
            return ApiResponse<object>.Fail(409, "患者存在未处置的 P0 危急值，请先完成危急值处置决策");

        long now = SystemTime.UtcNowMs;
        string encId = "ENC-IPD-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss");
        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var conn = _connFactory.CreateConnection();
            await conn.ExecuteAsync(
                "UPDATE ipd_pre_admission_orders SET billing_track = 'MERGED_TO_IPD', finance_split_confirm = 'KEEP_IPD', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status = 'DONE'",
                new { Now = now, Pid = id });
            await conn.ExecuteAsync(
                "UPDATE ipd_pre_admission_orders SET exec_status = 'CANCELLED', billing_track = 'REFUNDED', updated_at_ms = @Now WHERE pre_admission_id = @Pid AND exec_status = 'PENDING'",
                new { Now = now, Pid = id });
            await conn.ExecuteAsync(
                "UPDATE emr_documents SET visit_or_encounter_id = @EncId WHERE pre_admission_id = @Pid",
                new { EncId = encId, Pid = id });

            main.ConvertedEncounterId = encId;
            main.ConvertedDeptName = string.IsNullOrWhiteSpace(req?.WardName) ? main.TargetDeptName : req.WardName.Trim();
            main.ConvertedPrimaryDiag = main.PrimaryDiagName;
            main.AdmittedAtMs = now; // 病历质控 8h/24h/48h 及平均住院日严格从此时间起算！
            main.Status = "CONVERTED_IPD";
            // 闭环：自动签发正式住院入院证 ipd_admission_cards 并将 MERGED_TO_IPD 院前检查费写入住院费用台账 fin_charge_ledgers
            string paCardNo = "ADM-PA-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);
            await conn.ExecuteAsync(@"
                INSERT INTO ipd_admission_cards
                (id, card_no, request_key, patient_id, source_doctor_id, source_dept_id, suggested_ipd_dept_id, admission_diagnosis, remark, status, encounter_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @CardNo, @ReqKey, @Pat, @Actor, 'PRE_ADMIT', @Dept, @Diag, @Remark, 'REGISTERED', @EncId, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = Guid.NewGuid().ToString("D"), CardNo = paCardNo, ReqKey = "PA_ADM_" + id, Pat = main.PatientName, Actor = ActorId, Dept = main.ConvertedDeptName ?? main.TargetDeptName, Diag = main.ConvertedPrimaryDiag ?? main.PrimaryDiagName, Remark = $"预住院 {main.PreAdmitNo} 转正式住院", EncId = encId, Now = now });

            var ipdOrders = await conn.QueryAsync<dynamic>("SELECT id AS Id, item_code AS ItemCode, item_name AS ItemName, amount AS Amount, exec_dept_name AS ExecDept FROM ipd_pre_admission_orders WHERE pre_admission_id = @Pid AND exec_status = 'DONE' AND billing_track = 'MERGED_TO_IPD'", new { Pid = id });
            foreach (var po in ipdOrders)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO fin_charge_ledgers
                    (id, patient_id, visit_or_encounter_id, order_dept_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES
                    (@Id, @Pat, @Enc, @Dept, @Exec, @Code, @Name, 1, @Amt, @Amt, 1, @Ext, 1, @Now, @Actor, @Now, @Actor, 1)",
                    new { Id = Guid.NewGuid().ToString("D"), Pat = main.PatientName, Enc = encId, Dept = main.ConvertedDeptName ?? main.TargetDeptName, Exec = (string?)po.ExecDept ?? "MEDTECH", Code = (string)po.ItemCode, Name = $"[预住院并入住院] {(string)po.ItemName}", Amt = Convert.ToDecimal(po.Amount), Ext = $"{{\"preAdmissionId\":\"{id}\",\"track\":\"MERGED_TO_IPD\"}}", Now = now, Actor = ActorId });
            }
            main.UpdatedAtMs = now;
            main.UpdatedBy = ActorId;
            await _mainRepo.UpdateAsync(main, token);
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("PREADMIT_CONVERT_IPD", "ipd_pre_admissions", id, $"预住院转正式占床入科：{main.PatientName} → {main.ConvertedDeptName}（住院流水 {encId}，床号 {req?.BedCode ?? "待分配"}）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Main = main, EncounterId = encId }, $"已正式入科（住院流水 {encId}），院前检查费用已合并入住院统筹，院前文书已无损并卷");
    }

    /// <summary>
    /// 复合拆分第二道关口：财务/医保审核岗复核并最终确认每条已执行费用的结算归属（合并住院 KEEP_IPD vs 拆回门诊 SPLIT_OPD）
    /// </summary>
    [HttpPost("{id}/finance-confirm-split")]
    public async Task<ApiResponse<object>> FinanceConfirmSplit(string id, [FromBody] FinanceConfirmSplitDto req, CancellationToken ct)
    {
        var main = await _mainRepo.GetByIdAsync(id, ct);
        if (main == null) return ApiResponse<object>.Fail(404, "预住院主单不存在");
        if (req.Items == null || req.Items.Count == 0)
            return ApiResponse<object>.Fail(400, "请提交需要复核确认的费用明细");

        long now = SystemTime.UtcNowMs;
        int ipdCnt = 0, opdCnt = 0;
        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var conn = _connFactory.CreateConnection();
            foreach (var it in req.Items)
            {
                bool toOpd = string.Equals(it.FinalAction, "SPLIT_OPD", StringComparison.OrdinalIgnoreCase);
                string finConfirm = toOpd ? "SPLIT_OPD" : "KEEP_IPD";
                string track = toOpd ? "SPLIT_TO_OPD" : "MERGED_TO_IPD";
                if (toOpd) opdCnt++; else ipdCnt++;
                await conn.ExecuteAsync(
                    "UPDATE ipd_pre_admission_orders SET finance_split_confirm = @Fin, billing_track = @Track, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Oid AND pre_admission_id = @Pid",
                    new { Fin = finConfirm, Track = track, Now = now, Actor = ActorId, Oid = it.OrderId, Pid = id });
            }
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("PREADMIT_FINANCE_SPLIT", "ipd_pre_admissions", id, $"财务/医保复合拆分确认：{main.PatientName} — 合并入住院 {ipdCnt} 项，拆回门诊结算 {opdCnt} 项", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { MergedToIpdCount = ipdCnt, SplitToOpdCount = opdCnt }, $"财务/医保复合拆分已终审：合并入住院 {ipdCnt} 项，拆回门诊 {opdCnt} 项");
    }
}
