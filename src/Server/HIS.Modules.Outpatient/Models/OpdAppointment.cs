using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdAppointment : BaseEntity
{
	public string AppointmentNo { get; set; } = string.Empty;

	public string? RequestKey { get; set; }

    public string? PaymentId { get; set; }

    public decimal PaidAmount { get; set; }

    public string? PaymentChannel { get; set; }

    public long? PaidAtMs { get; set; }

    public string? RefundId { get; set; }

    public string? RefundReason { get; set; }

	public string PatientId { get; set; } = string.Empty;

	public string ScheduleId { get; set; } = string.Empty;

	public string ActiveUniqueToken { get; set; } = string.Empty;

	public int QueueNo { get; set; }

	public int Status { get; set; } = 0;

	public long LockExpiresAtMs { get; set; }

	public string? VisitId { get; set; }
}
