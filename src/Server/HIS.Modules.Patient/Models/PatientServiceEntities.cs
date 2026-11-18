using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Patient.Models;

public class SrvSatisfactionSurvey : BaseEntity
{
    public string? EncounterId { get; set; }
    public string? VisitId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public int Score { get; set; } = 5;
    public int MedicalSkillScore { get; set; } = 5;
    public int ServiceAttitudeScore { get; set; } = 5;
    public int EnvironmentScore { get; set; } = 5;
    public string? Comment { get; set; }
    public long SubmittedAtMs { get; set; }
}

public class SrvComplaint : BaseEntity
{
    public string ComplaintNo { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientPhone { get; set; }
    public string DeptId { get; set; } = string.Empty;
    public string ComplaintType { get; set; } = "SERVICE_ATTITUDE";
    public string Description { get; set; } = string.Empty;
    public int Status { get; set; } = 0; // 0=SUBMITTED, 1=ACCEPTED, 2=PROCESSING, 3=RESOLVED, 4=CLOSED
    public string? HandlerId { get; set; }
    public string? HandlerName { get; set; }
    public string? HandlingResult { get; set; }
    public string? PatientSatisfaction { get; set; }
}

public class SrvInformedConsent : BaseEntity
{
    public string ConsentNo { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string? EncounterId { get; set; }
    public string ConsentType { get; set; } = "SURGERY_CONSENT";
    public string Title { get; set; } = string.Empty;
    public string? DocumentContentSummary { get; set; }
    public string SignerType { get; set; } = "PATIENT";
    public string SignerName { get; set; } = string.Empty;
    public long SignedAtMs { get; set; }
    public string WitnessDoctorId { get; set; } = string.Empty;
    public string SignatureHash { get; set; } = string.Empty;
    public int Status { get; set; } = 1; // 1=SIGNED, 2=REVOKED
}

// DTOs
public sealed record SubmitSatisfactionRequest(
    string PatientId,
    string PatientName,
    string DeptId,
    string? EncounterId = null,
    string? VisitId = null,
    int Score = 5,
    int MedicalSkillScore = 5,
    int ServiceAttitudeScore = 5,
    int EnvironmentScore = 5,
    string? Comment = null
);

public sealed record SubmitComplaintRequest(
    string PatientId,
    string PatientName,
    string? PatientPhone,
    string DeptId,
    string ComplaintType,
    string Description
);

public sealed record ProcessComplaintRequest(
    int Status,
    string? HandlerId,
    string? HandlerName,
    string? HandlingResult
);

public sealed record CloseComplaintRequest(
    string HandlingResult,
    string? PatientSatisfaction
);

public sealed record RecordInformedConsentRequest(
    string PatientId,
    string? EncounterId,
    string ConsentType,
    string Title,
    string DocumentContentSummary,
    string SignerType,
    string SignerName,
    string WitnessDoctorId,
    string SignatureHash
);

public sealed record PatientServiceSummaryDto(
    int TotalSurveys,
    decimal AverageScore,
    int OpenComplaintsCount,
    int ResolvedComplaintsCount,
    int TotalSignedConsents
);
