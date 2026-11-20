using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.PrintDesigner;

public sealed class PrintDesignerViewModel : ClientModelBase
{
    private readonly ShellViewModel? _shell;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private int _selectedTabIndex;

    // Template editor state
    private PrintTemplateDto? _selectedTemplate;
    private string _editTemplateId = string.Empty;
    private string _editTemplateCode = string.Empty;
    private string _editTemplateName = "新建条码/单据模板";
    private string _editCategory = "BARCODE";
    private PrintPageDataSourceDto? _selectedPageDataSource;
    private decimal _paperWidthMm = 60m;
    private decimal _paperHeightMm = 35m;
    private string _orientation = "PORTRAIT";
    private decimal _marginMm = 2m;
    private string _editRemark = string.Empty;
    private double _zoomScale = 4.5;

    // Selected canvas element
    private PrintElementModel? _selectedElement;

    // Trigger binding editor state
    private PrintPageDataSourceDto? _triggerTargetPage;
    private PrintSlotMetaDto? _triggerTargetSlot;
    private string _triggerButtonLabel = "打印单据";
    private string _triggerButtonIcon = "🖨️";
    private int _triggerSortOrder = 10;

    public PrintDesignerViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new RelayCommand(() => _ = LoadAllAsync());
        NewTemplateCommand = new RelayCommand(CreateNewTemplate);
        SaveTemplateCommand = new RelayCommand(() => _ = SaveTemplateAsync());
        DeleteTemplateCommand = new RelayCommand(() => _ = DeleteSelectedTemplateAsync());
        PreviewPrintCommand = new RelayCommand(OpenDesignerPreview);

        AddTextElementCommand = new RelayCommand(() => AddElement("TEXT", "静态标题文本", string.Empty, 45, 7));
        AddFieldElementCommand = new RelayCommand(p => AddFieldFromMeta(p as PrintFieldMetaDto, false));
        AddBarcodeFromFieldCommand = new RelayCommand(p => AddFieldFromMeta(p as PrintFieldMetaDto, true));
        AddBarcodeElementCommand = new RelayCommand(() => AddElement("BARCODE", "{{medtech.requestNo}}", "medtech.requestNo", 50, 13));
        AddQrElementCommand = new RelayCommand(() => AddElement("QRCODE", "{{patient.medicalRecordNo}}", "patient.medicalRecordNo", 16, 16));
        AddLineElementCommand = new RelayCommand(() => AddElement("LINE", string.Empty, string.Empty, Math.Max(20, (double)_paperWidthMm - 8), 1));
        AddRectElementCommand = new RelayCommand(() => AddElement("RECT", string.Empty, string.Empty, 30, 12));
        DeleteElementCommand = new RelayCommand(DeleteSelectedElement);
        ApplyPaperPresetCommand = new RelayCommand(p => ApplyPaperPreset(p as string));

        AddTriggerCommand = new RelayCommand(() => _ = AddTriggerBindingAsync());
        DeleteTriggerCommand = new RelayCommand(p => _ = DeleteTriggerBindingAsync(p as PrintTriggerItemDto));

        _ = LoadAllAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<PrintPageDataSourceDto> PageDataSources { get; } = new();
    public ObservableCollection<PrintFieldMetaDto> CurrentPageFields { get; } = new();
    public ObservableCollection<PrintSlotMetaDto> CurrentPageSlots { get; } = new();
    public ObservableCollection<PrintTemplateDto> Templates { get; } = new();
    public ObservableCollection<PrintElementModel> CanvasElements { get; } = new();
    public ObservableCollection<PrintTriggerItemDto> TemplateTriggers { get; } = new();
    public ObservableCollection<PrintLogDto> PrintLogs { get; } = new();

    public IReadOnlyList<string> CategoryOptions { get; } = new[] { "BARCODE", "SLIP", "REPORT", "LABEL", "RECEIPT" };
    public IReadOnlyList<string> OrientationOptions { get; } = new[] { "PORTRAIT", "LANDSCAPE" };
    public IReadOnlyList<string> TextAlignOptions { get; } = new[] { "LEFT", "CENTER", "RIGHT" };
    public IReadOnlyList<string> ElementTypeOptions { get; } = new[] { "TEXT", "FIELD", "BARCODE", "QRCODE", "LINE", "RECT" };

