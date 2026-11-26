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
using HIS.Modules.Monitoring.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Monitoring.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE")]
[Route("api/v4/monitoring")]
public class MonitoringController : ControllerBase
{
    private readonly IRepository<MonDevice> _deviceRepo;
    private readonly IRepository<MonVitalStream> _streamRepo;
    private readonly IRepository<MonAlarm> _alarmRepo;
    private readonly IRepository<MonRiskScore> _scoreRepo;
    private readonly IMonitoringContract _monitoringContract;
    private readonly IUserSession _userSession;
    private readonly IAuditTrailContract? _auditContract;

    public MonitoringController(
        IRepository<MonDevice> deviceRepo,
        IRepository<MonVitalStream> streamRepo,
        IRepository<MonAlarm> alarmRepo,
        IRepository<MonRiskScore> scoreRepo,
        IMonitoringContract monitoringContract,
        IUserSession userSession,
        IAuditTrailContract? auditContract = null)
    {
        _deviceRepo = deviceRepo;
        _streamRepo = streamRepo;
        _alarmRepo = alarmRepo;
        _scoreRepo = scoreRepo;
        _monitoringContract = monitoringContract;
        _userSession = userSession;
        _auditContract = auditContract;
    }

    public record BindDeviceDto(
        string DeviceCode,
        string WardId,
        string BedCode,
        string EncounterId,
        string? DeviceName,
        string? Protocol
    );

    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices(CancellationToken ct)
    {
        var list = await _deviceRepo.QueryAsync("SELECT * FROM mon_devices ORDER BY bed_code, device_code");
        return Ok(ApiResponse<IReadOnlyList<MonDevice>>.Success(list));
    }

    [HttpPost("devices/bind")]
    public async Task<IActionResult> BindDevice([FromBody] BindDeviceDto dto, CancellationToken ct)
    {
        var devices = await _deviceRepo.QueryAsync(
            "SELECT * FROM mon_devices WHERE device_code = @Code",
            new { Code = dto.DeviceCode });

        var dev = devices.FirstOrDefault();
        long now = SystemTime.UtcNowMs;
        if (dev == null)
        {
            dev = new MonDevice
            {
                Id = Guid.NewGuid().ToString("D"),
                DeviceCode = dto.DeviceCode,
                DeviceName = dto.DeviceName ?? $"床旁监护仪 ({dto.BedCode})",
                Protocol = dto.Protocol ?? "HL7_MLLP",
                WardId = dto.WardId,
                BedCode = dto.BedCode,
                CurrentEncounterId = dto.EncounterId,
                IsOnline = 1,
                CreatedAtMs = now,
                CreatedBy = _userSession.UserId ?? "SYSTEM",
                UpdatedAtMs = now,
                UpdatedBy = _userSession.UserId ?? "SYSTEM"
            };
            await _deviceRepo.InsertAsync(dev);
        }
        else
        {
            dev.WardId = dto.WardId;
            dev.BedCode = dto.BedCode;
            dev.CurrentEncounterId = dto.EncounterId;
            dev.IsOnline = 1;
            dev.UpdatedAtMs = now;
            dev.UpdatedBy = _userSession.UserId ?? "SYSTEM";
            await _deviceRepo.UpdateAsync(dev);
        }

        return Ok(ApiResponse<MonDevice>.Success(dev, "监护设备已成功绑定患者床位"));
    }

    public record IngestVitalStreamDto(
        string DeviceCode,
        string EncounterId,
        string PatientId,
        long? SampledAtMs,
        decimal? Hr,
        decimal? Rr,
        decimal? SysBp,
        decimal? DiaBp,
        decimal? Map,
        decimal? Spo2,
        decimal? TempC,
        decimal? Cvp,
        int? Gcs,
        string? EcgRhythm
    );

