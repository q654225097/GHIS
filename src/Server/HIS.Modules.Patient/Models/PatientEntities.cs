using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Patient.Models;

public class PatPatient : BaseEntity
{
    public string MedicalRecordNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PinyinCode { get; set; }
    public string GenderCode { get; set; } = "1"; // 1=Male, 2=Female, 0=Unknown
    public int BirthDateInt { get; set; }
    public string IdCardType { get; set; } = "RESIDENT_ID";
    public string? IdCardCipher { get; set; }
    public string IdCardHash { get; set; } = string.Empty;
    public string? PhoneCipher { get; set; }
    public string? PhoneMasked { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianIdHash { get; set; }
    public string? AllergyTagsJson { get; set; }
    public short IsTempEmergency { get; set; } = 0;
    public string? MergedToPatientId { get; set; }
    public short IsActive { get; set; } = 1;
}

