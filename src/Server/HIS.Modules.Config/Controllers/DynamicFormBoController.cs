using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Config.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Config.Controllers;

/// <summary>
/// 动态表单与真实业务对象 (BO) 关联引擎：
/// 提供全院 BO 元数据注册表、按 BO 查询已发布表单、读取真实 BO 记录列表、
/// 以及按 (entityType, entityId) 校验持久化扩展字段并同步回写 BO 主表 ext_data_json。
/// </summary>
[ApiController]
[Route("api/v4/config/bo")]
public sealed class DynamicFormBoController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<CfgEntityFieldValue> _valRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public DynamicFormBoController(
        IDbConnectionFactory connFactory,
        IRepository<CfgEntityFieldValue> valRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _valRepo = valRepo;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record BoTypeMeta(
        string EntityType,
        string DisplayName,
        string TableName,
        string ModuleName,
        int BoundSchemaCount = 0);

    public sealed record BoRecordSummary(
        string Id,
        string BusinessNo,
        string Title,
        string SubTitle);

    public sealed record SaveBoFieldValueItem(
        string FieldCode,
        string? ValString,
        long? ValInt,
        decimal? ValDecimal,
        long? ValTimeMs);

    public sealed record SaveBoValuesRequest(
        string? FormCode,
        List<SaveBoFieldValueItem> Values);

    private static readonly Dictionary<string, (string DisplayName, string TableName, string ModuleName, string ListSql)> BoRegistry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OPD_VISIT"] = ("门诊就诊单", "opd_visits", "门诊医生站",
                "SELECT v.id AS Id, v.visit_no AS BusinessNo, COALESCE(p.full_name, v.patient_id) AS Title, COALESCE(v.primary_diag_name, '门诊接诊') AS SubTitle FROM opd_visits v LEFT JOIN pat_patients p ON p.id = v.patient_id ORDER BY v.created_at_ms DESC LIMIT 50"),
            ["OPD_PRESCRIPTION"] = ("门诊处方单", "opd_prescriptions", "门诊/药房",
                "SELECT id AS Id, prescription_no AS BusinessNo, rx_type AS Title, '金额 ¥' || CAST(total_amount AS TEXT) AS SubTitle FROM opd_prescriptions ORDER BY created_at_ms DESC LIMIT 50"),
            ["IPD_ENCOUNTER"] = ("住院在院记录", "ipd_encounters", "住院医护站",
                "SELECT e.id AS Id, e.admission_no AS BusinessNo, COALESCE(p.full_name, e.patient_id) AS Title, COALESCE(e.bed_code, '待分床') AS SubTitle FROM ipd_encounters e LEFT JOIN pat_patients p ON p.id = e.patient_id ORDER BY e.created_at_ms DESC LIMIT 50"),
            ["IPD_PRE_ADMISSION"] = ("医保预住院主单", "ipd_pre_admissions", "预住院管理中心",
                "SELECT id AS Id, pre_admit_no AS BusinessNo, patient_name AS Title, primary_diag_name || ' · ' || status AS SubTitle FROM ipd_pre_admissions ORDER BY registered_at_ms DESC LIMIT 50"),
            ["PAT_PATIENT"] = ("患者主索引档案", "pat_patients", "患者主索引",
                "SELECT id AS Id, medical_record_no AS BusinessNo, full_name AS Title, gender_code AS SubTitle FROM pat_patients ORDER BY created_at_ms DESC LIMIT 50"),
            ["MTC_SERVICE_REQUEST"] = ("医技检验检查申请单", "mtc_service_requests", "医技工作站",
                "SELECT id AS Id, request_no AS BusinessNo, service_item_code AS Title, med_tech_sub_type AS SubTitle FROM mtc_service_requests ORDER BY created_at_ms DESC LIMIT 50"),
            ["MTC_DIAGNOSTIC_REPORT"] = ("医技诊断报告单", "mtc_diagnostic_reports", "医技报告与审签",
                "SELECT id AS Id, report_no AS BusinessNo, patient_name || ' - ' || exam_item_name AS Title, medtech_dept_name || ' · ' || status AS SubTitle FROM mtc_diagnostic_reports ORDER BY updated_at_ms DESC LIMIT 50"),
            ["CHK_ORDER"] = ("健康体检登记单", "chk_orders", "健康体检中心",
                "SELECT id AS Id, checkup_no AS BusinessNo, full_name AS Title, package_name || ' · ' || status AS SubTitle FROM chk_orders ORDER BY registered_at_ms DESC LIMIT 50"),
            ["HR_EMPLOYEE"] = ("医院职工人事档案", "hr_employees", "人力资源管理",
                "SELECT id AS Id, employee_no AS BusinessNo, full_name AS Title, professional_title || ' · ' || employment_status AS SubTitle FROM hr_employees ORDER BY created_at_ms DESC LIMIT 50")
        };

    /// <summary>
    /// 获取全院所有可绑定动态表单的真实 BO 类型清单及其已绑定表单数
    /// </summary>
    [HttpGet("types")]
    public async Task<ApiResponse<IReadOnlyList<BoTypeMeta>>> GetBoTypes()
    {
        using var conn = _connFactory.CreateConnection();
        var counts = (await conn.QueryAsync<(string EntityType, int Cnt)>(
            "SELECT UPPER(entity_type) AS EntityType, COUNT(*) AS Cnt FROM cfg_form_schemas GROUP BY UPPER(entity_type)"))
            .ToDictionary(x => x.EntityType, x => x.Cnt, StringComparer.OrdinalIgnoreCase);

        var result = BoRegistry.Select(kv => new BoTypeMeta(
            kv.Key,
            kv.Value.DisplayName,
            kv.Value.TableName,
            kv.Value.ModuleName,
            counts.TryGetValue(kv.Key, out int c) ? c : 0)).ToList();

        return ApiResponse<IReadOnlyList<BoTypeMeta>>.Success(result);
    }

    /// <summary>
    /// 获取某个真实 BO 类型下的近期业务单据列表（供管理员测试关联或业务端选择）
    /// </summary>
    [HttpGet("records/{entityType}")]
    public async Task<ApiResponse<IReadOnlyList<BoRecordSummary>>> GetBoRecords(string entityType)
    {
        if (!BoRegistry.TryGetValue(entityType.Trim(), out var meta))
            return ApiResponse<IReadOnlyList<BoRecordSummary>>.Fail(400, $"未注册的业务对象类型：{entityType}");

        using var conn = _connFactory.CreateConnection();
        try
        {
            var rows = (await conn.QueryAsync<BoRecordSummary>(meta.ListSql)).ToList();
            return ApiResponse<IReadOnlyList<BoRecordSummary>>.Success(rows);
        }
        catch (Exception ex)
        {
            return ApiResponse<IReadOnlyList<BoRecordSummary>>.Fail(500, $"查询 BO [{meta.TableName}] 记录失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 按真实 BO 类型获取所有已发布的动态表单模板与字段定义
    /// </summary>
    [HttpGet("schemas/{entityType}")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetSchemasByBoType(string entityType)
    {
        using var conn = _connFactory.CreateConnection();
        var schemas = (await conn.QueryAsync<CfgFormSchema>(
            "SELECT * FROM cfg_form_schemas WHERE UPPER(entity_type) = UPPER(@Et) AND is_published = 1 ORDER BY updated_at_ms DESC",
            new { Et = entityType.Trim() })).ToList();

        var list = new List<object>();
        foreach (var s in schemas)
        {
            var fields = (await conn.QueryAsync<CfgFieldDefinition>(
                "SELECT * FROM cfg_field_definitions WHERE form_schema_id = @Sid AND is_deleted = 0 ORDER BY sort_order ASC",
                new { Sid = s.Id })).ToList();
            list.Add(new { Schema = s, Fields = fields });
        }
        return ApiResponse<IReadOnlyList<object>>.Success(list);
    }

    /// <summary>
    /// 加载指定真实 BO 实例 (entityType + entityId) 的已发布动态表单定义与已填报扩展字段值
    /// </summary>
    [HttpGet("instance/{entityType}/{entityId}")]
    public async Task<ApiResponse<object>> GetBoFormInstance(string entityType, string entityId)
    {
        string et = entityType.Trim().ToUpperInvariant();
        if (!BoRegistry.TryGetValue(et, out var meta))
            return ApiResponse<object>.Fail(400, $"未注册的业务对象类型：{entityType}");

        using var conn = _connFactory.CreateConnection();
        int exists = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {meta.TableName} WHERE id = @Id", new { Id = entityId.Trim() });
        if (exists == 0)
            return ApiResponse<object>.Fail(404, $"在真实 BO 表 [{meta.TableName}] 中未找到记录 {entityId}");

        var schemas = (await conn.QueryAsync<CfgFormSchema>(
            "SELECT * FROM cfg_form_schemas WHERE UPPER(entity_type) = @Et AND is_published = 1 ORDER BY updated_at_ms DESC",
            new { Et = et })).ToList();

        var values = (await conn.QueryAsync<CfgEntityFieldValue>(
            "SELECT * FROM cfg_entity_field_values WHERE UPPER(entity_type) = @Et AND entity_id = @Eid",
            new { Et = et, Eid = entityId.Trim() })).ToList();

        var valMap = values.ToDictionary(v => v.FieldCode, v => v, StringComparer.OrdinalIgnoreCase);
        var forms = new List<object>();
        foreach (var s in schemas)
        {
            var fields = (await conn.QueryAsync<CfgFieldDefinition>(
                "SELECT * FROM cfg_field_definitions WHERE form_schema_id = @Sid AND is_deleted = 0 ORDER BY sort_order ASC",
                new { Sid = s.Id })).ToList();

            var dictTypes = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.DictCode))
                .Select(f => f.DictCode!.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            var dictMap = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
            foreach (string dt in dictTypes)
            {
                var dRows = (await conn.QueryAsync<dynamic>(
                    "SELECT item_code AS ItemCode, item_name AS ItemName, is_default AS IsDefault FROM cfg_clinical_dictionaries WHERE UPPER(dict_type) = @Dt AND is_active = 1 ORDER BY sort_order ASC, item_code ASC",
                    new { Dt = dt })).ToList<object>();
                dictMap[dt] = dRows;
            }

            var fieldItems = fields.Select(f =>
            {
                valMap.TryGetValue(f.FieldCode, out var v);
                string displayVal = v?.ValString
                    ?? (v?.ValDecimal.HasValue == true ? v.ValDecimal.Value.ToString("0.####") : null)
                    ?? (v?.ValInt.HasValue == true ? v.ValInt.Value.ToString() : null)
                    ?? string.Empty;
                var dictOptions = !string.IsNullOrWhiteSpace(f.DictCode) && dictMap.TryGetValue(f.DictCode.Trim().ToUpperInvariant(), out var opts)
                    ? opts
                    : new List<object>();
                return new
                {
                    f.Id,
                    f.FieldCode,
                    f.FieldLabel,
                    f.DataType,
                    f.UrsaControlType,
                    f.UnitText,
                    f.DictCode,
                    f.SortOrder,
                    Value = displayVal,
                    DictOptions = dictOptions
                };
            }).ToList();

            forms.Add(new
            {
                SchemaId = s.Id,
                s.FormCode,
                s.FormName,
                s.EntityType,
                s.GridColumns,
                Fields = fieldItems
            });
        }

        return ApiResponse<object>.Success(new
        {
            EntityType = et,
            BoDisplayName = meta.DisplayName,
            EntityId = entityId.Trim(),
            Forms = forms
        });
    }

    /// <summary>
    /// 将动态表单填报值校验并持久化到真实 BO（写 cfg_entity_field_values + 同步回写 BO 主表 ext_data_json）
    /// </summary>
    [HttpPost("instance/{entityType}/{entityId}/values")]
    public async Task<ApiResponse<object>> SaveBoFormValues(
        string entityType,
        string entityId,
        [FromBody] SaveBoValuesRequest req,
        CancellationToken ct)
    {
        string et = entityType.Trim().ToUpperInvariant();
        if (!BoRegistry.TryGetValue(et, out var meta))
            return ApiResponse<object>.Fail(400, $"未注册的业务对象类型：{entityType}");

        using var checkConn = _connFactory.CreateConnection();
        int exists = await checkConn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {meta.TableName} WHERE id = @Id", new { Id = entityId.Trim() });
        if (exists == 0)
            return ApiResponse<object>.Fail(404, $"在真实 BO 表 [{meta.TableName}] 中未找到记录 {entityId}，拒绝写入孤立扩展字段");

        if (req.Values == null || req.Values.Count == 0)
            return ApiResponse<object>.Fail(400, "未提交任何扩展字段值");

        // 服务端强制主数据字典约束校验：防止绕过前端手工输入非法值
        var boundFields = (await checkConn.QueryAsync<CfgFieldDefinition>(@"
            SELECT f.* FROM cfg_field_definitions f
            INNER JOIN cfg_form_schemas s ON s.id = f.form_schema_id
            WHERE UPPER(s.entity_type) = @Et AND s.is_published = 1 AND f.is_deleted = 0 AND f.dict_code IS NOT NULL AND f.dict_code <> ''",
            new { Et = et })).ToList();
        var dictFieldMap = boundFields
            .GroupBy(f => f.FieldCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in req.Values)
        {
            if (string.IsNullOrWhiteSpace(item.FieldCode) || string.IsNullOrWhiteSpace(item.ValString)) continue;
            if (dictFieldMap.TryGetValue(item.FieldCode.Trim(), out var def) && !string.IsNullOrWhiteSpace(def.DictCode))
            {
                string submitted = item.ValString.Trim();
                int matchCnt = await checkConn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1) FROM cfg_clinical_dictionaries
                    WHERE UPPER(dict_type) = UPPER(@Dt) AND is_active = 1
                      AND (UPPER(item_code) = UPPER(@Val) OR item_name = @Val)",
                    new { Dt = def.DictCode.Trim(), Val = submitted });
                if (matchCnt == 0)
                {
                    return ApiResponse<object>.Fail(400, $"字段 [{def.FieldLabel}] 引用了主数据字典 [{def.DictCode}]，输入值「{submitted}」不在有效字典范围内，已拒绝保存");
                }
            }
        }

        long now = SystemTime.UtcNowMs;
        int savedCount = 0;

        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            foreach (var item in req.Values)
            {
                if (string.IsNullOrWhiteSpace(item.FieldCode)) continue;
                string fCode = item.FieldCode.Trim();
                var existing = (await _valRepo.QueryAsync(
                    "SELECT * FROM cfg_entity_field_values WHERE UPPER(entity_type) = @Et AND entity_id = @Eid AND field_code = @Fc",
                    new { Et = et, Eid = entityId.Trim(), Fc = fCode }, token)).FirstOrDefault();

                if (existing != null)
                {
                    existing.ValString = item.ValString;
                    existing.ValInt = item.ValInt;
                    existing.ValDecimal = item.ValDecimal;
                    existing.ValTimeMs = item.ValTimeMs;
                    existing.UpdatedAtMs = now;
                    existing.UpdatedBy = ActorId;
                    await _valRepo.UpdateAsync(existing, token);
                }
                else
                {
                    await _valRepo.InsertAsync(new CfgEntityFieldValue
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        EntityType = et,
                        EntityId = entityId.Trim(),
                        FieldCode = fCode,
                        ValString = item.ValString,
                        ValInt = item.ValInt,
                        ValDecimal = item.ValDecimal,
                        ValTimeMs = item.ValTimeMs,
                        SchemaVersion = 1,
                        CreatedAtMs = now,
                        CreatedBy = ActorId,
                        UpdatedAtMs = now,
                        UpdatedBy = ActorId,
                        VersionNo = 1
                    }, token);
                }
                savedCount++;
            }

            // 同步将全量扩展字段合并回写到真实 BO 主表的 ext_data_json，供审批流/打印/查询直接消费
            using var conn = _connFactory.CreateConnection();
            var allVals = (await conn.QueryAsync<CfgEntityFieldValue>(
                "SELECT * FROM cfg_entity_field_values WHERE UPPER(entity_type) = @Et AND entity_id = @Eid",
                new { Et = et, Eid = entityId.Trim() })).ToList();

            var extDict = new Dictionary<string, object?>();
            foreach (var v in allVals)
            {
                object? val = v.ValDecimal.HasValue ? v.ValDecimal.Value
                    : v.ValInt.HasValue ? v.ValInt.Value
                    : (object?)v.ValString;
                extDict[v.FieldCode] = val;
            }
            string extJson = JsonSerializer.Serialize(new { dynamicFields = extDict, lastUpdatedAtMs = now });
            if (extJson.Length > 3950) extJson = extJson[..3950];

            await conn.ExecuteAsync(
                $"UPDATE {meta.TableName} SET ext_data_json = @Ext, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
                new { Ext = extJson, Now = now, Actor = ActorId, Id = entityId.Trim() });
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("DYNAMIC_FORM_BO_SAVE", meta.TableName, entityId.Trim(), $"保存 BO [{meta.DisplayName}] 动态扩展字段 {savedCount} 项并同步主表 ext_data_json", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { EntityType = et, EntityId = entityId.Trim(), SavedCount = savedCount }, $"已保存 {savedCount} 项扩展字段并同步至真实 BO [{meta.DisplayName}]");
    }
}
