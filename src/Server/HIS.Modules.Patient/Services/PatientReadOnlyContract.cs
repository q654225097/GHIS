using System.Text.Json;
using HIS.BuildingBlocks.Common;
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

        var birthDate = ToDate(p.BirthDateInt);
        var today = DateOnly.FromDateTime(SystemTime.ShanghaiNow.DateTime);
        var age = Math.Max(0, today.Year - birthDate.Year
            - (today < birthDate.AddYears(today.Year - birthDate.Year) ? 1 : 0));

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

    public async Task<bool> AppendPatientAllergyAsync(string patientId, string allergyTag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId) || string.IsNullOrWhiteSpace(allergyTag))
            return false;

        var patient = await _patientRepo.GetByIdAsync(patientId, ct);
        if (patient == null || patient.IsActive != 1)
            return false;

        var tags = string.IsNullOrWhiteSpace(patient.AllergyTagsJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(patient.AllergyTagsJson) ?? new List<string>();
        var normalized = allergyTag.Trim();
        if (!tags.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(normalized);
            patient.AllergyTagsJson = JsonSerializer.Serialize(tags);
            await _patientRepo.UpdateAsync(patient, ct);
        }
        return true;
    }

    private static DateOnly ToDate(int dateInt)
    {
        try
        {
            return new DateOnly(dateInt / 10000, dateInt / 100 % 100, dateInt % 100);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateOnly.FromDateTime(SystemTime.ShanghaiNow.DateTime);
        }
    }
}
