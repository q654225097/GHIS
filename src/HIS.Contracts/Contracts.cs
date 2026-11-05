namespace HIS.Contracts;

public interface IUserSession
{
    string UserId { get; }
    string UserName { get; }
    string DisplayName { get; }
    string CampusId { get; }
    DepartmentCategory ActiveDeptCategory { get; }
    string ActiveDeptId { get; }
    string ActiveWardId { get; }
    IReadOnlyList<string> LinkedWardIds { get; }
    IReadOnlyList<string> Roles { get; }
    DataScope DataScope { get; }
    int SessionVersion { get; }
    int PrescribeLevel { get; }
    int SurgeryLevel { get; }
    int EmrSignLevel { get; }

    bool HasPermission(string controlKey);
    ControlAccessPolicy GetControlPolicy(string controlKey);
}

public sealed record PatientSummaryDto(
    string Id,
    string MedicalRecordNo,
    string FullName,
    string GenderCode,
    int BirthDateInt,
    int AgeYears,
    string PhoneMasked,
    IReadOnlyList<string> AllergyTags,
    bool IsTempEmergency,
    bool IsActive
);

public sealed record DepartmentSummaryDto(
    string Id,
    string CampusId,
    string DeptCode,
    string DeptName,
    DepartmentCategory DeptCategory,
    string? SubSpecialtyType,
    string? DefaultPharmacyId,
    int RatedBedCount,
    bool IsAllowAppointment,
    bool IsActive
);

public sealed record MedTechRoutingResultDto(
    string ExecMedTechDeptId,
    string ExecRoomCode,
    string ServiceItemCode,
    string MedTechSubType
);

public sealed record PriceItemSnapshotDto(
    string ItemCode,
    string ItemName,
    decimal UnitPrice,
    string UnitName,
    string FeeCategory
);

public interface IPatientReadOnlyContract
{
    Task<PatientSummaryDto?> GetPatientSummaryAsync(string patientId, CancellationToken ct = default);
}

public interface IDepartmentReadOnlyContract
{
    Task<DepartmentSummaryDto?> GetDeptSummaryAsync(string deptId, CancellationToken ct = default);
    Task<bool> IsDeptWardRelationValidAsync(string ipdDeptId, string wardId, CancellationToken ct = default);
}

public interface IMedTechRoutingContract
{
    Task<MedTechRoutingResultDto?> ResolveRoutingAsync(
        string campusId,
        string sourceScene,
        string orderDeptId,
        string serviceItemCode,
        long requestTimeMs,
        CancellationToken ct = default);
}

public interface IPriceTableContract
{
    Task<PriceItemSnapshotDto?> GetPriceSnapshotAsync(string itemCode, long atTimeMs, CancellationToken ct = default);
}

public interface IPharmacyInventoryContract
{
    Task<bool> CheckStockAvailabilityAsync(string pharmacyId, string drugId, int requestedQuantity, CancellationToken ct = default);
}

public interface IEmrMacroExtractorContract
{
    Task<string?> ExtractMacroValueAsync(string visitOrEncounterId, string macroSourcePath, CancellationToken ct = default);
}

public interface IIntegrationEvent
{
    string EventId { get; }
    string EventType { get; }
    long OccurredAtMs { get; }
}

public sealed record OutboxMessageDto(
    string Id,
    string EventType,
    string PayloadJson,
    long CreatedAtMs,
    int Status,
    int RetryCount,
    string? LastError
);

