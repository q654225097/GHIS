using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Checkup.Models;

public class CfgOrderBundle : BaseEntity
{
    public string BundleCode { get; set; } = string.Empty;
    public string BundleName { get; set; } = string.Empty;
    public string? PinyinCode { get; set; }
    public string BundleCategory { get; set; } = "LAB_PANEL"; // LAB_PANEL, EXAM_PANEL, TREATMENT_SET, HEALTH_CHECKUP
    public string ApplicableScene { get; set; } = "ALL"; // ALL, OPD, IPD, CHECKUP
    public string GenderLimit { get; set; } = "ALL"; // ALL, MALE, FEMALE
    public string? DeptName { get; set; }
    public string? SpecimenType { get; set; }
    public decimal PackagePrice { get; set; }
    public decimal SumItemPrice { get; set; }
    public short IsAllowSplit { get; set; }
    public short IsActive { get; set; } = 1;
    public string? Remark { get; set; }
}

public class CfgOrderBundleItem : BaseEntity
{
    public string BundleId { get; set; } = string.Empty;
    public string ItemType { get; set; } = "PRICE_ITEM"; // PRICE_ITEM, DRUG, CONSUMABLE
    public string? TargetItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string UnitName { get; set; } = "次";
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal SubtotalAmount { get; set; }
    public string? ExecDeptName { get; set; }
    public int SortOrder { get; set; } = 10;
    public short IsRequired { get; set; } = 1;
}

public class ChkOrder : BaseEntity
{
    public string CheckupNo { get; set; } = string.Empty;
    public string GuideBarcode { get; set; } = string.Empty;
    public string? PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; } = 30;
    public string? IdCardMasked { get; set; }
    public string? PhoneMasked { get; set; }
    public string CheckupType { get; set; } = "PERSONAL"; // PERSONAL, GROUP
    public string? CompanyName { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = "UNPAID"; // UNPAID, PAID
    public string Status { get; set; } = "REGISTERED"; // REGISTERED, IN_PROGRESS, PENDING_SUMMARY, COMPLETED
    public int TotalItemCount { get; set; }
    public int DoneItemCount { get; set; }
    public int AbnormalCount { get; set; }
    public string? SummaryDoctorId { get; set; }
    public string? SummaryDoctorName { get; set; }
    public string? OverallConclusion { get; set; }
    public string? HealthAdvice { get; set; }
    public long RegisteredAtMs { get; set; }
    public long? CompletedAtMs { get; set; }
}

public class ChkOrderItem : BaseEntity
{
    public string OrderId { get; set; } = string.Empty;
    public string? BundleItemId { get; set; }
    public string DeptName { get; set; } = "检验科";
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string? ResultValue { get; set; }
    public string? ReferenceRange { get; set; }
    public short IsAbnormal { get; set; }
    public string AbnormalFlag { get; set; } = "NORMAL"; // NORMAL, HIGH, LOW, POSITIVE
    public string? ExamDoctorName { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, DONE
    public long? CheckedAtMs { get; set; }
    public int SortOrder { get; set; } = 10;
}
