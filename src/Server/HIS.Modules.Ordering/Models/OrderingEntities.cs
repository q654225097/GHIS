using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Ordering.Models;

public class OrdMedicalOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string OrderIpdDeptId { get; set; } = string.Empty; // 开单住院科
    public string PatientWardId { get; set; } = string.Empty;  // 患者所在病区（校对与执行病区）
    public string ExecTargetDeptId { get; set; } = string.Empty;
    public int OrderTermType { get; set; } = 1; // 1=Standing, 2=Stat, 3=PRN, 4=SOS
    public int GroupNo { get; set; } = 1;
    public string OrderCategory { get; set; } = "DRUG"; // DRUG / TREATMENT / LAB / EXAM / NURSING
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal Dosage { get; set; }
    public string FrequencyCode { get; set; } = "QD";
    public string RouteCode { get; set; } = "IV_DRIP";
    public int Status { get; set; } = 0; // 0=Draft, 1=Verified, 2=Executing, 3=Stopped, 4=Discontinued
    public string OrderDoctorId { get; set; } = string.Empty;
    public string? VerifyNurseId { get; set; }
    public long PlanStartAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? PlanStopAtMs { get; set; }
}

public class OrdExecutionPlan : BaseEntity
{
    public string OrderId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public long PlannedTimeMs { get; set; }
    public int ExecStatus { get; set; } = 0; // 0=Pending, 1=Executed, 2=Cancelled
    public string? ExecNurseId { get; set; }
    public string? DualCheckNurseId { get; set; }
    public long? ExecutedAtMs { get; set; }
    public string? PdaScanToken { get; set; }
}

