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

namespace HIS.Modules.MedTech.Controllers;

/// <summary>
/// 手术麻醉全流程信息管理中心 (AIMS)：
/// 1) 手术室排台看板、急诊插台自动顺延重排、首台准点率质控与家属大屏脱敏视图
/// 2) 术前麻醉访视（ASA / Mallampati / STOP-BANG / 潮气量与局麻极量自动计算 + 回写 EMR）
/// 3) 术中液体出入量实时平衡核算与危机应急事件（含恶性高热 MH 丹曲林按体重首剂计算）
/// 4) 毒麻精药品（五专）领用、空安瓿回收核销与残液双人见证销毁闭环（联动药房库存）
/// 5) PACU Modified Aldrete 离室门禁（<9 分拦截）与 SBAR 病区交接签收
/// 6) 术后 PCA 镇痛泵（PCIA/PCEA）与 24h/48h/72h VAS 疼痛随访
/// </summary>
[ApiController]
[Route("api/v4/aims")]
public sealed class AimsController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public AimsController(
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

    public sealed record EmergencyInsertOrDto(string EncounterId, string OrRoomCode, string SurgeryCode, string SurgeryName, int SurgeryLevel, string SurgeonName, string AnesthesiologistName);
    public sealed record CreatePreopDto(string ScheduleId, string? EncounterId, string PatientName, decimal WeightKg, string AsaClass, int MallampatiClass, int StopBangScore, int FastingHours, string PlannedAnesthesiaMethod);
    public sealed record AddIntraopEventDto(string ScheduleId, string RecordType, string CategoryName, decimal VolumeMl, string? EventDetail, decimal? PatientWeightKg = null);
    public sealed record IssueNarcoticDto(string ScheduleId, string PatientName, string DrugName, string? BatchNo, int IssuedAmpoules);
    public sealed record ReconcileNarcoticDto(int UsedAmpoules, int ReturnedIntactAmpoules, int EmptyAmpoulesRecovered, decimal WasteVolumeMl, string WitnessName);
    public sealed record SavePacuDto(string ScheduleId, string PatientName, int ActivityScore, int RespirationScore, int CirculationScore, int ConsciousnessScore, int Spo2Score, string? SbarHandoffSummary);
    public sealed record PacuHandoffDto(string ReceivingWardNurse, string SbarHandoffSummary);
    public sealed record CreatePcaDto(string ScheduleId, string PatientName, string PcaMode, string FormulaSummary, decimal BackgroundRateMlH, decimal BolusDoseMl, int LockoutMinutes);
    public sealed record UpdatePcaFollowupDto(int? Vas24h, int? Vas48h, int? Vas72h, string? AdverseEvents, string Status);

    // ===================== 1. 手术室排台看板 / 急诊插台重排 / 质控指标 =====================

    [HttpGet("or-board")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetOrBoard()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT s.id AS Id, s.encounter_id AS EncounterId, s.or_room_code AS OrRoomCode,
                   s.table_seq AS TableSeq, s.surgery_code AS SurgeryCode, s.surgery_name AS SurgeryName,
                   s.surgery_level AS SurgeryLevel, s.main_surgeon_id AS MainSurgeon,
                   s.anesthesiologist_id AS Anesthesiologist, s.status AS Status,
                   s.sign_in_ok AS SignInOk, s.time_out_ok AS TimeOutOk, s.sign_out_ok AS SignOutOk,
                   COALESCE(p.full_name, e.patient_id, s.encounter_id) AS PatientName
            FROM mtc_surgery_schedules s
            LEFT JOIN ipd_encounters e ON e.id = s.encounter_id
            LEFT JOIN pat_patients p ON p.id = e.patient_id
            ORDER BY s.or_room_code ASC, s.table_seq ASC LIMIT 120")).ToList();

        var result = rows.Select(r =>
        {
            string fullName = (string?)r.PatientName ?? "患者";
            string masked = fullName.Length <= 1 ? fullName : fullName[0] + new string('*', fullName.Length - 1);
            int st = Convert.ToInt32(r.Status);
            string familyStage = st switch
            {
                0 => "术前准备中",
                1 => "手术进行中",
                2 => "麻醉复苏室(PACU)观察中",
                _ => "已返回病房"
            };
            return (object)new
            {
                Id = (string)r.Id,
                EncounterId = (string)r.EncounterId,
                OrRoomCode = (string)r.OrRoomCode,
                TableSeq = Convert.ToInt32(r.TableSeq),
                SurgeryCode = (string)r.SurgeryCode,
                SurgeryName = (string)r.SurgeryName,
                SurgeryLevel = Convert.ToInt32(r.SurgeryLevel),
                MainSurgeon = (string)r.MainSurgeon,
                Anesthesiologist = (string)r.Anesthesiologist,
                Status = st,
                PatientName = fullName,
                FamilyScreenName = masked,
                FamilyStage = familyStage
            };
        }).ToList();

        return ApiResponse<IReadOnlyList<object>>.Success(result);
    }

    /// <summary>
    /// 急诊插台重排：将急诊手术插入指定手术间第 1 台（或当前未开始首台），并自动将后续择期手术台次顺延 +1
    /// </summary>
    [HttpPost("or-board/emergency-insert")]
    public async Task<ApiResponse<object>> EmergencyInsertOr([FromBody] EmergencyInsertOrDto req)
    {
        if (string.IsNullOrWhiteSpace(req.OrRoomCode) || string.IsNullOrWhiteSpace(req.SurgeryName))
            return ApiResponse<object>.Fail(400, "手术间号与急诊手术名称不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string room = req.OrRoomCode.Trim();

        using var conn = _connFactory.CreateConnection();
        using var tx = conn.BeginTransaction();
        // 将该手术间所有尚未开始的择期手术台次顺延 +1
        int shifted = await conn.ExecuteAsync(
            "UPDATE mtc_surgery_schedules SET table_seq = table_seq + 1, updated_at_ms = @Now, updated_by = @Actor WHERE or_room_code = @Room AND status = 0",
            new { Now = now, Actor = ActorId, Room = room }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO mtc_surgery_schedules
            (id, encounter_id, or_dept_id, or_room_code, table_seq, surgery_code, surgery_name, surgery_level,
             main_surgeon_id, anesthesiologist_id, circulating_nurse_id, sign_in_ok, time_out_ok, sign_out_ok,
             instrument_count_balanced, status, ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Enc, 'OR_CENTER', @Room, 1, @Code, @Name, @Lvl,
             @Surg, @Anes, 'NURSE_OR', 0, 0, 0,
             1, 0, '{""isEmergency"":true}', 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id,
                Enc = string.IsNullOrWhiteSpace(req.EncounterId) ? "EMG-" + SystemTime.ShanghaiNow.ToString("HHmmss") : req.EncounterId.Trim(),
                Room = room,
                Code = string.IsNullOrWhiteSpace(req.SurgeryCode) ? "EMG_SURG" : req.SurgeryCode.Trim(),
                Name = "[急诊加台] " + req.SurgeryName.Trim(),
                Lvl = Math.Clamp(req.SurgeryLevel, 1, 4),
                Surg = string.IsNullOrWhiteSpace(req.SurgeonName) ? ActorName : req.SurgeonName.Trim(),
                Anes = string.IsNullOrWhiteSpace(req.AnesthesiologistName) ? ActorName : req.AnesthesiologistName.Trim(),
                Now = now,
                Actor = ActorId
            }, tx);
        tx.Commit();

        return ApiResponse<object>.Success(new { ScheduleId = id, Room = room, ShiftedElectiveCount = shifted },
            $"急诊手术已插入 {room} 第 1 台，后续 {shifted} 台未开始择期手术已自动顺延");
    }

    [HttpGet("or-board/qc-metrics")]
    public async Task<ApiResponse<object>> GetOrQcMetrics()
    {
        using var conn = _connFactory.CreateConnection();
        int total = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mtc_surgery_schedules");
        int finished = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mtc_surgery_schedules WHERE status >= 2");
        int emergency = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mtc_surgery_schedules WHERE ext_data_json LIKE '%isEmergency%'");
        int whoComplete = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mtc_surgery_schedules WHERE sign_in_ok = 1 AND time_out_ok = 1 AND sign_out_ok = 1");
        int activeNarcotics = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM aims_narcotic_ledgers WHERE status <> 'RECONCILED'");
        int pacuInRoom = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM aims_pacu_assessments WHERE status = 'IN_PACU'");

        return ApiResponse<object>.Success(new
        {
            TotalSurgeries = total,
            FinishedSurgeries = finished,
            EmergencySurgeries = emergency,
            WhoChecklistRatePct = total > 0 ? Math.Round(whoComplete * 100m / total, 1) : 100m,
            UnreconciledNarcotics = activeNarcotics,
            PatientsInPacu = pacuInRoom
        });
    }

    // ===================== 2. 术前麻醉访视与标准风险/极量自动计算 =====================

    [HttpGet("preop-assessments")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetPreopAssessments()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, assessment_no AS AssessmentNo, schedule_id AS ScheduleId, patient_name AS PatientName,
                   weight_kg AS WeightKg, asa_class AS AsaClass, mallampati_class AS MallampatiClass,
                   stop_bang_score AS StopBangScore, fasting_hours AS FastingHours,
                   planned_anesthesia_method AS PlannedAnesthesiaMethod, tidal_volume_range_ml AS TidalVolumeRangeMl,
                   lidocaine_max_mg AS LidocaineMaxMg, ropivacaine_max_mg AS RopivacaineMaxMg,
                   risk_alerts AS RiskAlerts, assessor_name AS AssessorName
            FROM aims_preop_assessments ORDER BY assessed_at_ms DESC LIMIT 120")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("preop-assessments")]
    public async Task<ApiResponse<object>> CreatePreopAssessment([FromBody] CreatePreopDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ScheduleId) || string.IsNullOrWhiteSpace(req.PatientName))
            return ApiResponse<object>.Fail(400, "手术排班ID与患者姓名不能为空");

        decimal wt = req.WeightKg > 0 ? req.WeightKg : 65.0m;
        int tvLow = (int)Math.Round(wt * 6m);
        int tvHigh = (int)Math.Round(wt * 8m);
        string tvRange = $"{tvLow}~{tvHigh} ml (6-8ml/kg)";
        decimal lidoMax = Math.Min(300m, Math.Round(wt * 4.5m, 1)); // 不含肾上腺素利多卡因极量 4.5mg/kg，上限 300mg
        decimal ropiMax = Math.Min(225m, Math.Round(wt * 3.0m, 1)); // 罗哌卡因极量 3mg/kg，上限 225mg

        var alerts = new List<string>();
        if (req.MallampatiClass >= 3) alerts.Add($"Mallampati {req.MallampatiClass}级(困难气道高危，备可视喉镜/纤支镜)");
        if (req.StopBangScore >= 5) alerts.Add($"STOP-BANG {req.StopBangScore}分(重度OSA风险，术后需强化呼吸监护)");
        if (req.FastingHours < 6) alerts.Add($"禁食仅{req.FastingHours}h(反流误吸风险，必要时按饱胃诱导)");
        if (req.AsaClass.Contains("IV") || req.AsaClass.Contains("V")) alerts.Add($"ASA {req.AsaClass}级极高危麻醉");
        string riskText = alerts.Count > 0 ? string.Join("；", alerts) : "常规低中危，可按计划实施麻醉";

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "PREOP-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            INSERT INTO aims_preop_assessments
            (id, assessment_no, schedule_id, encounter_id, patient_name, weight_kg, asa_class, mallampati_class,
             stop_bang_score, fasting_hours, planned_anesthesia_method, tidal_volume_range_ml, lidocaine_max_mg,
             ropivacaine_max_mg, risk_alerts, assessor_name, assessed_at_ms,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Sid, @Eid, @Pat, @Wt, @Asa, @Mal,
             @Sb, @Fast, @Method, @Tv, @Lido,
             @Ropi, @Risk, @Doc, @Now,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id, No = no, Sid = req.ScheduleId.Trim(), Eid = req.EncounterId, Pat = req.PatientName.Trim(),
                Wt = wt, Asa = req.AsaClass, Mal = Math.Clamp(req.MallampatiClass, 1, 4), Sb = Math.Clamp(req.StopBangScore, 0, 8),
                Fast = req.FastingHours, Method = req.PlannedAnesthesiaMethod, Tv = tvRange, Lido = lidoMax, Ropi = ropiMax,
                Risk = riskText, Doc = ActorName, Now = now, Actor = ActorId
            }, tx);

        // 自动回写电子病历 emr_documents 术前麻醉访视记录
        await conn.ExecuteAsync(@"
            INSERT INTO emr_documents
            (id, doc_no, emr_scene, visit_or_encounter_id, patient_id, dept_id, template_id, template_version,
             doc_type_code, doc_title, clinical_time_ms, plain_text_summary, status, author_id,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @DocNo, 'IPD', @Enc, @Pat, 'ANESTHESIA_DEPT', 'TPL_PREOP_VISIT', 1,
             'ANESTHESIA_PREOP', @Title, @Now, @Summary, 2, @Actor,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = Guid.NewGuid().ToString("D"),
                DocNo = "EMR-" + no,
                Enc = string.IsNullOrWhiteSpace(req.EncounterId) ? req.ScheduleId.Trim() : req.EncounterId.Trim(),
                Pat = req.PatientName.Trim(),
                Title = $"术前麻醉访视记录 - {req.PatientName}",
                Now = now,
                Summary = $"ASA {req.AsaClass}级 | Mallampati {req.MallampatiClass}级 | STOP-BANG {req.StopBangScore}分 | 潮气量建议 {tvRange} | 利多卡因极量 {lidoMax}mg / 罗哌卡因极量 {ropiMax}mg | 风险提示：{riskText}",
                Actor = ActorId
            }, tx);
        tx.Commit();

        return ApiResponse<object>.Success(new { Id = id, AssessmentNo = no, TidalVolumeRangeMl = tvRange, LidocaineMaxMg = lidoMax, RopivacaineMaxMg = ropiMax, RiskAlerts = riskText },
            $"术前麻醉访视 {no} 已完成并回写电子病历");
    }

    // ===================== 3. 术中出入量实时平衡与应急危机事件 =====================

    [HttpGet("intraop-events/{scheduleId}")]
    public async Task<ApiResponse<object>> GetIntraopEvents(string scheduleId)
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, schedule_id AS ScheduleId, record_type AS RecordType,
                   category_name AS CategoryName, volume_ml AS VolumeMl,
                   event_detail AS EventDetail, recorded_by AS RecordedBy, recorded_at_ms AS RecordedAtMs
            FROM aims_intraop_fluid_events WHERE schedule_id = @Sid ORDER BY recorded_at_ms ASC",
            new { Sid = scheduleId })).ToList();

        decimal totalIn = rows.Where(r => (string)r.RecordType == "FLUID_IN").Sum(r => Convert.ToDecimal(r.VolumeMl));
        decimal totalOut = rows.Where(r => (string)r.RecordType == "FLUID_OUT").Sum(r => Convert.ToDecimal(r.VolumeMl));
        return ApiResponse<object>.Success(new
        {
            ScheduleId = scheduleId,
            TotalFluidInMl = totalIn,
            TotalFluidOutMl = totalOut,
            NetBalanceMl = totalIn - totalOut,
            Events = rows
        });
    }

    [HttpPost("intraop-events")]
    public async Task<ApiResponse<object>> AddIntraopEvent([FromBody] AddIntraopEventDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ScheduleId) || string.IsNullOrWhiteSpace(req.CategoryName))
            return ApiResponse<object>.Fail(400, "手术排班ID与事件/液体分类不能为空");

        string detail = req.EventDetail?.Trim() ?? string.Empty;
        // 恶性高热 MH 应急协议：按体重 2.5 mg/kg 自动附加丹曲林首剂与 20mg/瓶 折算支数
        if (req.RecordType == "CRISIS_EVENT" && (req.CategoryName.Contains("恶性高热") || req.CategoryName.Contains("MH", StringComparison.OrdinalIgnoreCase)))
        {
            decimal wt = req.PatientWeightKg is > 0 ? req.PatientWeightKg.Value : 65m;
            decimal dantroleneMg = Math.Round(wt * 2.5m, 1);
            int vials = (int)Math.Ceiling(dantroleneMg / 20m);
            detail = $"[MH 应急协议已激活] 立即停用挥发性麻醉药、纯氧高流量过度通气；丹曲林首剂 2.5mg/kg = {dantroleneMg}mg（约 {vials} 支×20mg），同步主动降温。{detail}";
        }

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO aims_intraop_fluid_events
            (id, schedule_id, record_type, category_name, volume_ml, event_detail, recorded_by, recorded_at_ms,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Sid, @Type, @Cat, @Vol, @Detail, @ActorName, @Now,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Sid = req.ScheduleId.Trim(), Type = req.RecordType.Trim().ToUpperInvariant(), Cat = req.CategoryName.Trim(), Vol = req.VolumeMl, Detail = detail, ActorName, Now = now, Actor = ActorId });

        return ApiResponse<object>.Success(new { Id = id, EventDetail = detail }, "术中记录已落库");
    }

    // ===================== 4. 毒麻精药品领用、空安瓿回收与残液双人销毁闭环 =====================

    [HttpGet("narcotics")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetNarcoticLedgers()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, ledger_no AS LedgerNo, schedule_id AS ScheduleId, patient_name AS PatientName,
                   drug_name AS DrugName, batch_no AS BatchNo, issued_ampoules AS IssuedAmpoules,
                   used_ampoules AS UsedAmpoules, returned_intact_ampoules AS ReturnedIntactAmpoules,
                   empty_ampoules_recovered AS EmptyAmpoulesRecovered, waste_volume_ml AS WasteVolumeMl,
                   anesthesiologist_name AS AnesthesiologistName, witness_name AS WitnessName, status AS Status
            FROM aims_narcotic_ledgers ORDER BY created_at_ms DESC LIMIT 150")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("narcotics/issue")]
    public async Task<ApiResponse<object>> IssueNarcotic([FromBody] IssueNarcoticDto req)
    {
        if (string.IsNullOrWhiteSpace(req.DrugName) || req.IssuedAmpoules <= 0)
            return ApiResponse<object>.Fail(400, "毒麻药品名称与领用支数必须有效");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "DMY-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            INSERT INTO aims_narcotic_ledgers
            (id, ledger_no, schedule_id, patient_name, drug_name, batch_no, issued_ampoules,
             anesthesiologist_name, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Sid, @Pat, @Drug, @Batch, @Qty,
             @Anes, 'ISSUED', 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Sid = req.ScheduleId, Pat = req.PatientName, Drug = req.DrugName.Trim(), Batch = req.BatchNo?.Trim(), Qty = req.IssuedAmpoules, Anes = ActorName, Now = now, Actor = ActorId }, tx);

        // 若药房存在匹配毒麻批次则同步扣减库存并记入 inv_stock_ledgers
        var batch = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT b.id AS BatchId, b.warehouse_or_pharmacy_id AS WhId, b.drug_id AS DrugId, b.quantity AS Qty
            FROM inv_drug_batches b INNER JOIN inv_drugs d ON d.id = b.drug_id
            WHERE d.drug_name = @Drug AND b.is_frozen = 0 AND b.quantity >= @Qty
            ORDER BY b.expiry_date_int ASC LIMIT 1",
            new { Drug = req.DrugName.Trim(), Qty = req.IssuedAmpoules }, tx);
        if (batch != null)
        {
            int newBal = Convert.ToInt32(batch.Qty) - req.IssuedAmpoules;
            await conn.ExecuteAsync("UPDATE inv_drug_batches SET quantity = @Qty, updated_at_ms = @Now WHERE id = @Bid", new { Qty = newBal, Now = now, Bid = (string)batch.BatchId }, tx);
            await conn.ExecuteAsync(@"
                INSERT INTO inv_stock_ledgers
                (id, warehouse_or_pharmacy_id, drug_id, batch_id, change_type, change_quantity, balance_after, reference_doc_no, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @Wh, @Did, @Bid, 'AIMS_NARCOTIC_ISSUE', @Chg, @Bal, @Ref, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = Guid.NewGuid().ToString("D"), Wh = (string)batch.WhId, Did = (string)batch.DrugId, Bid = (string)batch.BatchId, Chg = -req.IssuedAmpoules, Bal = newBal, Ref = no, Now = now, Actor = ActorId }, tx);
        }
        tx.Commit();
        return ApiResponse<object>.Success(new { Id = id, LedgerNo = no }, $"毒麻精药领用单 {no} 已登记（待术后空安瓿回收与双人核销）");
    }

    [HttpPost("narcotics/{id}/reconcile")]
    public async Task<ApiResponse<object>> ReconcileNarcotic(string id, [FromBody] ReconcileNarcoticDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.WitnessName))
            return ApiResponse<object>.Fail(400, "毒麻精药空安瓿核销与残液销毁必须由第二见证人双签");

        using var conn = _connFactory.CreateConnection();
        var item = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM aims_narcotic_ledgers WHERE id = @Id", new { Id = id });
        if (item == null) return ApiResponse<object>.Fail(404, "未找到毒麻领用台账");
        if ((string)item.status == "RECONCILED") return ApiResponse<object>.Fail(400, "该毒麻台账已完成核销");

        int issued = Convert.ToInt32(item.issued_ampoules);
        if (req.UsedAmpoules + req.ReturnedIntactAmpoules != issued)
            return ApiResponse<object>.Fail(400, $"账实不符阻断：实耗({req.UsedAmpoules}) + 完整退回({req.ReturnedIntactAmpoules}) != 领用总支数({issued})");
        if (req.EmptyAmpoulesRecovered != req.UsedAmpoules)
            return ApiResponse<object>.Fail(400, $"空安瓿核销不符阻断：回收空安瓿({req.EmptyAmpoulesRecovered}) 必须等于实耗支数({req.UsedAmpoules})");
        if (string.Equals(req.WitnessName.Trim(), (string?)item.anesthesiologist_name, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.Fail(400, "职责分离阻断：见证销毁人不得与领用麻醉医师为同一人");

        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(@"
            UPDATE aims_narcotic_ledgers
            SET used_ampoules = @Used, returned_intact_ampoules = @Ret, empty_ampoules_recovered = @Empty,
                waste_volume_ml = @Waste, witness_name = @Wit, status = 'RECONCILED',
                reconciled_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { Used = req.UsedAmpoules, Ret = req.ReturnedIntactAmpoules, Empty = req.EmptyAmpoulesRecovered, Waste = req.WasteVolumeMl, Wit = req.WitnessName.Trim(), Now = now, Actor = ActorId, Id = id });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("AIMS_NARCOTIC_RECONCILE", "aims_narcotic_ledgers", id, $"毒麻药双人核销完成：{(string)item.drug_name} 实耗 {req.UsedAmpoules} 支/回收空安瓿 {req.EmptyAmpoulesRecovered} 支/销毁残液 {req.WasteVolumeMl}ml，见证人：{req.WitnessName}", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, Status = "RECONCILED" }, "毒麻精药空安瓿回收与残液双人见证销毁核销通过");
    }

    // ===================== 5. PACU Modified Aldrete 离室门禁与 SBAR 交接 =====================

    [HttpGet("pacu")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetPacuList()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, schedule_id AS ScheduleId, patient_name AS PatientName,
                   activity_score AS ActivityScore, respiration_score AS RespirationScore,
                   circulation_score AS CirculationScore, consciousness_score AS ConsciousnessScore,
                   spo2_score AS Spo2Score, aldrete_total AS AldreteTotal,
                   sbar_handoff_summary AS SbarHandoffSummary, receiving_ward_nurse AS ReceivingWardNurse,
                   status AS Status
            FROM aims_pacu_assessments ORDER BY created_at_ms DESC LIMIT 120")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("pacu")]
    public async Task<ApiResponse<object>> SavePacuAssessment([FromBody] SavePacuDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ScheduleId) || string.IsNullOrWhiteSpace(req.PatientName))
            return ApiResponse<object>.Fail(400, "手术排班ID与患者姓名不能为空");

        int total = Math.Clamp(req.ActivityScore, 0, 2) + Math.Clamp(req.RespirationScore, 0, 2)
                  + Math.Clamp(req.CirculationScore, 0, 2) + Math.Clamp(req.ConsciousnessScore, 0, 2)
                  + Math.Clamp(req.Spo2Score, 0, 2);

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO aims_pacu_assessments
            (id, schedule_id, patient_name, activity_score, respiration_score, circulation_score, consciousness_score, spo2_score,
             aldrete_total, sbar_handoff_summary, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Sid, @Pat, @Act, @Resp, @Circ, @Cons, @Spo2,
             @Total, @Sbar, 'IN_PACU', 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id, Sid = req.ScheduleId.Trim(), Pat = req.PatientName.Trim(),
                Act = Math.Clamp(req.ActivityScore, 0, 2), Resp = Math.Clamp(req.RespirationScore, 0, 2),
                Circ = Math.Clamp(req.CirculationScore, 0, 2), Cons = Math.Clamp(req.ConsciousnessScore, 0, 2),
                Spo2 = Math.Clamp(req.Spo2Score, 0, 2), Total = total, Sbar = req.SbarHandoffSummary?.Trim(), Now = now, Actor = ActorId
            });

        return ApiResponse<object>.Success(new { Id = id, AldreteTotal = total, DischargeReady = total >= 9 },
            total >= 9 ? $"Modified Aldrete 评分 {total}/10（达标，可办理 PACU 转出交接）" : $"Modified Aldrete 评分 {total}/10（<9 分，继续在 PACU 留观监护）");
    }

    [HttpPost("pacu/{id}/handoff")]
    public async Task<ApiResponse<object>> CompletePacuHandoff(string id, [FromBody] PacuHandoffDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ReceivingWardNurse))
            return ApiResponse<object>.Fail(400, "请填写病区接班护士姓名");

        using var conn = _connFactory.CreateConnection();
        var item = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM aims_pacu_assessments WHERE id = @Id", new { Id = id });
        if (item == null) return ApiResponse<object>.Fail(404, "未找到 PACU 评估记录");

        int total = Convert.ToInt32(item.aldrete_total);
        if (total < 9)
            return ApiResponse<object>.Fail(400, $"PACU 离室门禁阻断：当前 Modified Aldrete 评分仅 {total}/10 分（需 ≥ 9 分方可转回病区）");

        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(@"
            UPDATE aims_pacu_assessments
            SET status = 'HANDED_OFF', receiving_ward_nurse = @Nurse,
                sbar_handoff_summary = COALESCE(@Sbar, sbar_handoff_summary),
                handed_off_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { Nurse = req.ReceivingWardNurse.Trim(), Sbar = req.SbarHandoffSummary?.Trim(), Now = now, Actor = ActorId, Id = id });

        return ApiResponse<object>.Success(new { Id = id, Status = "HANDED_OFF" }, $"已完成 PACU → 病区 SBAR 交接签收（接班护士：{req.ReceivingWardNurse}）");
    }

    // ===================== 6. 术后镇痛泵 (PCA) 与 24/48/72h VAS 随访 =====================

    [HttpGet("pca")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetPcaRecords()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, pca_no AS PcaNo, schedule_id AS ScheduleId, patient_name AS PatientName,
                   pca_mode AS PcaMode, formula_summary AS FormulaSummary, background_rate_ml_h AS BackgroundRateMlH,
                   bolus_dose_ml AS BolusDoseMl, lockout_minutes AS LockoutMinutes,
                   vas_24h AS Vas24h, vas_48h AS Vas48h, vas_72h AS Vas72h,
                   adverse_events AS AdverseEvents, status AS Status
            FROM aims_pca_records ORDER BY started_at_ms DESC LIMIT 120")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("pca")]
    public async Task<ApiResponse<object>> CreatePcaRecord([FromBody] CreatePcaDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.FormulaSummary))
            return ApiResponse<object>.Fail(400, "患者姓名与镇痛泵配方不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "PCA-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO aims_pca_records
            (id, pca_no, schedule_id, patient_name, pca_mode, formula_summary, background_rate_ml_h,
             bolus_dose_ml, lockout_minutes, status, started_at_ms,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Sid, @Pat, @Mode, @Formula, @Bg,
             @Bolus, @Lock, 'ACTIVE', @Now,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Sid = req.ScheduleId, Pat = req.PatientName.Trim(), Mode = req.PcaMode, Formula = req.FormulaSummary.Trim(), Bg = req.BackgroundRateMlH, Bolus = req.BolusDoseMl, Lock = req.LockoutMinutes, Now = now, Actor = ActorId });

        return ApiResponse<object>.Success(new { Id = id, PcaNo = no }, $"术后镇痛泵 {no} 已启用");
    }

    [HttpPut("pca/{id}/followup")]
    public async Task<ApiResponse<object>> UpdatePcaFollowup(string id, [FromBody] UpdatePcaFollowupDto req)
    {
        long now = SystemTime.UtcNowMs;
        string st = string.IsNullOrWhiteSpace(req.Status) ? "ACTIVE" : req.Status.Trim().ToUpperInvariant();
        long? stopped = st == "STOPPED" ? now : null;
        using var conn = _connFactory.CreateConnection();
        int n = await conn.ExecuteAsync(@"
            UPDATE aims_pca_records
            SET vas_24h = COALESCE(@V24, vas_24h), vas_48h = COALESCE(@V48, vas_48h), vas_72h = COALESCE(@V72, vas_72h),
                adverse_events = COALESCE(@Adv, adverse_events), status = @St,
                stopped_at_ms = COALESCE(@Stop, stopped_at_ms), updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { V24 = req.Vas24h, V48 = req.Vas48h, V72 = req.Vas72h, Adv = req.AdverseEvents?.Trim(), St = st, Stop = stopped, Now = now, Actor = ActorId, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(404, "未找到镇痛泵记录");
        return ApiResponse<object>.Success(new { Id = id, Status = st }, st == "STOPPED" ? "已完成术后镇痛随访并停泵拔管" : "已更新术后镇痛 VAS 评分与随访记录");
    }
}
