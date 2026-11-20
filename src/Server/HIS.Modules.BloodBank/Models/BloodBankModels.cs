using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.BloodBank.Models;

public class BldBloodDictionary : BaseEntity
{
    public string BloodCode { get; set; } = string.Empty; // RBC / FFP / PLT / CRYO
    public string BloodName { get; set; } = string.Empty;
    public decimal StandardVolumeMl { get; set; }
    public string StorageTempRange { get; set; } = "2~6℃";
    public int ExpiryDays { get; set; } = 35;
    public decimal UnitPrice { get; set; }
    public short IsActive { get; set; } = 1;
}

public class BldInventoryBag : BaseEntity
{
    public string BagBarcode { get; set; } = string.Empty;
    public string DonationCode { get; set; } = string.Empty;
    public string SourceBloodCenter { get; set; } = "市中心血站";
    public string BloodCode { get; set; } = string.Empty;
    public string AboType { get; set; } = "O"; // A / B / AB / O
    public string RhType { get; set; } = "POSITIVE"; // POSITIVE / NEGATIVE
    public decimal VolumeMl { get; set; }
    public int CollectionDateInt { get; set; }
    public int ExpiryDateInt { get; set; }
    public decimal ColdChainTemp { get; set; } = 4.0m;
    public string Status { get; set; } = "IN_STOCK"; // IN_STOCK / RESERVED / CROSSMATCHED / DISPENSED / TRANSFUSED / RETURNED_EMPTY / SCRAPPED
    public string? ReservedForPatientId { get; set; }
    public string? MatchedRequestId { get; set; }
}

public class BldStockPolicy : BaseEntity
{
    public string BloodCode { get; set; } = string.Empty;
    public string AboType { get; set; } = string.Empty;
    public string RhType { get; set; } = "POSITIVE";
    public int SafeStockBags { get; set; } = 10;
    public int WarningStockBags { get; set; } = 5;
    public decimal SurgeryReserveRatio { get; set; } = 1.2m;
    public short IsActive { get; set; } = 1;
}

public class BldCrossmatchRecord : BaseEntity
{
    public string BloodRequestId { get; set; } = string.Empty;
    public string BagId { get; set; } = string.Empty;
    public string BagBarcode { get; set; } = string.Empty;
    public string PatientAbo { get; set; } = string.Empty;
    public string PatientRh { get; set; } = string.Empty;
    public string DonorAbo { get; set; } = string.Empty;
    public string DonorRh { get; set; } = string.Empty;
    public string AntibodyScreenResult { get; set; } = "NEGATIVE"; // NEGATIVE / POSITIVE
    public string MajorCrossmatch { get; set; } = "COMPATIBLE"; // COMPATIBLE / INCOMPATIBLE
    public string MinorCrossmatch { get; set; } = "COMPATIBLE"; // COMPATIBLE / INCOMPATIBLE
    public string Result { get; set; } = "COMPATIBLE"; // COMPATIBLE / INCOMPATIBLE
    public string TechnicianId { get; set; } = string.Empty;
    public string VerifierId { get; set; } = string.Empty;
    public long MatchedAtMs { get; set; }
    public string? Remark { get; set; }
}

public class BldTransfusionExecution : BaseEntity
{
    public string BloodRequestId { get; set; } = string.Empty;
    public string BagId { get; set; } = string.Empty;
    public string BagBarcode { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientWristbandCode { get; set; } = string.Empty;
    public string StartNurseId { get; set; } = string.Empty;
    public string DualCheckNurseId { get; set; } = string.Empty;
    public string? PreVitalsJson { get; set; }
    public string? Mid15VitalsJson { get; set; }
    public string? PostVitalsJson { get; set; }
    public long StartedAtMs { get; set; }
    public long? EndedAtMs { get; set; }
    public long? EmptyBagReturnedAtMs { get; set; }
    public string Status { get; set; } = "TRANSFUSING"; // TRANSFUSING / FINISHED / TERMINATED
    public string? TerminationReason { get; set; }
}

public class BldTransfusionReaction : BaseEntity
{
    public string ExecutionId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string BagBarcode { get; set; } = string.Empty;
    public string ReactionType { get; set; } = "ALLERGIC"; // ALLERGIC / FEBRILE / HEMOLYTIC / TRALI / TACO
    public string Severity { get; set; } = "MODERATE"; // MILD / MODERATE / SEVERE
    public string Symptoms { get; set; } = string.Empty;
    public string? LabRecheckJson { get; set; }
    public string Disposition { get; set; } = string.Empty;
    public string ReportedBy { get; set; } = string.Empty;
    public long ReportedAtMs { get; set; }
    public short SharedExternal { get; set; }
}

public class BldExternalSyncLog : BaseEntity
{
    public string PeerBloodCenter { get; set; } = string.Empty;
    public string SyncType { get; set; } = "STOCK_INVENTORY"; // STOCK_INVENTORY / RARE_BLOOD_DISPATCH / REACTION_REPORT
    public string Direction { get; set; } = "INBOUND";
    public string PayloadSummary { get; set; } = string.Empty;
    public string Status { get; set; } = "SUCCESS";
}
