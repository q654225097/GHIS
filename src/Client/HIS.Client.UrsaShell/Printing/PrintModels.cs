using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.Printing;

public interface IPrintContextProvider
{
    Dictionary<string, string> BuildPrintContext(object? rowContext = null);
}

public sealed class PrintElementModel : ClientModelBase
{
    private string _id = "el_" + Guid.NewGuid().ToString("N")[..6];
    private string _type = "TEXT"; // TEXT, FIELD, BARCODE, QRCODE, LINE, RECT
    private double _xMm = 5;
    private double _yMm = 5;
    private double _widthMm = 40;
    private double _heightMm = 8;
    private double _fontSize = 10;
    private bool _isBold;
    private string _textAlign = "LEFT"; // LEFT, CENTER, RIGHT
    private string _staticText = "文本内容";
    private string _bindingPath = string.Empty;
    private string _barcodeFormat = "CODE128";
    private bool _showBarcodeText = true;
    private bool _isSelected;
    private double _scalePxPerMm = 4.0;

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetField(ref _id, value); }

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value))
            {
                OnPropertyChanged(nameof(IsBarcode));
                OnPropertyChanged(nameof(IsQrCode));
                OnPropertyChanged(nameof(IsLine));
                OnPropertyChanged(nameof(IsRect));
                OnPropertyChanged(nameof(IsTextOrField));
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    [JsonPropertyName("xMm")]
    public double XMm
    {
        get => _xMm;
        set { if (SetField(ref _xMm, Math.Round(value, 1))) OnPropertyChanged(nameof(LeftPx)); }
    }

    [JsonPropertyName("yMm")]
    public double YMm
    {
        get => _yMm;
        set { if (SetField(ref _yMm, Math.Round(value, 1))) OnPropertyChanged(nameof(TopPx)); }
    }

    [JsonPropertyName("widthMm")]
    public double WidthMm
    {
        get => _widthMm;
        set { if (SetField(ref _widthMm, Math.Max(2, Math.Round(value, 1)))) OnPropertyChanged(nameof(WidthPx)); }
    }

    [JsonPropertyName("heightMm")]
    public double HeightMm
    {
        get => _heightMm;
        set { if (SetField(ref _heightMm, Math.Max(0.5, Math.Round(value, 1)))) OnPropertyChanged(nameof(HeightPx)); }
    }

    [JsonPropertyName("fontSize")]
    public double FontSize
    {
        get => _fontSize;
        set { if (SetField(ref _fontSize, Math.Clamp(value, 6, 48))) OnPropertyChanged(nameof(ScaledFontSize)); }
    }

    [JsonPropertyName("isBold")]
    public bool IsBold { get => _isBold; set => SetField(ref _isBold, value); }

    [JsonPropertyName("textAlign")]
    public string TextAlign { get => _textAlign; set => SetField(ref _textAlign, value); }

    [JsonPropertyName("staticText")]
    public string StaticText
    {
        get => _staticText;
        set { if (SetField(ref _staticText, value)) OnPropertyChanged(nameof(DisplayLabel)); }
    }

    [JsonPropertyName("bindingPath")]
    public string BindingPath
    {
        get => _bindingPath;
        set { if (SetField(ref _bindingPath, value)) OnPropertyChanged(nameof(DisplayLabel)); }
    }

    [JsonPropertyName("barcodeFormat")]
    public string BarcodeFormat { get => _barcodeFormat; set => SetField(ref _barcodeFormat, value); }

    [JsonPropertyName("showBarcodeText")]
    public bool ShowBarcodeText { get => _showBarcodeText; set => SetField(ref _showBarcodeText, value); }

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetField(ref _isSelected, value)) OnPropertyChanged(nameof(BorderColorHex)); }
    }

    [JsonIgnore]
    public double ScalePxPerMm
    {
        get => _scalePxPerMm;
        set
        {
            if (SetField(ref _scalePxPerMm, value))
            {
                OnPropertyChanged(nameof(LeftPx));
                OnPropertyChanged(nameof(TopPx));
                OnPropertyChanged(nameof(WidthPx));
                OnPropertyChanged(nameof(HeightPx));
                OnPropertyChanged(nameof(ScaledFontSize));
            }
        }
    }

    [JsonIgnore] public double LeftPx => XMm * _scalePxPerMm;
    [JsonIgnore] public double TopPx => YMm * _scalePxPerMm;
    [JsonIgnore] public double WidthPx => Math.Max(8, WidthMm * _scalePxPerMm);
    [JsonIgnore] public double HeightPx => Math.Max(4, HeightMm * _scalePxPerMm);
    [JsonIgnore] public double ScaledFontSize => Math.Max(8, FontSize * (_scalePxPerMm / 3.78));
    [JsonIgnore] public string BorderColorHex => IsSelected ? "#165DFF" : "#94A3B8";
    [JsonIgnore] public bool IsBarcode => string.Equals(Type, "BARCODE", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsQrCode => string.Equals(Type, "QRCODE", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsLine => string.Equals(Type, "LINE", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsRect => string.Equals(Type, "RECT", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsTextOrField => !IsBarcode && !IsQrCode && !IsLine && !IsRect;

    [JsonIgnore]
    public string DisplayLabel => Type switch
    {
        "BARCODE" => $"[条码] {(string.IsNullOrWhiteSpace(StaticText) ? "{{" + BindingPath + "}}" : StaticText)}",
        "QRCODE" => $"[二维码] {(string.IsNullOrWhiteSpace(StaticText) ? "{{" + BindingPath + "}}" : StaticText)}",
        "LINE" => "────────",
        "RECT" => string.Empty,
        _ => string.IsNullOrWhiteSpace(StaticText) ? (string.IsNullOrWhiteSpace(BindingPath) ? "文本" : "{{" + BindingPath + "}}") : StaticText
    };
}

public sealed class PrintTemplateDto
{
    public string Id { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Category { get; set; } = "BARCODE";
    public string PageKey { get; set; } = "medtech";
    public decimal PaperWidthMm { get; set; } = 60m;
    public decimal PaperHeightMm { get; set; } = 35m;
    public string Orientation { get; set; } = "PORTRAIT";
    public decimal MarginTopMm { get; set; } = 2m;
    public decimal MarginRightMm { get; set; } = 2m;
    public decimal MarginBottomMm { get; set; } = 2m;
    public decimal MarginLeftMm { get; set; } = 2m;
    public string LayoutJson { get; set; } = "[]";
    public short IsPublished { get; set; } = 1;
    public short IsActive { get; set; } = 1;
    public string? Remark { get; set; }

    public string SizeDisplay => $"{PaperWidthMm:0.#}×{PaperHeightMm:0.#}mm";
    public override string ToString() => $"{TemplateName} ({SizeDisplay})";
}

public sealed class PrintTriggerItemDto
{
    public string Id { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string PageKey { get; set; } = string.Empty;
    public string TriggerSlot { get; set; } = "TOP_TOOLBAR";
    public string ButtonLabel { get; set; } = "打印";
    public string? ButtonIcon { get; set; }
    public string? ConditionExpr { get; set; }
    public int SortOrder { get; set; } = 10;
    public short IsActive { get; set; } = 1;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Category { get; set; } = "BARCODE";
    public decimal PaperWidthMm { get; set; } = 60m;
    public decimal PaperHeightMm { get; set; } = 35m;
    public string Orientation { get; set; } = "PORTRAIT";
    public decimal MarginTopMm { get; set; } = 2m;
    public decimal MarginRightMm { get; set; } = 2m;
    public decimal MarginBottomMm { get; set; } = 2m;
    public decimal MarginLeftMm { get; set; } = 2m;
    public string LayoutJson { get; set; } = "[]";

    public string SlotDisplay => TriggerSlot switch
    {
        "TOP_TOOLBAR" => "页面顶部工具栏",
        "LIST_ROW" => "列表选中行操作区",
        "DETAIL_FOOTER" => "详情面板底部",
        _ => TriggerSlot
    };
}

public sealed class PrintFieldMetaDto
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string SampleValue { get; set; } = string.Empty;
    public string Token => "{{" + Path + "}}";
    public override string ToString() => $"[{Group}] {Label} ({Path})";
}

public sealed class PrintSlotMetaDto
{
    public string SlotKey { get; set; } = string.Empty;
    public string SlotName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public override string ToString() => $"{SlotName} ({SlotKey})";
}

public sealed class PrintPageDataSourceDto
{
    public string PageKey { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public List<PrintSlotMetaDto> SupportedSlots { get; set; } = new();
    public List<PrintFieldMetaDto> AvailableFields { get; set; } = new();
    public override string ToString() => $"{PageName} ({PageKey})";
}

public sealed class PrintLogDto
{
    public string Id { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string PageKey { get; set; } = string.Empty;
    public string TriggerSlot { get; set; } = string.Empty;
    public string? BusinessKey { get; set; }
    public string? PatientName { get; set; }
    public int Copies { get; set; }
    public string? PrinterName { get; set; }
    public string? PrintedByName { get; set; }
    public long PrintedAtMs { get; set; }

    public string TimeText => PrintedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(PrintedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "-";
}
