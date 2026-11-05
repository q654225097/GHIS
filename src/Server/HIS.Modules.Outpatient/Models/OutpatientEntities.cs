using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdScheduleSlot : BaseEntity
{
    public string CampusId { get; set; } = "CAMPUS-01";
    public string OpdDeptId { get; set; } = string.Empty;
    public string? ClinicRoomId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string? SubstituteDoctorId { get; set; }
    public int ScheduleDateInt { get; set; }
    public string SlotPeriodCode { get; set; } = "MORNING"; // MORNING / AFTERNOON / NIGHT
    public long StartsAtMs { get; set; }
    public long EndsAtMs { get; set; }
    public string RegLevelCode { get; set; } = "NORMAL";
    public decimal RegistrationFee { get; set; } = 15.00m;
    public int TotalCapacity { get; set; } = 30;
    public int ReservedCount { get; set; } = 0;
    public int SlotStatus { get; set; } = 1; // 1=Available, 2=Full, 3=Stopped
}

public class OpdAppointment : BaseEntity
{
    public string AppointmentNo { get; set; } = string.Empty;
    public string? RequestKey { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string ActiveUniqueToken { get; set; } = string.Empty;
    public int QueueNo { get; set; }
    public int Status { get; set; } = 0; // 0=Reserved, 1=Confirmed, 2=CheckedIn, 3=Cancelled, 4=Expired
    public long LockExpiresAtMs { get; set; }
    public string? VisitId { get; set; }
}

public class OpdVisit : BaseEntity
{
    public string VisitNo { get; set; } = string.Empty;
    public string? RequestKey { get; set; }
    public string CampusId { get; set; } = "CAMPUS-01";
    public string OpdDeptId { get; set; } = string.Empty;
    public string? ClinicRoomId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string? ScheduleId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public short IsEmergency { get; set; } = 0;
    public int? ErTriageLevel { get; set; }
    public short IsGreenChannel { get; set; } = 0;
    public decimal? GreenCreditLimit { get; set; }
    public short IsReturnVisit { get; set; } = 0;
    public int Status { get; set; } = 1; // 1=Waiting, 2=InConsultation, 3=Completed, 4=Cancelled
    public string? PrimaryDiagCode { get; set; }
    public string? PrimaryDiagName { get; set; }
    public string? EmrDocumentId { get; set; }
    public string? OfdArchiveId { get; set; }
    public long? SignedAtMs { get; set; }
}

public class OpdTriageQueue : BaseEntity
{
    public string VisitId { get; set; } = string.Empty;
    public string OpdDeptId { get; set; } = string.Empty;
    public string? ClinicRoomId { get; set; }
    public int PriorityScore { get; set; } = 100;
    public int PassCount { get; set; } = 0;
    public int QueueStatus { get; set; } = 1; // 1=Waiting, 2=Called, 3=Passed, 4=Finished
    public long CheckinAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? CalledAtMs { get; set; }
}

public class OpdPrescription : BaseEntity
{
    public string PrescriptionNo { get; set; } = string.Empty;
    public string VisitId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string OpdDeptId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string TargetPharmacyId { get; set; } = string.Empty;
    public string RxType { get; set; } = "WESTERN";
    public int? HerbDoses { get; set; }
    public decimal TotalAmount { get; set; }
    public int Status { get; set; } = 0; // 0=Draft, 1=AuditedPassed, 2=Rejected, 3=Paid, 4=Dispensed, 5=Returned
    public string? ReviewerPharmacistId { get; set; }
    public string? ReviewRemark { get; set; }
    public string? RxOfdArchiveId { get; set; }
}

public class OpdPrescriptionItem : BaseEntity
{
    public string PrescriptionId { get; set; } = string.Empty;
    public int GroupNo { get; set; } = 1;
    public string DrugId { get; set; } = string.Empty;
    public string DrugNameSnapshot { get; set; } = string.Empty;
    public decimal DoseOnce { get; set; }
    public string DoseUnit { get; set; } = "mg";
    public string FrequencyCode { get; set; } = "QD";
    public string RouteCode { get; set; } = "PO";
    public int Days { get; set; } = 3;
    public int TotalQuantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public int SkinTestFlag { get; set; } = 0; // 0=None, 1=Required, 2=Pending, 3=Negative(-), 4=Positive(+)
    public string? SkinTestBatchNo { get; set; }
    public string? SkinTestCheckerId { get; set; }
}

