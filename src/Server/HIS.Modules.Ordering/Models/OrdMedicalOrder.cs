using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Ordering.Models;

public class OrdMedicalOrder : BaseEntity
{
	public string OrderNo { get; set; } = string.Empty;

	public string EncounterId { get; set; } = string.Empty;

	public string PatientId { get; set; } = string.Empty;

	public string OrderIpdDeptId { get; set; } = string.Empty;

	public string PatientWardId { get; set; } = string.Empty;

	public string ExecTargetDeptId { get; set; } = string.Empty;

	public int OrderTermType { get; set; } = 1;

	public int GroupNo { get; set; } = 1;

	public string OrderCategory { get; set; } = "DRUG";

	public string OrderSubType { get; set; } = "NORMAL";

	public string? ParentOrderId { get; set; }

	public string ItemCode { get; set; } = string.Empty;

	public string ItemName { get; set; } = string.Empty;

	public decimal Dosage { get; set; }

	public string FrequencyCode { get; set; } = "QD";

	public string RouteCode { get; set; } = "IV_DRIP";

	public int Status { get; set; } = 0;

	public string OrderDoctorId { get; set; } = string.Empty;

	public string? VerifyNurseId { get; set; }

	public long PlanStartAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public long? PlanStopAtMs { get; set; }

	public string? StopDoctorId { get; set; }

	public long? ActualStopAtMs { get; set; }

	public string? VoidReason { get; set; }

	public string? VoidDoctorId { get; set; }

	public long? VoidAtMs { get; set; }

	public string ApprovalStatus { get; set; } = "DRAFT";

	public string? WorkflowInstanceId { get; set; }

	public long? SubmittedAtMs { get; set; }

	public long? ApprovedAtMs { get; set; }

	public string? ApprovedBy { get; set; }
}