    public ICommand RefreshCommand { get; }
    public ICommand NewTemplateCommand { get; }
    public ICommand SaveTemplateCommand { get; }
    public ICommand DeleteTemplateCommand { get; }
    public ICommand PreviewPrintCommand { get; }
    public ICommand AddTextElementCommand { get; }
    public ICommand AddFieldElementCommand { get; }
    public ICommand AddBarcodeFromFieldCommand { get; }
    public ICommand AddBarcodeElementCommand { get; }
    public ICommand AddQrElementCommand { get; }
    public ICommand AddLineElementCommand { get; }
    public ICommand AddRectElementCommand { get; }
    public ICommand DeleteElementCommand { get; }
    public ICommand ApplyPaperPresetCommand { get; }
    public ICommand AddTriggerCommand { get; }
    public ICommand DeleteTriggerCommand { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetField(ref _selectedTabIndex, value); }

    public PrintTemplateDto? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetField(ref _selectedTemplate, value) && value != null)
            {
                LoadTemplateIntoEditor(value);
                _ = LoadTriggersForTemplateAsync(value.Id);
            }
        }
    }

    public string EditTemplateCode { get => _editTemplateCode; set => SetField(ref _editTemplateCode, value); }
    public string EditTemplateName { get => _editTemplateName; set => SetField(ref _editTemplateName, value); }
    public string EditCategory { get => _editCategory; set => SetField(ref _editCategory, value); }
    public string EditRemark { get => _editRemark; set => SetField(ref _editRemark, value); }

    public PrintPageDataSourceDto? SelectedPageDataSource
    {
        get => _selectedPageDataSource;
        set
        {
            if (SetField(ref _selectedPageDataSource, value))
            {
                RefreshFieldsForPage(value);
                TriggerTargetPage ??= value;
            }
        }
    }

    public decimal PaperWidthMm
    {
        get => _paperWidthMm;
        set
        {
            if (SetField(ref _paperWidthMm, Math.Clamp(value, 15m, 500m)))
            {
                AutoAdjustZoom();
                OnPropertyChanged(nameof(CanvasWidthPx));
                OnPropertyChanged(nameof(CanvasHeightPx));
                OnPropertyChanged(nameof(PaperSizeDisplay));
            }
        }
    }

    public decimal PaperHeightMm
    {
        get => _paperHeightMm;
        set
        {
            if (SetField(ref _paperHeightMm, Math.Clamp(value, 15m, 1000m)))
            {
                AutoAdjustZoom();
                OnPropertyChanged(nameof(CanvasWidthPx));
                OnPropertyChanged(nameof(CanvasHeightPx));
                OnPropertyChanged(nameof(PaperSizeDisplay));
            }
        }
    }

    public string Orientation
    {
        get => _orientation;
        set
        {
            if (SetField(ref _orientation, value))
            {
                OnPropertyChanged(nameof(CanvasWidthPx));
                OnPropertyChanged(nameof(CanvasHeightPx));
                OnPropertyChanged(nameof(PaperSizeDisplay));
            }
        }
    }

    public decimal MarginMm { get => _marginMm; set => SetField(ref _marginMm, Math.Clamp(value, 0m, 30m)); }

    public double ZoomScale
    {
        get => _zoomScale;
        set
        {
            if (SetField(ref _zoomScale, Math.Clamp(value, 1.5, 8.0)))
            {
                foreach (var el in CanvasElements) el.ScalePxPerMm = _zoomScale;
                OnPropertyChanged(nameof(CanvasWidthPx));
                OnPropertyChanged(nameof(CanvasHeightPx));
            }
        }
    }

    public double EffectiveWidthMm => (double)(Orientation == "LANDSCAPE" ? PaperHeightMm : PaperWidthMm);
    public double EffectiveHeightMm => (double)(Orientation == "LANDSCAPE" ? PaperWidthMm : PaperHeightMm);
    public double CanvasWidthPx => EffectiveWidthMm * ZoomScale;
    public double CanvasHeightPx => EffectiveHeightMm * ZoomScale;
    public string PaperSizeDisplay => $"当前纸张：{EffectiveWidthMm:0.#} mm × {EffectiveHeightMm:0.#} mm（缩放 {ZoomScale:0.0} px/mm）";

    public PrintElementModel? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (_selectedElement != null) _selectedElement.IsSelected = false;
            if (SetField(ref _selectedElement, value))
            {
                if (_selectedElement != null) _selectedElement.IsSelected = true;
                OnPropertyChanged(nameof(HasSelectedElement));
            }
        }
    }

    public bool HasSelectedElement => _selectedElement != null;

    public PrintPageDataSourceDto? TriggerTargetPage
    {
        get => _triggerTargetPage;
        set
        {
            if (SetField(ref _triggerTargetPage, value) && value != null)
            {
                CurrentPageSlots.Clear();
                foreach (var s in value.SupportedSlots) CurrentPageSlots.Add(s);
                TriggerTargetSlot = CurrentPageSlots.FirstOrDefault();
            }
        }
    }

    public PrintSlotMetaDto? TriggerTargetSlot { get => _triggerTargetSlot; set => SetField(ref _triggerTargetSlot, value); }
    public string TriggerButtonLabel { get => _triggerButtonLabel; set => SetField(ref _triggerButtonLabel, value); }
    public string TriggerButtonIcon { get => _triggerButtonIcon; set => SetField(ref _triggerButtonIcon, value); }
    public int TriggerSortOrder { get => _triggerSortOrder; set => SetField(ref _triggerSortOrder, value); }

    private void AutoAdjustZoom()
    {
        double w = EffectiveWidthMm;
        double target = w <= 70 ? 5.2 : w <= 110 ? 4.0 : w <= 160 ? 2.8 : 2.2;
        ZoomScale = target;
    }

    private void RefreshFieldsForPage(PrintPageDataSourceDto? ds)
    {
        CurrentPageFields.Clear();
        if (ds == null) return;
        foreach (var f in ds.AvailableFields) CurrentPageFields.Add(f);
    }

    public async Task LoadAllAsync()
    {
        IsBusy = true;
        StatusMessage = "正在加载打印模板、页面数据源与触发位配置...";
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var dsRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<PrintPageDataSourceDto>>>("/api/v4/print/datasources", JsonOpts);
            PageDataSources.Clear();
            if (dsRes?.Data != null)
                foreach (var ds in dsRes.Data) PageDataSources.Add(ds);

            var tplRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<PrintTemplateDto>>>("/api/v4/print/templates", JsonOpts);
            string? prevId = _selectedTemplate?.Id;
            Templates.Clear();
            if (tplRes?.Data != null)
                foreach (var t in tplRes.Data) Templates.Add(t);

            var logRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<PrintLogDto>>>("/api/v4/print/jobs/logs?limit=100", JsonOpts);
            PrintLogs.Clear();
            if (logRes?.Data != null)
                foreach (var l in logRes.Data) PrintLogs.Add(l);

            SelectedTemplate = (prevId != null ? Templates.FirstOrDefault(t => t.Id == prevId) : null) ?? Templates.FirstOrDefault();
            if (SelectedTemplate == null && PageDataSources.Count > 0)
            {
                SelectedPageDataSource = PageDataSources[0];
            }
            StatusMessage = $"已加载 {Templates.Count} 个打印模板、{PageDataSources.Count} 个业务页面数据源";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadTemplateIntoEditor(PrintTemplateDto tpl)
    {
        _editTemplateId = tpl.Id;
        EditTemplateCode = tpl.TemplateCode;
        EditTemplateName = tpl.TemplateName;
        EditCategory = tpl.Category;
        EditRemark = tpl.Remark ?? string.Empty;
        Orientation = string.IsNullOrWhiteSpace(tpl.Orientation) ? "PORTRAIT" : tpl.Orientation;
        MarginMm = tpl.MarginTopMm;
        PaperWidthMm = tpl.PaperWidthMm;
        PaperHeightMm = tpl.PaperHeightMm;
        SelectedPageDataSource = PageDataSources.FirstOrDefault(p => p.PageKey == tpl.PageKey) ?? PageDataSources.FirstOrDefault();
        TriggerTargetPage = SelectedPageDataSource;

        CanvasElements.Clear();
        foreach (var el in PrintEngineService.ParseLayout(tpl.LayoutJson))
        {
            el.ScalePxPerMm = ZoomScale;
            CanvasElements.Add(el);
        }
        SelectedElement = CanvasElements.FirstOrDefault();
    }

    private async Task LoadTriggersForTemplateAsync(string templateId)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<PrintTriggerItemDto>>>(
                $"/api/v4/print/triggers?templateId={Uri.EscapeDataString(templateId)}", JsonOpts);
            TemplateTriggers.Clear();
            if (res?.Data != null)
                foreach (var item in res.Data) TemplateTriggers.Add(item);
        }
        catch { }
    }

    private void CreateNewTemplate()
    {
        _selectedTemplate = null;
        OnPropertyChanged(nameof(SelectedTemplate));
        _editTemplateId = string.Empty;
        EditTemplateCode = "TPL-" + DateTime.Now.ToString("yyMMddHHmm");
        EditTemplateName = "新自定义打印模板";
        EditCategory = "BARCODE";
        EditRemark = string.Empty;
        Orientation = "PORTRAIT";
        PaperWidthMm = 60m;
        PaperHeightMm = 35m;
        MarginMm = 2m;
        SelectedPageDataSource ??= PageDataSources.FirstOrDefault();
        CanvasElements.Clear();
        TemplateTriggers.Clear();
        SelectedElement = null;
        _shell?.ShowToast("已初始化空白模板，可自定义纸张宽高并拖拽添加元素。", "Info");
    }

    private void ApplyPaperPreset(string? preset)
    {
        switch (preset)
        {
            case "60x35": PaperWidthMm = 60; PaperHeightMm = 35; Orientation = "PORTRAIT"; break;
            case "90x30": PaperWidthMm = 90; PaperHeightMm = 30; Orientation = "PORTRAIT"; break;
            case "80x150": PaperWidthMm = 80; PaperHeightMm = 150; Orientation = "PORTRAIT"; break;
            case "148x210": PaperWidthMm = 148; PaperHeightMm = 210; Orientation = "PORTRAIT"; break;
            case "210x297": PaperWidthMm = 210; PaperHeightMm = 297; Orientation = "PORTRAIT"; break;
        }
    }

    public void AddFieldFromMeta(PrintFieldMetaDto? field, bool asBarcode, double? dropXMm = null, double? dropYMm = null)
    {
        if (field == null) return;
        if (asBarcode)
        {
            AddElement("BARCODE", field.Token, field.Path, Math.Min(54, EffectiveWidthMm - 4), 13, dropXMm, dropYMm);
        }
        else
        {
            AddElement("FIELD", $"{field.Label}:{field.Token}", field.Path, Math.Min(52, EffectiveWidthMm - 4), 6, dropXMm, dropYMm);
        }
    }

    public void AddElement(string type, string staticText, string bindingPath, double widthMm, double heightMm, double? xMm = null, double? yMm = null)
    {
        double nextY = yMm ?? Math.Min(EffectiveHeightMm - heightMm - 1, 2 + CanvasElements.Count * 6.5);
        double nextX = xMm ?? 2.0;
        var el = new PrintElementModel
        {
            Type = type,
            XMm = Math.Max(0, nextX),
            YMm = Math.Max(0, nextY),
            WidthMm = Math.Min(widthMm, EffectiveWidthMm),
            HeightMm = Math.Min(heightMm, EffectiveHeightMm),
            FontSize = type == "BARCODE" ? 8 : 9,
            StaticText = staticText,
            BindingPath = bindingPath,
            ShowBarcodeText = true,
            ScalePxPerMm = ZoomScale
        };
        CanvasElements.Add(el);
        SelectedElement = el;
    }

    private void DeleteSelectedElement()
    {
        if (SelectedElement == null) return;
        CanvasElements.Remove(SelectedElement);
        SelectedElement = CanvasElements.FirstOrDefault();
    }

    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTemplateName))
        {
            _shell?.ShowToast("请输入模板名称。", "Warning");
            return;
        }
        IsBusy = true;
        try
        {
            string layoutJson = JsonSerializer.Serialize(CanvasElements.ToList());
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                Id = string.IsNullOrWhiteSpace(_editTemplateId) ? null : _editTemplateId,
                TemplateCode = EditTemplateCode,
                TemplateName = EditTemplateName.Trim(),
                Category = EditCategory,
                PageKey = SelectedPageDataSource?.PageKey ?? "medtech",
                PaperWidthMm = PaperWidthMm,
                PaperHeightMm = PaperHeightMm,
                Orientation = Orientation,
                MarginTopMm = MarginMm,
                MarginRightMm = MarginMm,
                MarginBottomMm = MarginMm,
                MarginLeftMm = MarginMm,
                LayoutJson = layoutJson,
                IsPublished = true,
                IsActive = true,
                Remark = EditRemark
            };
            var resp = await http.PostAsJsonAsync("/api/v4/print/templates", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<PrintTemplateDto>>(JsonOpts);
            if (env?.Code == 0 && env.Data != null)
            {
                _editTemplateId = env.Data.Id;
                _shell?.ShowToast($"模板 [{env.Data.TemplateName}] 已保存并生效。", "Success");
                PrintEngineService.NotifyTriggersChanged();
                await LoadAllAsync();
                SelectedTemplate = Templates.FirstOrDefault(t => t.Id == env.Data.Id);
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "保存模板失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"保存异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task DeleteSelectedTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(_editTemplateId)) return;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.DeleteAsync($"/api/v4/print/templates/{_editTemplateId}");
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if (env?.Code == 0)
            {
                _shell?.ShowToast("模板及关联打印按钮已停用。", "Warning");
                PrintEngineService.NotifyTriggersChanged();
                await LoadAllAsync();
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "删除失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"删除异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task AddTriggerBindingAsync()
    {
        if (string.IsNullOrWhiteSpace(_editTemplateId))
        {
            _shell?.ShowToast("请先保存当前打印模板，再配置触发位置。", "Warning");
            return;
        }
        if (TriggerTargetPage == null || TriggerTargetSlot == null || string.IsNullOrWhiteSpace(TriggerButtonLabel))
        {
            _shell?.ShowToast("请选择目标页面、触发位置并填写按钮名称。", "Warning");
            return;
        }
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                Id = (string?)null,
                TemplateId = _editTemplateId,
                PageKey = TriggerTargetPage.PageKey,
                TriggerSlot = TriggerTargetSlot.SlotKey,
                ButtonLabel = TriggerButtonLabel.Trim(),
                ButtonIcon = string.IsNullOrWhiteSpace(TriggerButtonIcon) ? "🖨️" : TriggerButtonIcon.Trim(),
                ConditionExpr = (string?)null,
                SortOrder = TriggerSortOrder,
                IsActive = true
            };
            var resp = await http.PostAsJsonAsync("/api/v4/print/triggers", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if (env?.Code == 0)
            {
                _shell?.ShowToast($"已挂载到 [{TriggerTargetPage.PageName} · {TriggerTargetSlot.SlotName}]，业务页面已实时生效。", "Success");
                PrintEngineService.NotifyTriggersChanged();
                await LoadTriggersForTemplateAsync(_editTemplateId);
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "配置触发位失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"配置触发位异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task DeleteTriggerBindingAsync(PrintTriggerItemDto? item)
    {
        if (item == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.DeleteAsync($"/api/v4/print/triggers/{item.Id}");
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if (env?.Code == 0)
            {
                _shell?.ShowToast("已移除打印触发位。", "Info");
                PrintEngineService.NotifyTriggersChanged();
                await LoadTriggersForTemplateAsync(_editTemplateId);
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"移除失败：{ex.Message}", "Error");
        }
    }

    private void OpenDesignerPreview()
    {
        var sampleCtx = new Dictionary<string, string>();
        if (SelectedPageDataSource != null)
        {
            foreach (var f in SelectedPageDataSource.AvailableFields)
                sampleCtx[f.Path] = f.SampleValue;
        }
        var tpl = new PrintTemplateDto
        {
            Id = _editTemplateId,
            TemplateCode = string.IsNullOrWhiteSpace(EditTemplateCode) ? "PREVIEW" : EditTemplateCode,
            TemplateName = EditTemplateName,
            Category = EditCategory,
            PageKey = SelectedPageDataSource?.PageKey ?? "medtech",
            PaperWidthMm = PaperWidthMm,
            PaperHeightMm = PaperHeightMm,
            Orientation = Orientation,
            MarginTopMm = MarginMm,
            MarginRightMm = MarginMm,
            MarginBottomMm = MarginMm,
            MarginLeftMm = MarginMm,
            LayoutJson = JsonSerializer.Serialize(CanvasElements.ToList())
        };
        PrintEngineService.OpenPreviewWindow(tpl, "DESIGNER_PREVIEW", sampleCtx);
    }
}
