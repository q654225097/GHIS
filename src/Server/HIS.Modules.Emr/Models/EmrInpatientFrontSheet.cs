using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrInpatientFrontSheet : BaseEntity
{
	public string EncounterId { get; set; } = string.Empty;
	public string PatientId { get; set; } = string.Empty;
	public string MedicalRecordNo { get; set; } = string.Empty;
	public int AdmissionTimes { get; set; } = 1;
	public string PayTypeCode { get; set; } = "SOCIAL_SECURITY";
	public string? HealthCardNo { get; set; }
	public string AdmitDeptId { get; set; } = string.Empty;
	public string AdmitWardId { get; set; } = string.Empty;
	public long AdmitTimeMs { get; set; }
	public string? DischargeDeptId { get; set; }
	public string? DischargeWardId { get; set; }
	public long? DischargeTimeMs { get; set; }
	public int InpatientDays { get; set; } = 0;
	public string? OutpatientDiagCode { get; set; }
	public string? OutpatientDiagName { get; set; }
	public string? PrimaryDiagCode { get; set; }
	public string? PrimaryDiagName { get; set; }
	public string? PrimaryAdmissionCondition { get; set; }
	public string? OtherDiagsJson { get; set; }
	public string? SurgeriesJson { get; set; }
	public decimal TotalCost { get; set; } = 0;
	public decimal SelfPayAmount { get; set; } = 0;
	public string? FeeBreakdownJson { get; set; }
	public string? ChiefPhysicianId { get; set; }
	public string? AttendingPhysicianId { get; set; }
	public string? ResidentPhysicianId { get; set; }
	public string? PrimaryNurseId { get; set; }
	public string? QualityLevel { get; set; }
	public string Status { get; set; } = "DRAFT";
}
