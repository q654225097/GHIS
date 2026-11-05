using System.Text.Json;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Patient.Models;

namespace HIS.Modules.Patient.Services;

public class PatientReadOnlyContract : IPatientReadOnlyContract
{
    private readonly IRepository<PatPatient> _patientRepo;

    public PatientReadOnlyContract(IRepository<PatPatient> patientRepo)
    {
        _patientRepo = patientRepo;
    }

    public async Task<PatientSummaryDto?> GetPatientSummaryAsync(string patientId, CancellationToken ct = default)
    {
        var p = await _patientRepo.GetByIdAsync(patientId, ct);
        if (p == null) return null;

        IReadOnlyList<string> allergies = string.IsNullOrEmpty(p.AllergyTagsJson)
            ? Array.Empty<string>()
            : (JsonSerializer.Deserialize<List<string>>(p.AllergyTagsJson) ?? new List<string>());

        var birthYear = p.BirthDateInt / 10000;
        var currentYear = DateTime.UtcNow.Year;
        var age = Math.Max(0, currentYear - birthYear);

        return new PatientSummaryDto(
            p.Id,
            p.MedicalRecordNo,
            p.FullName,
            p.GenderCode,
            p.BirthDateInt,
            age,
            p.PhoneMasked ?? "",
            allergies,
            p.IsTempEmergency == 1,
            p.IsActive == 1
        );
    }
}