    [HttpPost("telemetry/ingest")]
    public async Task<IActionResult> IngestTelemetry([FromBody] IngestVitalStreamDto dto, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        long sampledMs = dto.SampledAtMs.HasValue && dto.SampledAtMs.Value > 0 ? dto.SampledAtMs.Value : now;

        // Alarm detection
        bool isAlarm = false;
        string? alarmType = null;
        string? alarmMsg = null;
        string alarmLevel = "INFO";

        if (dto.Hr.HasValue && dto.Hr.Value > 130)
        {
            isAlarm = true;
            alarmType = "TACHYCARDIA";
            alarmLevel = "WARNING";
            alarmMsg = $"持续心动过速: 心率 {dto.Hr} bpm (参考 < 100)";
        }
        else if (dto.Hr.HasValue && dto.Hr.Value < 45)
        {
            isAlarm = true;
            alarmType = "BRADYCARDIA";
            alarmLevel = "CRITICAL";
            alarmMsg = $"重度心动过缓: 心率 {dto.Hr} bpm (参考 > 60)";
        }
        else if (dto.Spo2.HasValue && dto.Spo2.Value < 90)
        {
            isAlarm = true;
            alarmType = "DESATURATION";
            alarmLevel = "CRITICAL";
            alarmMsg = $"低氧血症报警: SpO2 {dto.Spo2}% (参考 ≥ 95%)";
        }
        else if (dto.SysBp.HasValue && dto.SysBp.Value < 80)
        {
            isAlarm = true;
            alarmType = "HYPOTENSION";
            alarmLevel = "CRITICAL";
            alarmMsg = $"休克血压报警: 收缩压 {dto.SysBp} mmHg (参考 ≥ 90)";
        }

        var stream = new MonVitalStream
        {
            Id = Guid.NewGuid().ToString("D"),
            DeviceId = dto.DeviceCode,
            EncounterId = dto.EncounterId,
            PatientId = dto.PatientId,
            SampledAtMs = sampledMs,
            Hr = dto.Hr,
            Rr = dto.Rr,
            SysBp = dto.SysBp,
            DiaBp = dto.DiaBp,
            Map = dto.Map ?? (dto.SysBp.HasValue && dto.DiaBp.HasValue ? (dto.SysBp.Value + 2 * dto.DiaBp.Value) / 3 : null),
            Spo2 = dto.Spo2,
            TempC = dto.TempC,
            Cvp = dto.Cvp,
            Gcs = dto.Gcs,
            EcgRhythm = dto.EcgRhythm ?? "SINUS",
            IsAlarm = isAlarm ? (short)1 : (short)0,
            AlarmLevel = isAlarm ? alarmLevel : null,
            CreatedAtMs = now,
            CreatedBy = "DEVICE_INGEST",
            UpdatedAtMs = now,
            UpdatedBy = "DEVICE_INGEST"
        };
        await _streamRepo.InsertAsync(stream);

        if (isAlarm)
        {
            var alarm = new MonAlarm
            {
                Id = Guid.NewGuid().ToString("D"),
                StreamId = stream.Id,
                EncounterId = dto.EncounterId,
                PatientId = dto.PatientId,
                AlarmType = alarmType ?? "VITAL_ABNORMAL",
                Severity = alarmLevel,
                Message = alarmMsg ?? "生理指标偏离安全阈值",
                Acked = 0,
                CreatedAtMs = now,
                CreatedBy = "AUTO_ALARM",
                UpdatedAtMs = now,
                UpdatedBy = "AUTO_ALARM"
            };
            await _alarmRepo.InsertAsync(alarm);
        }

        return Ok(ApiResponse<MonVitalStream>.Success(stream, isAlarm ? $"已记录体征数据并触发【{alarmLevel}】级别报警" : "监护数据采集正常"));
    }

    [HttpPost("scores/evaluate/{encounterId}")]
    public async Task<IActionResult> EvaluateScores(
        string encounterId,
        [FromQuery] string? scoreType,
        CancellationToken ct)
    {
        var result = await _monitoringContract.EvaluateRiskScoreAsync(encounterId, scoreType ?? "MEWS", ct);
        return Ok(ApiResponse<RiskScoreEvaluationResultDto>.Success(result, "综合病情危重评分计算与临床知识库分析完成"));
    }

    [HttpGet("streams/{encounterId}")]
    public async Task<IActionResult> GetStreams(
        string encounterId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        string sql = "SELECT * FROM mon_vital_streams WHERE encounter_id = @EncounterId ORDER BY sampled_at_ms DESC LIMIT @Limit";
        var list = await _streamRepo.QueryAsync(sql, new { EncounterId = encounterId, Limit = limit });
        return Ok(ApiResponse<IReadOnlyList<MonVitalStream>>.Success(list));
    }

    [HttpGet("alarms")]
    public async Task<IActionResult> GetAlarms(
        [FromQuery] string? encounterId,
        [FromQuery] bool onlyUnacked = true,
        CancellationToken ct = default)
    {
        string sql = "SELECT * FROM mon_alarms WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(encounterId)) sql += " AND encounter_id = @EncounterId";
        if (onlyUnacked) sql += " AND acked = 0";
        sql += " ORDER BY created_at_ms DESC LIMIT 100";

        var list = await _alarmRepo.QueryAsync(sql, new { EncounterId = encounterId });
        return Ok(ApiResponse<IReadOnlyList<MonAlarm>>.Success(list));
    }

    [HttpPost("alarms/{id}/ack")]
    public async Task<IActionResult> AckAlarm(string id, CancellationToken ct)
    {
        var alarm = await _alarmRepo.GetByIdAsync(id);
        if (alarm == null) return NotFound(ApiResponse<object>.Fail(404, "报警记录不存在"));

        alarm.Acked = 1;
        alarm.AckedBy = _userSession.UserId ?? "SYSTEM";
        alarm.AckedAtMs = SystemTime.UtcNowMs;
        alarm.UpdatedAtMs = SystemTime.UtcNowMs;
        alarm.UpdatedBy = _userSession.UserId ?? "SYSTEM";
        await _alarmRepo.UpdateAsync(alarm);

        return Ok(ApiResponse<MonAlarm>.Success(alarm, "报警已确认解除"));
    }
}
