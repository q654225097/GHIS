using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.HieGateway.Models;

public class HieEmpiMapping : BaseEntity
{
    public string LocalPatientId { get; set; } = string.Empty;
    public string EmpiId { get; set; } = string.Empty;
    public string IdCardBlindIndex { get; set; } = string.Empty;
    public string ExternalOrgCode { get; set; } = string.Empty;
    public string ExternalOrgName { get; set; } = string.Empty;
    public string ExternalPatientId { get; set; } = string.Empty;
    public decimal MatchConfidence { get; set; } = 1.0m;
    public long VerifiedAtMs { get; set; }
}

public class HieItemCodeMapping : BaseEntity
{
    public string Domain { get; set; } = "LAB"; // LAB / EXAM / DRUG / SURGERY / TREATMENT / ICD10
    public string LocalCode { get; set; } = string.Empty;
    public string StandardCode { get; set; } = string.Empty;
    public string ExternalOrgCode { get; set; } = string.Empty;
    public string ExternalCode { get; set; } = string.Empty;
    public short IsActive { get; set; } = 1;
}

public class HieExchangeLog : BaseEntity
{
    public string Direction { get; set; } = "OUTBOUND"; // INBOUND / OUTBOUND
    public string Protocol { get; set; } = "FHIR_R4"; // FHIR_R4 / IHE_XDSB / CDA_R2 / HL7_V2
    public string PeerOrgCode { get; set; } = string.Empty;
    public string? PatientId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string PayloadSm3Hash { get; set; } = string.Empty;
    public string Status { get; set; } = "SUCCESS";
    public string? ErrorMessage { get; set; }
}
