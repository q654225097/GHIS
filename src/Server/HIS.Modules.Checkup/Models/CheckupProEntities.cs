using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Checkup.Models;

public class ChkAppointment : BaseEntity
{
    public string ApptNo { get; set; } = string.Empty;
    public int ApptDateInt { get; set; }
    public string TimeSlot { get; set; } = "08:00-10:00";
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; } = 30;
    public string? PhoneMasked { get; set; }
    public string? IdCardMasked { get; set; }
    public string ExamCategory { get; set; } = "HEALTH"; // HEALTH, OCCUPATIONAL, FOOD_HEALTH
    public string? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Status { get; set; } = "BOOKED"; // BOOKED, CHECKED_IN, CANCELLED
    public string? ConvertedOrderId { get; set; }
    public string? Remark { get; set; }
}

public class ChkCompany : BaseEntity
{
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? CreditCode { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public decimal DiscountRate { get; set; } = 0.90m;
    public string SettlementMode { get; set; } = "POST_SETTLE"; // PRE_PAY, POST_SETTLE
    public short IsActive { get; set; } = 1;
    public string? Remark { get; set; }
}

public class ChkGroupOrder : BaseEntity
{
    public string GroupNo { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string GenderLimit { get; set; } = "ALL";
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public int PlannedCount { get; set; } = 50;
    public int RegisteredCount { get; set; }
    public int CompletedCount { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public string SettlementStatus { get; set; } = "UNSETTLED"; // UNSETTLED, SETTLED
    public string? Remark { get; set; }
}

public class ChkKnowledgeBase : BaseEntity
{
    public string KbCode { get; set; } = string.Empty;
    public string DiseaseName { get; set; } = string.Empty;
    public string MatchKeywords { get; set; } = string.Empty;
    public string SeverityLevel { get; set; } = "B_IMPORTANT"; // A_CRITICAL, B_IMPORTANT, C_NORMAL
    public string ConclusionTemplate { get; set; } = string.Empty;
    public string AdviceTemplate { get; set; } = string.Empty;
    public string? SuggestDept { get; set; }
    public short IsActive { get; set; } = 1;
}

public class ChkFollowup : BaseEntity
{
    public string OrderId { get; set; } = string.Empty;
    public string CheckupNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneMasked { get; set; }
    public string PositiveGrade { get; set; } = "B_IMPORTANT"; // A_CRITICAL, B_IMPORTANT
    public string FindingSummary { get; set; } = string.Empty;
    public string? SuggestDept { get; set; }
    public string FollowupStatus { get; set; } = "PENDING"; // PENDING, NOTIFIED, REFERRED, CLOSED
    public string? FollowupNote { get; set; }
    public string? ReferredPreAdmissionId { get; set; }
    public string? HandledBy { get; set; }
    public long? HandledAtMs { get; set; }
}

public class ChkOccupationalRecord : BaseEntity
{
    public string OrderId { get; set; } = string.Empty;
    public string CheckupNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string OccCategory { get; set; } = "IN_JOB"; // PRE_JOB, IN_JOB, POST_JOB, FOOD_HEALTH
    public string HazardFactors { get; set; } = "噪声";
    public int WorkYears { get; set; } = 1;
    public string OccConclusion { get; set; } = "NO_ABNORMAL"; // NO_ABNORMAL, RECHECK, SUSPECTED_OCC, CONTRAINDICATION, QUALIFIED_CERT
    public string? ConclusionDetail { get; set; }
    public string? HealthCertNo { get; set; }
    public int? HealthCertValidUntil { get; set; }
    public string ReportUploadStatus { get; set; } = "PENDING"; // PENDING, REPORTED
    public long? UploadedAtMs { get; set; }
    public string? UploadPayloadJson { get; set; }
}
