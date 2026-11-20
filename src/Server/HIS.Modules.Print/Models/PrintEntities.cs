using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Print.Models;

public class PrtTemplate : BaseEntity
{
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Category { get; set; } = "BARCODE"; // BARCODE, SLIP, REPORT, LABEL, RECEIPT
    public string PageKey { get; set; } = "medtech";
    public decimal PaperWidthMm { get; set; } = 60m;
    public decimal PaperHeightMm { get; set; } = 35m;
    public string Orientation { get; set; } = "PORTRAIT"; // PORTRAIT, LANDSCAPE
    public decimal MarginTopMm { get; set; } = 2m;
    public decimal MarginRightMm { get; set; } = 2m;
    public decimal MarginBottomMm { get; set; } = 2m;
    public decimal MarginLeftMm { get; set; } = 2m;
    public string LayoutJson { get; set; } = "[]";
    public short IsPublished { get; set; } = 1;
    public short IsActive { get; set; } = 1;
    public string? Remark { get; set; }
}

public class PrtTriggerBinding : BaseEntity
{
    public string TemplateId { get; set; } = string.Empty;
    public string PageKey { get; set; } = "medtech";
    public string TriggerSlot { get; set; } = "TOP_TOOLBAR"; // TOP_TOOLBAR, LIST_ROW, DETAIL_FOOTER
    public string ButtonLabel { get; set; } = "打印";
    public string? ButtonIcon { get; set; } = "🖨️";
    public string? ConditionExpr { get; set; }
    public int SortOrder { get; set; } = 10;
    public short IsActive { get; set; } = 1;
}

public class PrtPrintLog : BaseEntity
{
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string PageKey { get; set; } = string.Empty;
    public string TriggerSlot { get; set; } = string.Empty;
    public string? BusinessKey { get; set; }
    public string? PatientName { get; set; }
    public string? RenderedContextJson { get; set; }
    public int Copies { get; set; } = 1;
    public string? PrinterName { get; set; }
    public string? PrintedBy { get; set; }
    public string? PrintedByName { get; set; }
    public long PrintedAtMs { get; set; }
}
