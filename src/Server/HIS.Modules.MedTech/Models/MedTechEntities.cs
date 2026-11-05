using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class SysMedtechExecRouting : BaseEntity
{
    public string CampusId { get; set; } = "CAMPUS-01";
    public string SourceScene { get; set; } = "ALL"; // ALL / OPD / IPD / ER
    public string? OrderDeptId { get; set; }
    public string ServiceItemCode { get; set; } = string.Empty;
    public string ExecMedTechDeptId { get; set; } = string.Empty;
    public string? ExecRoomCode { get; set; }
    public string TimeWindowType { get; set; } = "ALL_DAY";
    public int PriorityWeight { get; set; } = 10;
    public short IsActive { get; set; } = 1;
}

public class MtcServiceRequest : BaseEntity
{
    public string RequestNo { get; set; } = string.Empty;
    public string SourceType { get; set; } = "OPD"; // OPD / IPD
    public string? SourceVisitId { get; set; }
    public string? SourceOrderId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string OrderDeptId { get; set; } = string.Empty;
    public string? OrderWardId { get; set; }
    public string ExecMedTechDeptId { get; set; } = string.Empty;
    public string MedTechSubType { get; set; } = "LAB"; // LAB / RADIOLOGY / ULTRASOUND / ENDOSCOPY / PATHOLOGY
    public string? ExecRoomCode { get; set; }
    public string? SpecimenTubeCode { get; set; }
    public string ServiceItemCode { get; set; } = string.Empty;
    public decimal FeeAmount { get; set; }
    public short IsFeeConfirmed { get; set; } = 0; // 1=FeeConfirmed (locked from direct refund)
    public int Status { get; set; } = 0; // 0=Created, 1=Collected, 2=Received/Confirmed, 3=ReportPublished, 4=Cancelled
    public long? CollectedAtMs { get; set; }
    public long? ReceivedAtMs { get; set; }
    public long? ExecutedAtMs { get; set; }
    public string? ReportSummary { get; set; }
    public string? ReportOfdArchiveId { get; set; }
    public short IsCriticalValue { get; set; } = 0;
    public string? CriticalAckDocId { get; set; }
    public string? CriticalAckNurseId { get; set; }
}

public class MtcSurgerySchedule : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string OrDeptId { get; set; } = string.Empty;
    public string OrRoomCode { get; set; } = string.Empty;
    public int TableSeq { get; set; } = 1;
    public string SurgeryCode { get; set; } = string.Empty;
    public string SurgeryName { get; set; } = string.Empty;
    public int SurgeryLevel { get; set; } = 1; // 1 to 4
    public string MainSurgeonId { get; set; } = string.Empty;
    public string AnesthesiologistId { get; set; } = string.Empty;
    public string CirculatingNurseId { get; set; } = string.Empty;
    public short SignInOk { get; set; } = 0;
    public short TimeOutOk { get; set; } = 0;
    public short SignOutOk { get; set; } = 0;
    public short InstrumentCountBalanced { get; set; } = 0;
    public int Status { get; set; } = 0; // 0=Scheduled, 1=InSurgery, 2=Finished, 3=Cancelled
}

public class MtcBloodRequest : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string AboType { get; set; } = "O";
    public string RhType { get; set; } = "POSITIVE";
    public string BloodProductCode { get; set; } = "RBC";
    public int VolumeMl { get; set; } = 400;
    public string? CrossMatchResult { get; set; }
    public string? BagBarcode { get; set; }
    public string? DispensedById { get; set; }
    public string? DualCheckDispenserId { get; set; }
    public short EmptyBagReturned { get; set; } = 0;
    public int Status { get; set; } = 0; // 0=Requested, 1=Matched, 2=Dispensed, 3=Transfused, 4=BagReturned
}

