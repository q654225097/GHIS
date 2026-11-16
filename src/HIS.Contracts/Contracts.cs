namespace HIS.Contracts;

public interface IUserSession
{
    string UserId { get; }
    string UserName { get; }
    string DisplayName { get; }
    string RoleCode { get; }
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
    bool IsAdmin { get; }

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
    Task<bool> AppendPatientAllergyAsync(string patientId, string allergyTag, CancellationToken ct = default);
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

    Task<DrugCatalogSnapshotDto?> GetDrugSnapshotAsync(string drugIdOrCode, CancellationToken ct = default)
    {
        return Task.FromResult<DrugCatalogSnapshotDto?>(null);
    }

    Task<StockMutationResultDto> DeductFefoStockAsync(string pharmacyId, string drugIdOrCode, int requestedQuantity, string referenceDocNo, string? operatorId = null, CancellationToken ct = default, string? paymentOrderId = null)
    {
        return Task.FromResult(new StockMutationResultDto(true, "OK", requestedQuantity, Array.Empty<BatchDeductionItemDto>()));
    }

    Task<StockMutationResultDto> RestoreStockFromReturnAsync(string pharmacyId, string referenceDocNo, string? operatorId = null, CancellationToken ct = default)
    {
        return Task.FromResult(new StockMutationResultDto(true, "OK", 0, Array.Empty<BatchDeductionItemDto>()));
    }
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

public sealed record BatchDeductionItemDto(
    string BatchId,
    string BatchNo,
    int ExpiryDateInt,
    int DeductedQuantity,
    int BalanceAfter
);

public sealed record ClinicalDictionaryItemDto(
    string Id,
    string DictType,
    string ItemCode,
    string ItemName,
    string? PinyinCode,
    int SortOrder,
    bool IsDefault,
    bool IsActive
);

public sealed record DrugCatalogSnapshotDto(
    string DrugId,
    string DrugCode,
    string DrugName,
    string Spec,
    string UnitName,
    decimal UnitPrice,
    int AntibioticLevel,
    bool IsNarcotic,
    bool IsSkinTestRequired,
    bool IsActive
);

public sealed record Icd10DiagnosisDto(
    string Id,
    string IcdCode,
    string DiseaseName,
    string? PinyinCode,
    string CategoryName,
    string GenderExclusive,
    bool IsChronicSpecial,
    bool IsInfectiousReport,
    bool IsActive
);

public sealed record OrderFrequencySnapshotDto(
    string Id,
    string FreqCode,
    string FreqName,
    int DailyTimes,
    string TimePointsText,
    IReadOnlyList<string> ParsedTimePoints,
    string ApplicableTerm,
    bool IsActive
);

public sealed record StockMutationResultDto(
    bool IsSuccess,
    string Message,
    int TotalQuantityProcessed,
    IReadOnlyList<BatchDeductionItemDto> BatchItems
);

public interface IAuditTrailContract
{
    Task RecordAuditAsync(
        string eventType,
        string targetEntity,
        string targetId,
        string? actionDetail = null,
        string? userId = null,
        string? operatorName = null,
        string? deptId = null,
        CancellationToken ct = default);
}

public interface IIdempotencyContract
{
    Task<(bool IsDuplicate, string? CachedResponseJson, bool IsConflict)> TryAcquireAsync(
        string requestKey,
        string payloadJson,
        TimeSpan? ttl = null,
        CancellationToken ct = default);

    Task SaveResponseAsync(
        string requestKey,
        string responseJson,
        CancellationToken ct = default);
}

public interface IMasterDataReadOnlyContract
{
    Task<Icd10DiagnosisDto?> GetDiagnosisByCodeAsync(string icdCode, CancellationToken ct = default);
    Task<OrderFrequencySnapshotDto?> GetOrderFrequencyAsync(string freqCode, CancellationToken ct = default);
    Task<IReadOnlyList<ClinicalDictionaryItemDto>> GetDictionaryItemsAsync(string dictType, CancellationToken ct = default);
}

public interface IOutboxContract
{
    Task EnqueueEventAsync(
        string eventType,
        object payload,
        string? operatorId = null,
        CancellationToken ct = default);
}

public sealed record RegistrationPaymentRequestDto(
    string? RequestKey,
    string PatientId,
    string AppointmentId,
    decimal TotalAmount,
    string PaymentChannel,
    string OrderDeptId,
    string AppointmentNo,
    string? OperatorId,
    long PaidAtMs);

public sealed record RegistrationPaymentResultDto(
    string PaymentId,
    string PaymentNo,
    string InvoiceOfdId,
    int Status);

public sealed record RegistrationRefundRequestDto(
    string PaymentId,
    decimal RefundAmount,
    string Reason,
    string? OperatorId,
    long RefundedAtMs);

public sealed record RegistrationRefundResultDto(
    string RefundId,
    string RefundNo,
    int PaymentStatus);

public interface IFinanceSettlementContract
{
    Task<RegistrationPaymentResultDto> CreateRegistrationPaymentAsync(
        RegistrationPaymentRequestDto request,
        CancellationToken ct = default);

    Task<RegistrationPaymentResultDto?> GetRegistrationPaymentAsync(
        string paymentId,
        CancellationToken ct = default);

    Task<RegistrationRefundResultDto> CreateRegistrationRefundAsync(
        RegistrationRefundRequestDto request,
        CancellationToken ct = default);
}


