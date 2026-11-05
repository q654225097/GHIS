using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrDataElement : BaseEntity
{
    public string ElementCode { get; set; } = string.Empty;
    public string? NhcStdCode { get; set; }
    public string ElementName { get; set; } = string.Empty;
    public string DataType { get; set; } = "STRING";
    public string UrsaControlType { get; set; } = "u:TextBox";
    public string? DictCode { get; set; }
    public string? UnitText { get; set; }
    public string? MacroSource { get; set; }
    public string GenderExclusive { get; set; } = "ALL"; // ALL / MALE_ONLY / FEMALE_ONLY
    public string? ValidationRuleJson { get; set; }
    public string? ControlKey { get; set; }
    public short IsIndexed { get; set; } = 1;
    public short IsActive { get; set; } = 1;
}

public class EmrOfdLayoutSchema : BaseEntity
{
    public string LayoutCode { get; set; } = string.Empty;
    public string LayoutName { get; set; } = string.Empty;
    public string PageSizeCode { get; set; } = "A4";
    public decimal PageWidthMm { get; set; } = 210.0m;
    public decimal PageHeightMm { get; set; } = 297.0m;
    public decimal MarginTopMm { get; set; } = 20.0m;
    public decimal MarginBottomMm { get; set; } = 20.0m;
    public decimal MarginLeftMm { get; set; } = 20.0m;
    public decimal MarginRightMm { get; set; } = 20.0m;
    public short IsContinuousFlow { get; set; } = 0;
    public string? HeaderLayoutJson { get; set; }
    public string? FooterLayoutJson { get; set; }
    public string? SignSlotsJson { get; set; }
    public string? WatermarkPolicy { get; set; }
    public short IsActive { get; set; } = 1;
}

public class EmrDocTemplate : BaseEntity
{
    public string TemplateCode { get; set; } = string.Empty;
    public int TemplateVersion { get; set; } = 1;
    public string TemplateName { get; set; } = string.Empty;
    public string EmrScene { get; set; } = "OPD"; // OPD / ER / IPD / WARD / CONSENT
    public string DocTypeCode { get; set; } = string.Empty;
    public string? DeptCategory { get; set; }
    public string? TargetDeptId { get; set; }
    public string? DiseaseIcdPrefix { get; set; }
    public int TimeLimitHours { get; set; } = 24;
    public int RequiredSignLevels { get; set; } = 1; // 1 to 3
    public string? OfdLayoutId { get; set; }
    public short IsPublished { get; set; } = 1;
}

public class EmrSectionSchema : BaseEntity
{
    public string TemplateId { get; set; } = string.Empty;
    public string SectionCode { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public string RenderMode { get; set; } = "GRID_FORM"; // GRID_FORM / INLINE_PARAGRAPH / TABLE_GRID / VECTOR_CANVAS
    public string? ContentTemplateJson { get; set; }
    public string? SectionControlKey { get; set; }
    public short IsRequired { get; set; } = 1;
    public string? VisibilityExpr { get; set; }
    public int SortOrder { get; set; } = 0;
}

public class EmrDocument : BaseEntity
{
    public string DocNo { get; set; } = string.Empty;
    public string? RequestKey { get; set; }
    public string? CampusId { get; set; }
    public string EmrScene { get; set; } = "OPD";
    public string VisitOrEncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public string? WardId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public int TemplateVersion { get; set; } = 1;
    public string DocTypeCode { get; set; } = string.Empty;
    public string DocTitle { get; set; } = string.Empty;
    public long ClinicalTimeMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string? StructuredAstJson { get; set; }
    public string? PlainTextSummary { get; set; }
    public string? TrackChangesJson { get; set; }
    public int Status { get; set; } = 0; // 0=Draft, 1=ResidentSigned, 2=AttendingSigned, 3=ChiefLocked, 4=Archived
    public string AuthorId { get; set; } = string.Empty;
    public long? ResidentSignedAtMs { get; set; }
    public string? AttendingDoctorId { get; set; }
    public long? AttendingSignedAtMs { get; set; }
    public string? ChiefDoctorId { get; set; }
    public long? ChiefSignedAtMs { get; set; }
    public string? PatientSignSvg { get; set; }
    public string? LatestOfdArchiveId { get; set; }
}

public class EmrDocumentElement : BaseEntity
{
    public string DocumentId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string EmrScene { get; set; } = "OPD";
    public string DocTypeCode { get; set; } = string.Empty;
    public string SectionCode { get; set; } = string.Empty;
    public string ElementCode { get; set; } = string.Empty;
    public string? NhcStdCode { get; set; }
    public string? ValString { get; set; }
    public long? ValInt { get; set; }
    public decimal? ValDecimal { get; set; }
    public long? ValTimeMs { get; set; }
}

public class EmrOfdArchive : BaseEntity
{
    public string OfdNo { get; set; } = string.Empty;
    public string ArchiveScope { get; set; } = "SINGLE_DOC"; // SINGLE_DOC / FULL_ENCOUNTER_BOOK / RX_SHEET / MEDTECH_REPORT
    public string VisitOrEncounterId { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string OfdVersionStandard { get; set; } = "GB/T 33190-2016";
    public int PageCount { get; set; } = 1;
    public string FileStorageKey { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string? Sm3Digest { get; set; }
    public string? CaSignSerialNo { get; set; }
    public string? TsaTimestampToken { get; set; }
    public short HasEmbeddedSemanticXml { get; set; } = 1;
    public int ArchiveStatus { get; set; } = 0; // 0=Effective, 1=ArchivedSeal, 2=Void
    public long GeneratedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

