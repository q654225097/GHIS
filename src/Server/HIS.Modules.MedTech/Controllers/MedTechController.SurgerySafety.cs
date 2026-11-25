using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.MedTech.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.MedTech.Controllers;

public partial class MedTechController
{
    private const string Surgeon = "SURGEON";
    private const string Anesthetist = "ANESTHETIST";
    private const string Circulating = "CIRCULATING";
    private const string Scrub = "SCRUB";
    private static readonly string[] ClinicalDuties = [Surgeon, Anesthetist, Circulating];
    private static readonly string[] CountDuties = [Circulating, Scrub];
    private static readonly SafetyStep[] SafetySteps =
    [
        new("SIGN_IN", "麻醉前核查", [new("IDENTITY", "患者身份、术式和手术部位一致"),
            new("CONSENT", "知情同意及相关资料已核对"), new("READINESS", "麻醉及术前准备已核对")]),
        new("PRE_INCISION_COUNT", "切皮前基础清点", []),
        new("TIME_OUT", "切皮前核查", [new("IDENTITY", "团队确认患者、术式和部位"),
            new("RISKS", "关键风险及准备情况已复核"), new("EQUIPMENT", "器械及所需资料已核对")]),
        new("PRE_CLOSURE_COUNT", "关闭切口前清点", []),
        new("POST_CLOSURE_COUNT", "关闭切口后清点", []),
        new("SIGN_OUT", "离室前核查", [new("PROCEDURE", "实际术式已核对"),
            new("SPECIMEN", "标本处置及记录已核对"), new("HANDOFF", "术后与交接事项已核对")])
    ];

    private sealed record SafetyStep(string Code, string Name, SafetyCheck[] Checks)
    {
        public bool IsCount => Checks.Length == 0;
        public string[] Duties => IsCount ? CountDuties : ClinicalDuties;
    }

    public sealed record SafetyCheck(string Code, string Label);
    public sealed record SafetyCountInput(int? Gauze, int? Needles, int? Instruments, int? AddedGauze, int? AddedNeedles, int? AddedInstruments);
    public sealed record SafetyCounts(int Gauze, int Needles, int Instruments, int AddedGauze, int AddedNeedles, int AddedInstruments);
    public sealed record SurgerySafetyInput(int ExpectedVersion, Dictionary<string, bool>? Checks, SafetyCountInput? Counts, string? Note);
    public sealed record SafetyEvidence(Dictionary<string, bool>? Checks, SafetyCounts? Counts);
    public sealed record SurgerySafetySignature(string Id, string StepCode, string DutyCode, string OperatorId, string OperatorName,
        long RecordedAtMs, bool Passed, string? Note, SafetyEvidence Evidence);
    public sealed record SurgerySafetyDuty(string Code, string Name, string? AssignedUserId, bool HasPassed);
    public sealed record SurgerySafetyStepView(string Code, string Name, bool IsCount, IReadOnlyList<SafetyCheck> Checks,
        IReadOnlyList<SurgerySafetyDuty> Duties, bool Completed, bool HasCountConflict);
    public sealed record SurgerySafetyState(string ScheduleId, string ScheduleNo, string PatientId, string PatientName, string EncounterNo,
        string SurgeryName, string OrRoomId, int Status, int Version, string? CurrentStepCode, string? CurrentDutyCode, string? BlockReason,
        bool CanStart, bool CanFinish, bool HasArchivedAnesthesia, IReadOnlyList<SurgerySafetyStepView> Steps, IReadOnlyList<SurgerySafetySignature> History);

