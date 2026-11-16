namespace HIS.Modules.Inpatient.Models;

public sealed class IpdEncounterView : IpdEncounter
{
    public string PatientName { get; set; } = string.Empty;

    public string MedicalRecordNo { get; set; } = string.Empty;

    public string DeptName { get; set; } = string.Empty;

    public string WardName { get; set; } = string.Empty;

    public string? AttendingDoctorName { get; set; }

    public string? PrimaryNurseName { get; set; }
}
