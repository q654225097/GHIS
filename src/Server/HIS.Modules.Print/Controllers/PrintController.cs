using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Print.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Print.Controllers;

[ApiController]
[Route("api/v4/print")]
public sealed class PrintController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<PrtTemplate> _tplRepo;
    private readonly IRepository<PrtTriggerBinding> _triggerRepo;
    private readonly IRepository<PrtPrintLog> _logRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public PrintController(
        IDbConnectionFactory connFactory,
        IRepository<PrtTemplate> tplRepo,
        IRepository<PrtTriggerBinding> triggerRepo,
        IRepository<PrtPrintLog> logRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _tplRepo = tplRepo;
        _triggerRepo = triggerRepo;
        _logRepo = logRepo;
        _session = session;
        _auditTrail = auditTrail;
    }

    public sealed record PrintFieldMeta(string Path, string Label, string Group, string SampleValue);
    public sealed record PrintSlotMeta(string SlotKey, string SlotName, string Description);
    public sealed record PrintPageDataSourceMeta(
        string PageKey,
        string PageName,
        IReadOnlyList<PrintSlotMeta> SupportedSlots,
        IReadOnlyList<PrintFieldMeta> AvailableFields);

    public sealed record SaveTemplateRequest(
        string? Id,
        string TemplateCode,
        string TemplateName,
        string Category,
        string PageKey,
        decimal PaperWidthMm,
        decimal PaperHeightMm,
        string Orientation,
        decimal MarginTopMm,
        decimal MarginRightMm,
        decimal MarginBottomMm,
        decimal MarginLeftMm,
        string LayoutJson,
        bool IsPublished = true,
        bool IsActive = true,
        string? Remark = null);

    public sealed record SaveTriggerRequest(
        string? Id,
        string TemplateId,
        string PageKey,
        string TriggerSlot,
        string ButtonLabel,
        string? ButtonIcon = "🖨️",
        string? ConditionExpr = null,
        int SortOrder = 10,
        bool IsActive = true);

    public sealed record RecordPrintJobRequest(
        string TemplateId,
        string TemplateCode,
        string TemplateName,
        string PageKey,
        string TriggerSlot,
        string? BusinessKey,
        string? PatientName,
        string? RenderedContextJson,
        int Copies = 1,
        string? PrinterName = null);

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    private static readonly IReadOnlyList<PrintSlotMeta> StandardSlots = new[]
    {
        new PrintSlotMeta("TOP_TOOLBAR", "页面顶部工具栏", "显示在当前工作站页面顶部操作栏"),
        new PrintSlotMeta("LIST_ROW", "列表选中行/卡片操作区", "显示在每条业务记录右侧操作区，直接取该行数据"),
        new PrintSlotMeta("DETAIL_FOOTER", "详情面板底部", "显示在右侧/底部详情区，取当前选中患者与明细")
    };

    private static readonly IReadOnlyList<PrintFieldMeta> CommonSystemFields = new[]
    {
        new PrintFieldMeta("system.printTime", "打印时间", "系统公共", "2026-11-20 14:30"),
        new PrintFieldMeta("system.printDate", "打印日期", "系统公共", "2026-11-20"),
        new PrintFieldMeta("system.operatorName", "打印操作员", "系统公共", "张医生"),
        new PrintFieldMeta("system.hospitalName", "医院名称", "系统公共", "GHIS 示范医院")
    };

    /// <summary>
    /// 获取各业务页面的可绑定打印数据源字段与支持的触发位置元数据
    /// </summary>
    [HttpGet("datasources")]
    public ApiResponse<IReadOnlyList<PrintPageDataSourceMeta>> GetDataSources()
    {
        var list = new List<PrintPageDataSourceMeta>
        {
            new("medtech", "检验检查工作站", StandardSlots, new[]
            {
                new PrintFieldMeta("patient.fullName", "患者姓名", "当前选中患者", "张三"),
                new PrintFieldMeta("patient.gender", "患者性别", "当前选中患者", "男"),
                new PrintFieldMeta("patient.age", "患者年龄", "当前选中患者", "45"),
                new PrintFieldMeta("patient.medicalRecordNo", "病历号", "当前选中患者", "MR20260001"),
                new PrintFieldMeta("patient.visitNo", "就诊号", "当前选中患者", "OPD20261120001"),
                new PrintFieldMeta("medtech.requestNo", "申请单号/条码号", "选中检验检查项目", "MTC202611200001"),
                new PrintFieldMeta("medtech.itemName", "检验检查项目名称", "选中检验检查项目", "血常规五分类+CRP"),
                new PrintFieldMeta("medtech.deptName", "开单科室", "选中检验检查项目", "呼吸内科门诊"),
                new PrintFieldMeta("medtech.execLocation", "执行地点/窗口", "选中检验检查项目", "门诊二楼检验科2号窗"),
                new PrintFieldMeta("medtech.fee", "费用金额", "选中检验检查项目", "¥45.00"),
                new PrintFieldMeta("medtech.status", "当前执行状态", "选中检验检查项目", "已确费锁单")
            }.Concat(CommonSystemFields).ToList()),

            new("opd_clinic", "门诊医生工作站", StandardSlots, new[]
            {
                new PrintFieldMeta("patient.fullName", "患者姓名", "当前接诊患者", "张三"),
                new PrintFieldMeta("patient.gender", "患者性别", "当前接诊患者", "男"),
                new PrintFieldMeta("patient.age", "患者年龄", "当前接诊患者", "45"),
                new PrintFieldMeta("patient.medicalRecordNo", "病历号", "当前接诊患者", "MR20260001"),
                new PrintFieldMeta("patient.visitNo", "门诊就诊号", "当前接诊患者", "OPD20261120001"),
                new PrintFieldMeta("patient.queueNo", "候诊号", "当前接诊患者", "A012"),
                new PrintFieldMeta("patient.deptName", "就诊科室", "当前接诊患者", "心内科门诊"),
                new PrintFieldMeta("patient.doctorName", "接诊医生", "当前接诊患者", "李主任"),
                new PrintFieldMeta("patient.feeType", "费别", "当前接诊患者", "城镇职工医保"),
                new PrintFieldMeta("patient.chiefComplaint", "主诉", "当前接诊患者", "反复胸闷3天"),
                new PrintFieldMeta("patient.allergyTags", "过敏史标签", "当前接诊患者", "青霉素过敏")
            }.Concat(CommonSystemFields).ToList()),

            new("ipd_nurse", "住院护士站", StandardSlots, new[]
            {
                new PrintFieldMeta("ipd.patientName", "住院患者姓名", "当前选中住院患者", "张三"),
                new PrintFieldMeta("ipd.gender", "性别", "当前选中住院患者", "男"),
                new PrintFieldMeta("ipd.age", "年龄", "当前选中住院患者", "58"),
                new PrintFieldMeta("ipd.admissionNo", "住院号", "当前选中住院患者", "IPD202611008"),
                new PrintFieldMeta("ipd.medicalRecordNo", "病案号", "当前选中住院患者", "MR20260008"),
                new PrintFieldMeta("ipd.bedCode", "床号", "当前选中住院患者", "08床"),
                new PrintFieldMeta("ipd.admittingDept", "入院科室", "当前选中住院患者", "普外科"),
                new PrintFieldMeta("ipd.nursingWard", "所在病区", "当前选中住院患者", "外科一病区"),
                new PrintFieldMeta("ipd.diagnosis", "入院诊断", "当前选中住院患者", "急性胆囊炎"),
                new PrintFieldMeta("ipd.attendingDoctor", "管床医生", "当前选中住院患者", "王主治"),
                new PrintFieldMeta("ipd.admissionDate", "入院日期", "当前选中住院患者", "2026-11-18")
            }.Concat(CommonSystemFields).ToList()),

            new("ipd_doctor", "住院医生站", StandardSlots, new[]
            {
                new PrintFieldMeta("ipd.patientName", "住院患者姓名", "当前选中住院患者", "张三"),
                new PrintFieldMeta("ipd.gender", "性别", "当前选中住院患者", "男"),
                new PrintFieldMeta("ipd.age", "年龄", "当前选中住院患者", "58"),
                new PrintFieldMeta("ipd.admissionNo", "住院号", "当前选中住院患者", "IPD202611008"),
                new PrintFieldMeta("ipd.bedCode", "床号", "当前选中住院患者", "08床"),
                new PrintFieldMeta("ipd.admittingDept", "科室", "当前选中住院患者", "普外科"),
                new PrintFieldMeta("ipd.nursingWard", "病区", "当前选中住院患者", "外科一病区"),
                new PrintFieldMeta("ipd.diagnosis", "诊断", "当前选中住院患者", "急性胆囊炎"),
                new PrintFieldMeta("ipd.attendingDoctor", "主管医师", "当前选中住院患者", "王主治")
            }.Concat(CommonSystemFields).ToList()),

            new("pharmacy_window", "药房发药窗口", StandardSlots, new[]
            {
                new PrintFieldMeta("pharmacy.prescriptionNo", "处方号", "选中发药处方", "RX20261120005"),
                new PrintFieldMeta("pharmacy.patientName", "患者姓名", "选中发药处方", "张三"),
                new PrintFieldMeta("pharmacy.drugSummary", "药品摘要", "选中发药处方", "阿莫西林胶囊 0.5g×24粒"),
                new PrintFieldMeta("pharmacy.dispenseQuantity", "发药数量", "选中发药处方", "2"),
                new PrintFieldMeta("pharmacy.totalAmount", "处方金额", "选中发药处方", "45.00"),
                new PrintFieldMeta("pharmacy.paymentOrderNo", "缴费单号", "选中发药处方", "PAY20261120005")
            }.Concat(CommonSystemFields).ToList()),

            new("pre_admission", "预住院与入院准备中心", StandardSlots, new[]
            {
                new PrintFieldMeta("preadmit.preAdmitNo", "预住院号", "当前选中预住院单", "PA202611210001"),
                new PrintFieldMeta("preadmit.patientName", "患者姓名", "当前选中预住院单", "张三"),
                new PrintFieldMeta("preadmit.gender", "性别", "当前选中预住院单", "男"),
                new PrintFieldMeta("preadmit.age", "年龄", "当前选中预住院单", "48"),
                new PrintFieldMeta("preadmit.medicalRecordNo", "病历号", "当前选中预住院单", "MR20260001"),
                new PrintFieldMeta("preadmit.targetDeptName", "拟入院科室", "当前选中预住院单", "普外科"),
                new PrintFieldMeta("preadmit.primaryDiagName", "拟入院主诊断(A诊断)", "当前选中预住院单", "胆囊结石伴慢性胆囊炎"),
                new PrintFieldMeta("preadmit.plannedSurgeryName", "拟行手术", "当前选中预住院单", "腹腔镜胆囊切除术"),
                new PrintFieldMeta("preadmit.validUntilText", "医保有效期截止", "当前选中预住院单", "2026-11-28")
            }.Concat(CommonSystemFields).ToList()),

            new("checkup", "健康体检管理工作站", StandardSlots, new[]
            {
                new PrintFieldMeta("checkup.checkupNo", "体检单号", "当前选中体检单", "TJ202611200001"),
                new PrintFieldMeta("checkup.guideBarcode", "导检条码号", "当前选中体检单", "PE261120100888"),
                new PrintFieldMeta("checkup.fullName", "体检人姓名", "当前选中体检单", "张三"),
                new PrintFieldMeta("checkup.gender", "性别", "当前选中体检单", "男"),
                new PrintFieldMeta("checkup.age", "年龄", "当前选中体检单", "32"),
                new PrintFieldMeta("checkup.companyName", "团检单位", "当前选中体检单", "个人体检"),
                new PrintFieldMeta("checkup.packageName", "体检套餐名称", "当前选中体检单", "入职常规健康体检套餐A"),
                new PrintFieldMeta("checkup.totalAmount", "套餐费用", "当前选中体检单", "188.00"),
                new PrintFieldMeta("checkup.overallConclusion", "主检总检结论", "当前选中体检单", "本次体检未见明显异常")
            }.Concat(CommonSystemFields).ToList()),

            new("finance", "收费结算工作站", StandardSlots, new[]
            {
                new PrintFieldMeta("finance.paymentOrderNo", "收费单号", "选中收费记录", "PAY20261120005"),
                new PrintFieldMeta("finance.prescriptionNo", "关联处方/申请单号", "选中收费记录", "RX20261120005"),
                new PrintFieldMeta("finance.patientName", "患者姓名", "选中收费记录", "张三"),
                new PrintFieldMeta("finance.drugSummary", "收费明细摘要", "选中收费记录", "门诊西药费/检验费"),
                new PrintFieldMeta("finance.totalAmount", "实收金额", "选中收费记录", "45.00")
            }.Concat(CommonSystemFields).ToList())
        };

        return ApiResponse<IReadOnlyList<PrintPageDataSourceMeta>>.Success(list);
    }

    [HttpGet("templates")]
    public async Task<ApiResponse<IReadOnlyList<PrtTemplate>>> GetTemplates([FromQuery] string? pageKey = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = "SELECT * FROM prt_templates WHERE is_active = 1";
        if (!string.IsNullOrWhiteSpace(pageKey))
            sql += " AND page_key = @PageKey";
        sql += " ORDER BY updated_at_ms DESC";
        var rows = (await conn.QueryAsync<PrtTemplate>(sql, new { PageKey = pageKey?.Trim() })).ToList();
        return ApiResponse<IReadOnlyList<PrtTemplate>>.Success(rows);
    }

    [HttpGet("templates/{id}")]
    public async Task<ApiResponse<PrtTemplate>> GetTemplateById(string id, CancellationToken ct)
    {
        var tpl = await _tplRepo.GetByIdAsync(id, ct);
        if (tpl == null) return ApiResponse<PrtTemplate>.Fail(404, "打印模板不存在");
        return ApiResponse<PrtTemplate>.Success(tpl);
    }

    [HttpPost("templates")]
    public async Task<ApiResponse<PrtTemplate>> SaveTemplate([FromBody] SaveTemplateRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.TemplateName))
            return ApiResponse<PrtTemplate>.Fail(400, "模板名称不能为空");
        if (req.PaperWidthMm < 15 || req.PaperWidthMm > 500 || req.PaperHeightMm < 15 || req.PaperHeightMm > 1000)
            return ApiResponse<PrtTemplate>.Fail(400, "纸张宽高超出合理范围（宽 15~500mm，高 15~1000mm）");

        long now = SystemTime.UtcNowMs;
        PrtTemplate? existing = string.IsNullOrWhiteSpace(req.Id) ? null : await _tplRepo.GetByIdAsync(req.Id, ct);

        if (existing == null)
        {
            string code = string.IsNullOrWhiteSpace(req.TemplateCode)
                ? "TPL-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss")
                : req.TemplateCode.Trim().ToUpperInvariant();

            var entity = new PrtTemplate
            {
                Id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim(),
                TemplateCode = code,
                TemplateName = req.TemplateName.Trim(),
                Category = string.IsNullOrWhiteSpace(req.Category) ? "BARCODE" : req.Category.Trim().ToUpperInvariant(),
                PageKey = string.IsNullOrWhiteSpace(req.PageKey) ? "medtech" : req.PageKey.Trim(),
                PaperWidthMm = req.PaperWidthMm,
                PaperHeightMm = req.PaperHeightMm,
                Orientation = string.IsNullOrWhiteSpace(req.Orientation) ? "PORTRAIT" : req.Orientation.Trim().ToUpperInvariant(),
                MarginTopMm = req.MarginTopMm,
                MarginRightMm = req.MarginRightMm,
                MarginBottomMm = req.MarginBottomMm,
                MarginLeftMm = req.MarginLeftMm,
                LayoutJson = string.IsNullOrWhiteSpace(req.LayoutJson) ? "[]" : req.LayoutJson,
                IsPublished = (short)(req.IsPublished ? 1 : 0),
                IsActive = (short)(req.IsActive ? 1 : 0),
                Remark = req.Remark,
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            };
            await _tplRepo.InsertAsync(entity, ct);
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("PRINT_TEMPLATE_CREATE", "prt_templates", entity.Id, $"新建打印模板：{entity.TemplateName}（{entity.PaperWidthMm}×{entity.PaperHeightMm}mm，绑定页面 {entity.PageKey}）", ActorId, ActorName, null, ct);
            return ApiResponse<PrtTemplate>.Success(entity, "打印模板已保存");
        }
        else
        {
            existing.TemplateName = req.TemplateName.Trim();
            if (!string.IsNullOrWhiteSpace(req.TemplateCode))
                existing.TemplateCode = req.TemplateCode.Trim().ToUpperInvariant();
            existing.Category = string.IsNullOrWhiteSpace(req.Category) ? existing.Category : req.Category.Trim().ToUpperInvariant();
            existing.PageKey = string.IsNullOrWhiteSpace(req.PageKey) ? existing.PageKey : req.PageKey.Trim();
            existing.PaperWidthMm = req.PaperWidthMm;
            existing.PaperHeightMm = req.PaperHeightMm;
            existing.Orientation = string.IsNullOrWhiteSpace(req.Orientation) ? existing.Orientation : req.Orientation.Trim().ToUpperInvariant();
            existing.MarginTopMm = req.MarginTopMm;
            existing.MarginRightMm = req.MarginRightMm;
            existing.MarginBottomMm = req.MarginBottomMm;
            existing.MarginLeftMm = req.MarginLeftMm;
            existing.LayoutJson = string.IsNullOrWhiteSpace(req.LayoutJson) ? "[]" : req.LayoutJson;
            existing.IsPublished = (short)(req.IsPublished ? 1 : 0);
            existing.IsActive = (short)(req.IsActive ? 1 : 0);
            existing.Remark = req.Remark;
            existing.UpdatedAtMs = now;
            existing.UpdatedBy = ActorId;
            await _tplRepo.UpdateAsync(existing, ct);
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("PRINT_TEMPLATE_UPDATE", "prt_templates", existing.Id, $"更新打印模板：{existing.TemplateName}（{existing.PaperWidthMm}×{existing.PaperHeightMm}mm）", ActorId, ActorName, null, ct);
            return ApiResponse<PrtTemplate>.Success(existing, "打印模板已更新");
        }
    }

    [HttpDelete("templates/{id}")]
    public async Task<ApiResponse<object>> DeleteTemplate(string id, CancellationToken ct)
    {
        var existing = await _tplRepo.GetByIdAsync(id, ct);
        if (existing == null) return ApiResponse<object>.Fail(404, "模板不存在");
        existing.IsActive = 0;
        existing.UpdatedAtMs = SystemTime.UtcNowMs;
        existing.UpdatedBy = ActorId;
        await _tplRepo.UpdateAsync(existing, ct);
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync("UPDATE prt_trigger_bindings SET is_active = 0 WHERE template_id = @Id", new { Id = id });
        return ApiResponse<object>.Success(new { id }, "模板及关联触发点已停用");
    }

    /// <summary>
    /// 查询打印触发位配置（供业务页面动态渲染打印按钮，或供设计器管理全部触发位）
    /// </summary>
    [HttpGet("triggers")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetTriggers(
        [FromQuery] string? pageKey = null,
        [FromQuery] string? slot = null,
        [FromQuery] string? templateId = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"
            SELECT b.id AS Id, b.template_id AS TemplateId, b.page_key AS PageKey,
                   b.trigger_slot AS TriggerSlot, b.button_label AS ButtonLabel,
                   b.button_icon AS ButtonIcon, b.condition_expr AS ConditionExpr,
                   b.sort_order AS SortOrder, b.is_active AS IsActive,
                   t.template_code AS TemplateCode, t.template_name AS TemplateName,
                   t.category AS Category, t.paper_width_mm AS PaperWidthMm,
                   t.paper_height_mm AS PaperHeightMm, t.orientation AS Orientation,
                   t.margin_top_mm AS MarginTopMm, t.margin_right_mm AS MarginRightMm,
                   t.margin_bottom_mm AS MarginBottomMm, t.margin_left_mm AS MarginLeftMm,
                   t.layout_json AS LayoutJson
            FROM prt_trigger_bindings b
            INNER JOIN prt_templates t ON t.id = b.template_id
            WHERE b.is_active = 1 AND t.is_active = 1 AND t.is_published = 1";
        if (!string.IsNullOrWhiteSpace(pageKey))
            sql += " AND b.page_key = @PageKey";
        if (!string.IsNullOrWhiteSpace(slot))
            sql += " AND b.trigger_slot = @Slot";
        if (!string.IsNullOrWhiteSpace(templateId))
            sql += " AND b.template_id = @TemplateId";
        sql += " ORDER BY b.sort_order ASC, b.created_at_ms ASC";

        var rows = (await conn.QueryAsync<dynamic>(sql, new { PageKey = pageKey?.Trim(), Slot = slot?.Trim(), TemplateId = templateId?.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("triggers")]
    public async Task<ApiResponse<PrtTriggerBinding>> SaveTrigger([FromBody] SaveTriggerRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.TemplateId) || string.IsNullOrWhiteSpace(req.PageKey) || string.IsNullOrWhiteSpace(req.ButtonLabel))
            return ApiResponse<PrtTriggerBinding>.Fail(400, "模板、目标页面与按钮名称不能为空");

        long now = SystemTime.UtcNowMs;
        PrtTriggerBinding? existing = string.IsNullOrWhiteSpace(req.Id) ? null : await _triggerRepo.GetByIdAsync(req.Id, ct);
        if (existing == null)
        {
            var binding = new PrtTriggerBinding
            {
                Id = Guid.NewGuid().ToString("D"),
                TemplateId = req.TemplateId.Trim(),
                PageKey = req.PageKey.Trim(),
                TriggerSlot = string.IsNullOrWhiteSpace(req.TriggerSlot) ? "TOP_TOOLBAR" : req.TriggerSlot.Trim().ToUpperInvariant(),
                ButtonLabel = req.ButtonLabel.Trim(),
                ButtonIcon = string.IsNullOrWhiteSpace(req.ButtonIcon) ? "🖨️" : req.ButtonIcon.Trim(),
                ConditionExpr = req.ConditionExpr,
                SortOrder = req.SortOrder,
                IsActive = (short)(req.IsActive ? 1 : 0),
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            };
            await _triggerRepo.InsertAsync(binding, ct);
            return ApiResponse<PrtTriggerBinding>.Success(binding, "打印触发位置已配置");
        }
        else
        {
            existing.TemplateId = req.TemplateId.Trim();
            existing.PageKey = req.PageKey.Trim();
            existing.TriggerSlot = string.IsNullOrWhiteSpace(req.TriggerSlot) ? existing.TriggerSlot : req.TriggerSlot.Trim().ToUpperInvariant();
            existing.ButtonLabel = req.ButtonLabel.Trim();
            existing.ButtonIcon = req.ButtonIcon;
            existing.ConditionExpr = req.ConditionExpr;
            existing.SortOrder = req.SortOrder;
            existing.IsActive = (short)(req.IsActive ? 1 : 0);
            existing.UpdatedAtMs = now;
            existing.UpdatedBy = ActorId;
            await _triggerRepo.UpdateAsync(existing, ct);
            return ApiResponse<PrtTriggerBinding>.Success(existing, "打印触发位置已更新");
        }
    }

    [HttpDelete("triggers/{id}")]
    public async Task<ApiResponse<object>> DeleteTrigger(string id, CancellationToken ct)
    {
        var existing = await _triggerRepo.GetByIdAsync(id, ct);
        if (existing == null) return ApiResponse<object>.Fail(404, "触发位配置不存在");
        existing.IsActive = 0;
        existing.UpdatedAtMs = SystemTime.UtcNowMs;
        existing.UpdatedBy = ActorId;
        await _triggerRepo.UpdateAsync(existing, ct);
        return ApiResponse<object>.Success(new { id }, "已移除该打印触发位");
    }

    [HttpPost("jobs/record")]
    public async Task<ApiResponse<PrtPrintLog>> RecordPrintJob([FromBody] RecordPrintJobRequest req, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        var log = new PrtPrintLog
        {
            Id = Guid.NewGuid().ToString("D"),
            TemplateId = req.TemplateId ?? string.Empty,
            TemplateCode = req.TemplateCode ?? string.Empty,
            TemplateName = req.TemplateName ?? string.Empty,
            PageKey = req.PageKey ?? string.Empty,
            TriggerSlot = req.TriggerSlot ?? "TOP_TOOLBAR",
            BusinessKey = req.BusinessKey,
            PatientName = req.PatientName,
            RenderedContextJson = req.RenderedContextJson,
            Copies = Math.Max(1, req.Copies),
            PrinterName = req.PrinterName ?? "系统默认打印机",
            PrintedBy = ActorId,
            PrintedByName = ActorName,
            PrintedAtMs = now,
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        await _logRepo.InsertAsync(log, ct);
        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("PRINT_EXECUTE", "prt_print_logs", log.Id, $"打印单据/条码：{log.TemplateName}（患者：{log.PatientName ?? "-"}，单号：{log.BusinessKey ?? "-"}，份数：{log.Copies}）", ActorId, ActorName, null, ct);
        return ApiResponse<PrtPrintLog>.Success(log, "打印日志已留痕");
    }

    [HttpGet("jobs/logs")]
    public async Task<ApiResponse<IReadOnlyList<PrtPrintLog>>> GetPrintLogs([FromQuery] int limit = 100)
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<PrtPrintLog>(
            "SELECT * FROM prt_print_logs ORDER BY printed_at_ms DESC LIMIT @Limit",
            new { Limit = Math.Clamp(limit, 1, 500) })).ToList();
        return ApiResponse<IReadOnlyList<PrtPrintLog>>.Success(rows);
    }
}