    private sealed class SafetyEntry
    {
        public string Id { get; set; } = string.Empty;
        public string StepCode { get; set; } = string.Empty;
        public string DutyCode { get; set; } = string.Empty;
        public string OperatorId { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public long RecordedAtMs { get; set; }
        public string EvidenceJson { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string? Note { get; set; }

        public SurgerySafetySignature ToSignature() => new(Id, StepCode, DutyCode, OperatorId, OperatorName, RecordedAtMs,
            Passed, Note, JsonSerializer.Deserialize<SafetyEvidence>(EvidenceJson) ?? new(null, null));
    }

    private sealed class SafetyPatient
    {
        public string PatientName { get; set; } = string.Empty;
        public string EncounterNo { get; set; } = string.Empty;
        public int Status { get; set; }
    }
    private sealed record SafetySnapshot(SurgerySafetyState State, Dictionary<string, SafetyCounts> CompletedCounts);

    private static Dictionary<string, string?> SurgeryTeam(MteSurgerySchedule schedule) => new(StringComparer.Ordinal)
    {
        [Surgeon] = schedule.SurgeonId, [Anesthetist] = schedule.AnesthesiologistId,
        [Circulating] = schedule.CirculatingNurseId, [Scrub] = schedule.ScrubNurseId
    };

    private static string DutyName(string duty) => duty switch
    {
        Surgeon => "主刀医师", Anesthetist => "麻醉医师", Circulating => "巡回护士", Scrub => "器械护士", _ => duty
    };

    private string? CurrentSurgeryDuty(Dictionary<string, string?> team)
    {
        if (CurrentUserId.Length == 0) return null;
        var assigned = team.Where(x => string.Equals(x.Value, CurrentUserId, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToArray();
        if (assigned.Length != 1) return null;
        string role = RoleCode.Normalize(_session?.RoleCode);
        return assigned[0] switch
        {
            Surgeon when role is RoleCode.IpdAttending or "IPD-CHIEF" or "SURGEON" or "DOCTOR-IPD" => Surgeon,
            Anesthetist when role is "ANESTHESIOLOGIST" or "ANESTHESIA" or RoleCode.IpdAttending or "IPD-CHIEF" or "DOCTOR-IPD" => Anesthetist,
            Circulating or Scrub when role is "OR-NURSE" or RoleCode.WardNurse => assigned[0],
            _ => null
        };
    }

    private bool CanReadSurgery(MteSurgerySchedule schedule) => RoleCode.IsAdmin(_session?.RoleCode) ||
        CurrentSurgeryDuty(SurgeryTeam(schedule)) != null;

    private async Task<SafetySnapshot> ReadSurgerySafetyAsync(MteSurgerySchedule schedule, CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        var tx = _connFactory.CurrentTransaction;
        var team = SurgeryTeam(schedule);
        string? myDuty = CurrentSurgeryDuty(team);
        var patient = await conn.QuerySingleOrDefaultAsync<SafetyPatient>(new CommandDefinition("""
            SELECT p.full_name AS PatientName, e.admission_no AS EncounterNo, e.status AS Status
            FROM ipd_encounters e JOIN pat_patients p ON p.id = e.patient_id
            WHERE e.id = @Encounter AND e.patient_id = @Patient
            """, new { Encounter = schedule.EncounterId, Patient = schedule.PatientId }, tx, cancellationToken: ct));
        string? blocking = null;
        if (schedule.Status is 0 or 1)
        {
            if (patient?.Status != 1)
                blocking = "排班与当前在院患者不一致，请核实住院记录";
            else if (schedule.ApprovalStatus != "APPROVED")
                blocking = "手术排班尚未审批通过";
            else if (team.Values.Any(string.IsNullOrWhiteSpace) || team.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != team.Count)
                blocking = "排班须指定四个不同账号：主刀医师、麻醉医师、巡回护士和器械护士";
            else if (await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM sys_users WHERE id IN @Ids AND is_active = 1", new { Ids = team.Values.ToArray() }, tx, cancellationToken: ct)) != team.Count)
                blocking = "手术参与账号未配置或已停用，请先核对排班";
        }

        var history = (await conn.QueryAsync<SafetyEntry>(new CommandDefinition("""
            SELECT id, step_code, duty_code, operator_id, operator_name, recorded_at_ms, evidence_json, passed, note
            FROM mte_surgery_safety_entries WHERE schedule_id = @Id ORDER BY schedule_version DESC
            """, new { schedule.Id }, tx, cancellationToken: ct))).Select(x => x.ToSignature()).ToArray();
        var latest = history.GroupBy(x => (x.StepCode, x.DutyCode)).ToDictionary(g => g.Key, g => g.First());
        var steps = new List<SurgerySafetyStepView>(SafetySteps.Length);
        var counts = new Dictionary<string, SafetyCounts>(StringComparer.Ordinal);
        bool precedingComplete = true;
        foreach (var step in SafetySteps)
        {
            var entries = step.Duties.Select(d => latest.GetValueOrDefault((step.Code, d))).ToArray();
            bool countConflict = step.IsCount && entries.All(e => e?.Evidence.Counts != null) && entries[0]!.Evidence.Counts != entries[1]!.Evidence.Counts;
            var duties = step.Duties.Select((d, index) => new SurgerySafetyDuty(d, DutyName(d), team[d],
                entries[index] is { Passed: true } e && e.OperatorId == team[d])).ToArray();
            bool completed = precedingComplete && !countConflict && duties.All(d => d.HasPassed);
            if (completed && step.IsCount && entries[0]!.Evidence.Counts is { } count) counts.Add(step.Code, count);
            steps.Add(new SurgerySafetyStepView(step.Code, step.Name, step.IsCount, step.Checks, duties, completed, countConflict));
            precedingComplete = completed;
        }

        bool preIncisionVerified = steps.Take(3).All(x => x.Completed);
        if (schedule.Status == 1 && !preIncisionVerified && blocking == null)
            blocking = "本台手术缺少开台前电子核查，不能补造历史签署，请按院方异常流程核对";
        string? next = schedule.Status switch
        {
            0 => steps.Take(3).FirstOrDefault(x => !x.Completed)?.Code,
            1 when preIncisionVerified => steps.Skip(3).FirstOrDefault(x => !x.Completed)?.Code,
            _ => null
        };
        bool coordinator = myDuty is Surgeon or Circulating;
        bool archived = schedule.Status is 1 or 2 && await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM mte_anesthesia_records WHERE schedule_id = @Id AND encounter_id = @EncounterId
                AND patient_id = @PatientId AND status = 1 AND anesthesiologist_id = @AnesthesiologistId
            """, schedule, tx, cancellationToken: ct)) == 1;
        return new(new SurgerySafetyState(schedule.Id, schedule.ScheduleNo, schedule.PatientId, patient?.PatientName ?? schedule.PatientName,
            patient?.EncounterNo ?? schedule.EncounterId, schedule.SurgeryName, schedule.OrRoomId, schedule.Status, schedule.VersionNo, next, myDuty,
            blocking, coordinator && blocking == null && schedule.Status == 0 && preIncisionVerified,
            coordinator && blocking == null && schedule.Status == 1 && archived && steps.All(x => x.Completed), archived, steps, history), counts);
    }

    [HttpGet("surgeries/{id}/safety")]
    public async Task<ApiResponse<SurgerySafetyState>> GetSurgerySafety(string id, CancellationToken ct)
    {
        var schedule = await _surgeryScheduleRepo.GetByIdAsync(id, ct);
        if (schedule == null || !CanReadSurgery(schedule))
            return ApiResponse<SurgerySafetyState>.Fail(404, "当前岗位未找到有权查看的手术排班");
        return ApiResponse<SurgerySafetyState>.Success((await ReadSurgerySafetyAsync(schedule, ct)).State);
    }

    [HttpPost("surgeries/{id}/safety/{stepCode}")]
    public async Task<ApiResponse<SurgerySafetyState>> RecordSurgerySafetyStep(string id, string stepCode,
        [FromBody] SurgerySafetyInput input, CancellationToken ct)
    {
        var definition = SafetySteps.SingleOrDefault(x => x.Code == stepCode);
        if (definition == null || input.Note?.Trim().Length > 500)
            return ApiResponse<SurgerySafetyState>.Fail(400, "核查阶段无效或备注超过 500 字");
        try
        {
            return await _connFactory.ExecuteInTransactionAsync(async token =>
            {
                var schedule = await _surgeryScheduleRepo.GetByIdAsync(id, token);
                if (schedule == null || !CanReadSurgery(schedule))
                    return ApiResponse<SurgerySafetyState>.Fail(404, "当前岗位未找到有权查看的手术排班");
                var snapshot = await ReadSurgerySafetyAsync(schedule, token);
                var state = snapshot.State;
                if (state.CurrentDutyCode is not { } duty || !definition.Duties.Contains(duty))
                    return ApiResponse<SurgerySafetyState>.Fail(403, "当前登录岗位不是本台手术本阶段的签署人");
                if (state.BlockReason != null)
                    return ApiResponse<SurgerySafetyState>.Fail(409, state.BlockReason);
                if (state.CurrentStepCode != definition.Code || state.Version != input.ExpectedVersion)
                    return ApiResponse<SurgerySafetyState>.Fail(409, "手术阶段或签署记录已变化，请刷新后核对");

                string? note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
                SafetyEvidence evidence;
                bool passed;
                if (definition.IsCount)
                {
                    var count = NormalizeSurgeryCounts(input, definition.Code == "PRE_INCISION_COUNT");
                    if (count == null)
                        return ApiResponse<SurgerySafetyState>.Fail(400, "须记录各项实际清点数量及追加数量，数量须为 0 至 100000 的整数");
                    passed = definition.Code == "PRE_INCISION_COUNT" ||
                        (snapshot.CompletedCounts.TryGetValue("PRE_INCISION_COUNT", out var baseline) && MatchesSurgeryCounts(count, baseline) &&
                         (!snapshot.CompletedCounts.TryGetValue("PRE_CLOSURE_COUNT", out var preClosure) ||
                          (count.AddedGauze >= preClosure.AddedGauze && count.AddedNeedles >= preClosure.AddedNeedles && count.AddedInstruments >= preClosure.AddedInstruments)));
                    evidence = new(null, count);
                }
                else
                {
                    if (input.Counts != null || input.Checks == null || input.Checks.Count != definition.Checks.Length ||
                        definition.Checks.Any(c => !input.Checks.ContainsKey(c.Code)))
                        return ApiResponse<SurgerySafetyState>.Fail(400, "必须逐项选择当前阶段的核查结果");
                    passed = input.Checks.Values.All(x => x);
                    evidence = new(definition.Checks.ToDictionary(x => x.Code, x => input.Checks[x.Code]), null);
                }
                if (!passed && note == null)
                    return ApiResponse<SurgerySafetyState>.Fail(400, "未通过或清点不平衡时必须记录实际差异");

                string evidenceJson = JsonSerializer.Serialize(evidence);
                var previous = state.History.FirstOrDefault(x => x.StepCode == stepCode && x.DutyCode == duty);
                if (previous != null && previous.OperatorId == CurrentUserId && previous.Note == note &&
                    JsonSerializer.Serialize(previous.Evidence) == evidenceJson)
                    return ApiResponse<SurgerySafetyState>.Success(state, "该结果已记录，未重复签署");

                string entryId = Guid.NewGuid().ToString("D");
                using var conn = _connFactory.CreateConnection();
                await conn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO mte_surgery_safety_entries
                        (id, schedule_id, schedule_version, step_code, duty_code, operator_id, operator_name, recorded_at_ms, evidence_json, passed, note)
                    VALUES (@Id, @ScheduleId, @Version, @Step, @Duty, @Actor, @Operator, @Now, @Evidence, @Passed, @Note)
                    """, new { Id = entryId, ScheduleId = id, Version = schedule.VersionNo + 1, Step = stepCode, Duty = duty,
                        Actor = CurrentUserId, Operator = _session!.DisplayName, Now = SystemTime.UtcNowMs, Evidence = evidenceJson,
                        Passed = passed ? 1 : 0, Note = note }, _connFactory.CurrentTransaction, cancellationToken: token));

                var saved = (await ReadSurgerySafetyAsync(schedule, token)).State;
                ApplySurgerySafetyProjection(schedule, saved);
                await SaveSurgeryAndMirrorAsync(schedule, token);
                await RecordAuditAsync("MTE_SURGERY_SAFETY_SIGN", nameof(MteSurgerySchedule), schedule.Id,
                    $"阶段={stepCode}；职责={duty}；记录={entryId}；本次结果={(passed ? "通过" : "未通过")}", token);
                var stage = saved.Steps.Single(x => x.Code == stepCode);
                return ApiResponse<SurgerySafetyState>.Success(saved with { Version = schedule.VersionNo }, stage.Completed ? "本阶段核查已通过" :
                    !passed || stage.HasCountConflict ? "已记录异常，当前阶段未放行" : "已记录本人签署，等待本阶段其他人员核对");
            }, ct);
        }
        catch (DbConcurrencyException)
        {
            return ApiResponse<SurgerySafetyState>.Fail(409, "排台信息已变化，请刷新后核对");
        }
    }

