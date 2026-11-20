using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Monitoring.Models;

public class MonDevice : BaseEntity
{
    public string DeviceCode { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Protocol { get; set; } = "HL7_MLLP"; // HL7_MLLP / MQTT / HTTP_GATEWAY
    public string WardId { get; set; } = string.Empty;
    public string? BedCode { get; set; }
    public string? CurrentEncounterId { get; set; }
    public short IsOnline { get; set; } = 1;
    public short IsActive { get; set; } = 1;
}

public class MonVitalStream : BaseEntity
{
    public string DeviceId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public long SampledAtMs { get; set; }
    public decimal? Hr { get; set; }
    public decimal? Rr { get; set; }
    public decimal? SysBp { get; set; }
    public decimal? DiaBp { get; set; }
    public decimal? Map { get; set; }
    public decimal? Spo2 { get; set; }
    public decimal? TempC { get; set; }
    public decimal? Cvp { get; set; }
    public int? Gcs { get; set; }
    public string? EcgRhythm { get; set; }
    public short IsAlarm { get; set; }
    public string? AlarmLevel { get; set; } // INFO / WARNING / CRITICAL
}

public class MonAlarm : BaseEntity
{
    public string? StreamId { get; set; }
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string AlarmType { get; set; } = string.Empty; // TACHYCARDIA / HYPOTENSION / DESATURATION / FEVER / APNEA
    public string Severity { get; set; } = "WARNING"; // WARNING / CRITICAL
    public string Message { get; set; } = string.Empty;
    public short Acked { get; set; }
    public string? AckedBy { get; set; }
    public long? AckedAtMs { get; set; }
}

public class MonRiskScore : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string ScoreType { get; set; } = "MEWS"; // APACHE_II / SOFA / NEWS2 / MEWS
    public int TotalScore { get; set; }
    public string RiskLevel { get; set; } = "LOW"; // LOW / MEDIUM / HIGH / CRITICAL
    public string? ComponentScoresJson { get; set; }
    public string? DrugLabCorrelationJson { get; set; }
    public string? KbGuidanceText { get; set; }
    public long CalculatedAtMs { get; set; }
}
