using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdBed : BaseEntity
{
    public string CampusId { get; set; } = "CAMPUS-01";
    public string WardId { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
    public string BedCode { get; set; } = string.Empty;
    public string BedType { get; set; } = "STANDARD"; // STANDARD / EXTRA / ICU
    public decimal DailyFee { get; set; } = 60.00m;
    public int BedStatus { get; set; } = 0; // 0=Free, 1=Occupied, 2=Maintenance, 3=Reserved
    public string? CurrentEncounterId { get; set; }
}

public class IpdEncounter : BaseEntity
{
    public string AdmissionNo { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string CampusId { get; set; } = "CAMPUS-01";
    public string AdmitDeptId { get; set; } = string.Empty;  // 住院临床科室（医疗主体）
    public string? MedGroupId { get; set; }
    public string AdmitWardId { get; set; } = string.Empty;  // 护理病区（物理空间与护理主体）
    public short IsBorrowedBed { get; set; } = 0;           // 是否跨病区借床
    public string? BedCode { get; set; }
    public string ActiveBedToken { get; set; } = string.Empty;
    public string? AttendingDoctorId { get; set; }
    public string? PrimaryNurseId { get; set; }
    public string NursingLevelCode { get; set; } = "LEVEL_2";
    public decimal PrepaymentBalance { get; set; } = 0.00m;
    public string? ArchiveOfdId { get; set; }
    public int Status { get; set; } = 1; // 1=Admitted, 2=DischargePending, 3=Discharged, 4=Recalled
}

public class IpdTransferLog : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string TransferType { get; set; } = "DEPT_TRANSFER"; // BED_TRANSFER / WARD_TRANSFER / DEPT_TRANSFER
    public string FromDeptId { get; set; } = string.Empty;
    public string FromWardId { get; set; } = string.Empty;
    public string? FromBedCode { get; set; }
    public string ToDeptId { get; set; } = string.Empty;
    public string ToWardId { get; set; } = string.Empty;
    public string? ToBedCode { get; set; }
    public long HandoverOutAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? HandoverInAtMs { get; set; }
}