    private static SafetyCounts? NormalizeSurgeryCounts(SurgerySafetyInput input, bool initial)
    {
        if (input.Checks != null || input.Counts is not { } c) return null;
        int?[] numbers = [c.Gauze, c.Needles, c.Instruments, c.AddedGauze ?? (initial ? 0 : null),
            c.AddedNeedles ?? (initial ? 0 : null), c.AddedInstruments ?? (initial ? 0 : null)];
        if (numbers.Any(n => n is null or < 0 or > 100000) || (initial && numbers.Skip(3).Any(n => n != 0))) return null;
        return new(numbers[0]!.Value, numbers[1]!.Value, numbers[2]!.Value, numbers[3]!.Value, numbers[4]!.Value, numbers[5]!.Value);
    }

    private static bool MatchesSurgeryCounts(SafetyCounts count, SafetyCounts baseline) =>
        count.Gauze == baseline.Gauze + count.AddedGauze && count.Needles == baseline.Needles + count.AddedNeedles &&
        count.Instruments == baseline.Instruments + count.AddedInstruments;

    private static void ApplySurgerySafetyProjection(MteSurgerySchedule schedule, SurgerySafetyState state)
    {
        short Completed(string code) => (short)(state.Steps.Single(x => x.Code == code).Completed ? 1 : 0);
        schedule.WhoSignInOk = Completed("SIGN_IN");
        schedule.WhoTimeoutOk = Completed("TIME_OUT");
        schedule.WhoSignOutOk = Completed("SIGN_OUT");
        schedule.InstrumentCountBalanced = Completed("POST_CLOSURE_COUNT");
    }

    private async Task SaveSurgeryAndMirrorAsync(MteSurgerySchedule schedule, CancellationToken ct)
    {
        schedule.UpdatedBy = CurrentUserId;
        if (await _surgeryScheduleRepo.UpdateAsync(schedule, ct) != 1)
            throw new DbConcurrencyException("mte_surgery_schedules", schedule.Id, "手术排台已变化");
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE mtc_surgery_schedules SET status = @Status, sign_in_ok = @WhoSignInOk, time_out_ok = @WhoTimeoutOk,
                sign_out_ok = @WhoSignOutOk, instrument_count_balanced = @InstrumentCountBalanced,
                updated_at_ms = @UpdatedAtMs, updated_by = @UpdatedBy, version_no = version_no + 1
            WHERE id = @Id AND encounter_id = @EncounterId
            """, schedule, _connFactory.CurrentTransaction, cancellationToken: ct));
    }

    private async Task<ApiResponse<MteSurgerySchedule>> AdvanceSurgeryThroughSafetyAsync(string id, bool finish, CancellationToken ct)
    {
        try
        {
            return await _connFactory.ExecuteInTransactionAsync(async token =>
            {
                var schedule = await _surgeryScheduleRepo.GetByIdAsync(id, token);
                if (schedule == null || !CanReadSurgery(schedule))
                    return ApiResponse<MteSurgerySchedule>.Fail(404, "当前岗位未找到有权查看的手术排班");
                if (CurrentSurgeryDuty(SurgeryTeam(schedule)) is not (Surgeon or Circulating))
                    return ApiResponse<MteSurgerySchedule>.Fail(403, "仅本台主刀医师或巡回护士可以开台或办理结束");
                var safety = (await ReadSurgerySafetyAsync(schedule, token)).State;
                if (schedule.Status == (finish ? 2 : 1) && safety.Steps.Take(finish ? 6 : 3).All(x => x.Completed))
                    return ApiResponse<MteSurgerySchedule>.Success(schedule, finish ? "手术已结束" : "手术已开台");
                if (safety.BlockReason != null) return ApiResponse<MteSurgerySchedule>.Fail(409, safety.BlockReason);
                if (finish && schedule.Status == 1 && safety.Steps.All(x => x.Completed) && !safety.HasArchivedAnesthesia)
                    return ApiResponse<MteSurgerySchedule>.Fail(409, "本台手术的麻醉记录尚未完成并归档，不得结束手术");
                if (!(finish ? safety.CanFinish : safety.CanStart))
                    return ApiResponse<MteSurgerySchedule>.Fail(409, finish ? "当前阶段尚未完成，不得结束手术" : "麻醉前、清点和切皮前核查未通过，不得开台");
                if (finish) schedule.ActualEndMs = SystemTime.UtcNowMs;
                else schedule.ActualStartMs = SystemTime.UtcNowMs;

                schedule.Status = finish ? 2 : 1;
                ApplySurgerySafetyProjection(schedule, safety);
                await SaveSurgeryAndMirrorAsync(schedule, token);
                await RecordAuditAsync(finish ? "MTE_SURGERY_FINISH" : "MTE_SURGERY_START", nameof(MteSurgerySchedule), schedule.Id,
                    $"手术 [{schedule.ScheduleNo}] {(finish ? "阶段核查完成并结束" : "完成术前及切皮前核查并开台")}", token);
                return ApiResponse<MteSurgerySchedule>.Success(schedule, finish ? "手术已结束，可交接复苏" : "手术已开台");
            }, ct);
        }
        catch (DbConcurrencyException)
        {
            return ApiResponse<MteSurgerySchedule>.Fail(409, "手术排台状态已变化，请刷新后重试");
        }
    }
}
