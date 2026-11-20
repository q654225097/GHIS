using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.EmrDesigner;

public sealed class EmrDesignerStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private string _searchKeyword = string.Empty;
    private string _selectedScene = "ALL";
    private EmrVisualTemplateModel? _selectedTemplate;
    private EmrVisualSectionModel? _selectedSection;
    private EmrVisualFieldItemModel? _selectedField;
    private bool _isBusy;
    private string _statusNotice = string.Empty;

    public EmrDesignerStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(LoadTemplatesAsync);
        SelectSceneCommand = new RelayCommand(p =>
        {
            if (p is string scene)
            {
                SelectedScene = scene;
            }
        });
        SwitchModeCommand = new RelayCommand(p =>
        {
            if (p is string mode && SelectedTemplate != null)
            {
                SelectedTemplate.DesignMode = mode;
                SelectedTemplate.NotifyPrintMetrics();
                OnPropertyChanged(nameof(IsFillMode));
                OnPropertyChanged(nameof(IsPrintMode));
            }
        });
        SaveDesignCommand = new AsyncRelayCommand(SaveDesignAsync, () => CanSaveDesign);
        AddFieldCommand = new RelayCommand(ExecuteAddField);
        RemoveFieldCommand = new RelayCommand(p => ExecuteRemoveField(p as EmrVisualFieldItemModel));
        MoveFieldUpCommand = new RelayCommand(p => MoveField(p as EmrVisualFieldItemModel, -1));
        MoveFieldDownCommand = new RelayCommand(p => MoveField(p as EmrVisualFieldItemModel, 1));
        NewTemplateCommand = new RelayCommand(ExecuteNewTemplate);

        _ = LoadTemplatesAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<EmrVisualTemplateModel> AllTemplates { get; } = new();
    public ObservableCollection<EmrVisualTemplateModel> FilteredTemplates { get; } = new();
    public ObservableCollection<EmrVisualFieldItemModel> StandardCatalogElements { get; } = new();

    public Action? FocusSearchAction { get; set; }

    public bool OnShortcut(string keyGesture)
    {
        if (keyGesture == "F5")
        {
            _ = LoadTemplatesAsync();
            return true;
        }
        if (keyGesture == "Ctrl+S" && CanSaveDesign)
        {
            _ = SaveDesignAsync();
            return true;
        }
        return false;
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string StatusNotice
    {
        get => _statusNotice;
        set => SetField(ref _statusNotice, value);
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetField(ref _searchKeyword, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedScene
    {
        get => _selectedScene;
        set
        {
            if (SetField(ref _selectedScene, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(IsSceneAll));
                OnPropertyChanged(nameof(IsSceneIpd));
                OnPropertyChanged(nameof(IsSceneWard));
                OnPropertyChanged(nameof(IsSceneOpd));
            }
        }
    }

    public bool IsSceneAll => SelectedScene == "ALL";
    public bool IsSceneIpd => SelectedScene == "IPD";
    public bool IsSceneWard => SelectedScene == "WARD";
    public bool IsSceneOpd => SelectedScene == "OPD";

    public EmrVisualTemplateModel? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetField(ref _selectedTemplate, value))
            {
                SelectedSection = value?.Sections.FirstOrDefault();
                SelectedField = SelectedSection?.Fields.FirstOrDefault();
                value?.NotifyPrintMetrics();
                OnPropertyChanged(nameof(HasSelectedTemplate));
                OnPropertyChanged(nameof(IsFillMode));
                OnPropertyChanged(nameof(IsPrintMode));
                OnPropertyChanged(nameof(CanSaveDesign));
            }
        }
    }

    public bool HasSelectedTemplate => SelectedTemplate != null;
    public bool IsFillMode => SelectedTemplate?.IsFillMode ?? true;
    public bool IsPrintMode => SelectedTemplate?.IsPrintMode ?? false;
    public bool CanSaveDesign => SelectedTemplate != null && !SelectedTemplate.IsPrintExceeded && !IsBusy;

    public EmrVisualSectionModel? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetField(ref _selectedSection, value))
            {
                SelectedField = value?.Fields.FirstOrDefault();
                OnPropertyChanged(nameof(HasSelectedSection));
            }
        }
    }

    public bool HasSelectedSection => SelectedSection != null;

    public EmrVisualFieldItemModel? SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetField(ref _selectedField, value))
            {
                OnPropertyChanged(nameof(HasSelectedField));
            }
        }
    }

    public bool HasSelectedField => SelectedField != null;

    public ICommand RefreshCommand { get; }
    public ICommand SelectSceneCommand { get; }
    public ICommand SwitchModeCommand { get; }
    public ICommand SaveDesignCommand { get; }
    public ICommand AddFieldCommand { get; }
    public ICommand RemoveFieldCommand { get; }
    public ICommand MoveFieldUpCommand { get; }
    public ICommand MoveFieldDownCommand { get; }
    public ICommand NewTemplateCommand { get; }

    public async Task LoadTemplatesAsync()
    {
        IsBusy = true;
        StatusNotice = "正在加载系统文书模板定义...";
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<TemplateSummaryDto>>>("/api/v4/emr/templates");
            if (res?.Code == 200 && res.Data != null)
            {
                AllTemplates.Clear();
                foreach (var t in res.Data)
                {
                    var model = new EmrVisualTemplateModel
                    {
                        TemplateId = t.Id,
                        TemplateCode = t.TemplateCode,
                        TemplateName = t.TemplateName,
                        EmrScene = t.EmrScene,
                        DocTypeCode = t.DocTypeCode,
                        DeptName = t.DeptCategory ?? "全科",
                        TimeLimitHours = t.TimeLimitHours,
                        RequiredSignLevels = t.RequiredSignLevels,
                        BoundLayoutCode = t.LayoutName,
                        Version = t.TemplateVersion,
                        IsPublished = t.IsPublished == 1,
                        PageSizeCode = t.PageSizeCode ?? "A4"
                    };

                    AllTemplates.Add(model);
                }

                ApplyFilter();

                if (SelectedTemplate == null && FilteredTemplates.Count > 0)
                {
                    await LoadTemplateDetailAsync(FilteredTemplates[0]);
                }
            }
            StatusNotice = $"文书模板加载就绪 (共 {AllTemplates.Count} 个模板)";
        }
        catch (Exception ex)
        {
            StatusNotice = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadTemplateDetailAsync(EmrVisualTemplateModel template)
    {
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<TemplateDetailResponseDto>>($"/api/v4/emr/templates/{template.TemplateId}");
            if (res?.Code == 200 && res.Data != null)
            {
                template.Sections.Clear();
                if (res.Data.Sections != null)
                {
                    foreach (var s in res.Data.Sections)
                    {
                        var secModel = new EmrVisualSectionModel
                        {
                            SectionCode = s.SectionCode,
                            SectionTitle = s.SectionTitle,
                            RenderMode = s.RenderMode,
                            IsRequired = s.IsRequired == 1,
                            SortOrder = s.SortOrder,
                            EstimatedHeightMm = s.EstimatedHeightMm > 0 ? s.EstimatedHeightMm : 30.0m
                        };

                        if (s.LayoutControls != null && s.LayoutControls.Count > 0)
                        {
                            foreach (var c in s.LayoutControls)
                            {
                                secModel.Fields.Add(new EmrVisualFieldItemModel
                                {
                                    ElementCode = c.ElementCode,
                                    ElementName = c.ElementName,
                                    ControlType = c.ControlType,
                                    Row = c.Row,
                                    Col = c.Col,
                                    ColSpan = c.ColSpan,
                                    RowSpan = c.RowSpan,
                                    WidthPx = c.WidthPx > 0 ? c.WidthPx : 240,
                                    HeightPx = c.HeightPx > 0 ? c.HeightPx : 32,
                                    PrintHeightMm = c.PrintHeightMm > 0 ? c.PrintHeightMm : 8.0m,
                                    DefaultValue = c.DefaultValue,
                                    PlaceholderText = c.Placeholder ?? "请输入临床内容...",
                                    UnitText = c.UnitText,
                                    IsRequired = c.IsRequired
                                });
                            }
                        }
                        else if (s.Elements != null)
                        {
                            int r = 0, c = 0;
                            foreach (var el in s.Elements)
                            {
                                secModel.Fields.Add(new EmrVisualFieldItemModel
                                {
                                    ElementCode = el.ElementCode,
                                    ElementName = el.ElementName,
                                    NhcStdCode = el.NhcStdCode ?? "DE00.00.000",
                                    DataType = el.DataType,
                                    UrsaControlType = el.UrsaControlType,
                                    UnitText = el.UnitText,
                                    GenderExclusive = el.GenderExclusive,
                                    Row = r,
                                    Col = c,
                                    ColSpan = 1,
                                    PrintHeightMm = el.DataType == "TEXT" ? 25.0m : 8.0m,
                                    ControlType = el.DataType == "TEXT" ? "MULTI_TEXT" : "SINGLE_TEXT"
                                });
                                c++;
                                if (c >= 2) { c = 0; r++; }
                            }
                        }

                        template.Sections.Add(secModel);
                    }
                }

                if (res.Data.Layout != null)
                {
                    template.PageSizeCode = res.Data.Layout.PageSizeCode;
                    template.PageWidthMm = res.Data.Layout.PageWidthMm;
                    template.PageHeightMm = res.Data.Layout.PageHeightMm;
                    template.MarginTopMm = res.Data.Layout.MarginTopMm;
                    template.MarginBottomMm = res.Data.Layout.MarginBottomMm;
                    template.MarginLeftMm = res.Data.Layout.MarginLeftMm;
                    template.MarginRightMm = res.Data.Layout.MarginRightMm;
                }

                SelectedTemplate = template;
                template.NotifyPrintMetrics();
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"加载模板详情失败: {ex.Message}", "Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveDesignAsync()
    {
        if (SelectedTemplate == null) return;

        // 强约束校验：打印模式下内容不可超过纸张大小
        if (SelectedTemplate.IsPrintMode && SelectedTemplate.IsPrintExceeded)
        {
            _shell?.ShowToast($"[打印模式尺寸超限] 内容总高度 ({SelectedTemplate.TotalPrintContentHeightMm:0.#}mm) 超过纸张有效区域 ({SelectedTemplate.AvailablePrintHeightMm:0.#}mm)！禁止保存。", "Warning");
            return;
        }

        IsBusy = true;
        StatusNotice = "正在提交保存文书结构化设计...";
        try
        {
            var sectionsDto = new List<SaveSectionItemDto>();
            for (int i = 0; i < SelectedTemplate.Sections.Count; i++)
            {
                var s = SelectedTemplate.Sections[i];
                var controlsDto = s.Fields.Select(f => new SaveControlItemDto(
                    f.ElementCode,
                    f.ElementName,
                    f.ControlType,
                    f.Row,
                    f.Col,
                    f.ColSpan,
                    f.RowSpan,
                    f.WidthPx,
                    f.HeightPx,
                    f.PrintHeightMm,
                    f.DefaultValue,
                    f.PlaceholderText,
                    f.UnitText,
                    null,
                    f.IsRequired
                )).ToList();

                sectionsDto.Add(new SaveSectionItemDto(
                    s.SectionCode,
                    s.SectionTitle,
                    s.RenderMode,
                    s.Fields.Select(f => f.ElementCode).ToList(),
                    (short)(s.IsRequired ? 1 : 0),
                    s.VisibilityExpr,
                    s.SortOrder > 0 ? s.SortOrder : i + 1,
                    controlsDto,
                    s.EstimatedHeightMm
                ));
            }

            var request = new SaveDesignRequestDto(
                SelectedTemplate.TemplateCode,
                SelectedTemplate.TemplateName,
                SelectedTemplate.EmrScene,
                SelectedTemplate.DocTypeCode,
                SelectedTemplate.DeptName,
                null,
                null,
                SelectedTemplate.TimeLimitHours,
                SelectedTemplate.RequiredSignLevels,
                null,
                (short)(SelectedTemplate.IsPublished ? 1 : 0),
                sectionsDto,
                SelectedTemplate.DesignMode,
                SelectedTemplate.PageSizeCode,
                SelectedTemplate.PageWidthMm,
                SelectedTemplate.PageHeightMm,
                SelectedTemplate.MarginTopMm,
                SelectedTemplate.MarginBottomMm,
                SelectedTemplate.MarginLeftMm,
                SelectedTemplate.MarginRightMm
            );

            using var http = AuthenticatedHttpClientFactory.Create();
            var response = await http.PostAsJsonAsync("/api/v4/emr/templates/design", request);
            var res = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<object>>();

            if (res?.Code == 200)
            {
                _shell?.ShowToast($"文书模板 [{SelectedTemplate.TemplateName}] 保存成功！模式: {(SelectedTemplate.IsPrintMode ? "打印模式" : "填写模式")}", "Success");
                StatusNotice = $"保存成功 (版本更新于 {DateTime.Now:HH:mm:ss})";
            }
            else
            {
                _shell?.ShowToast(res?.Message ?? "保存失败", "Warning");
                StatusNotice = res?.Message ?? "保存失败";
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"保存异常: {ex.Message}", "Error");
            StatusNotice = $"保存异常: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteAddField()
    {
        if (SelectedSection == null) return;
        int nextRow = SelectedSection.Fields.Count > 0 ? SelectedSection.Fields.Max(f => f.Row) + 1 : 0;
        var newField = new EmrVisualFieldItemModel
        {
            ElementCode = $"CUSTOM_FIELD_{SelectedSection.Fields.Count + 1}",
            ElementName = $"新增临床字段 {SelectedSection.Fields.Count + 1}",
            ControlType = "SINGLE_TEXT",
            Row = nextRow,
            Col = 0,
            ColSpan = 2,
            WidthPx = 300,
            HeightPx = 32,
            PrintHeightMm = 8.0m,
            PlaceholderText = "请输入内容...",
            IsRequired = true
        };
        SelectedSection.Fields.Add(newField);
        SelectedField = newField;
        SelectedTemplate?.NotifyPrintMetrics();
        OnPropertyChanged(nameof(CanSaveDesign));
    }

    private void ExecuteRemoveField(EmrVisualFieldItemModel? field)
    {
        if (field == null || SelectedSection == null) return;
        SelectedSection.Fields.Remove(field);
        SelectedField = SelectedSection.Fields.FirstOrDefault();
        SelectedTemplate?.NotifyPrintMetrics();
        OnPropertyChanged(nameof(CanSaveDesign));
    }

    private void MoveField(EmrVisualFieldItemModel? field, int direction)
    {
        if (field == null || SelectedSection == null) return;
        int idx = SelectedSection.Fields.IndexOf(field);
        int targetIdx = idx + direction;
        if (targetIdx >= 0 && targetIdx < SelectedSection.Fields.Count)
        {
            SelectedSection.Fields.Move(idx, targetIdx);
            for (int i = 0; i < SelectedSection.Fields.Count; i++)
            {
                SelectedSection.Fields[i].Row = i;
            }
            SelectedTemplate?.NotifyPrintMetrics();
        }
    }

    private void ExecuteNewTemplate()
    {
        var newTpl = new EmrVisualTemplateModel
        {
            TemplateId = $"TMPL_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}",
            TemplateCode = $"TMPL_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}".ToUpperInvariant(),
            TemplateName = "新建住院结构化病历模板",
            EmrScene = SelectedScene == "ALL" ? "IPD" : SelectedScene,
            DocTypeCode = "IPD_PROGRESS",
            DeptName = "心血管内科",
            TimeLimitHours = 24,
            RequiredSignLevels = 2,
            PageSizeCode = "A4",
            DesignMode = "FILL"
        };

        var sec = new EmrVisualSectionModel
        {
            SectionCode = "SEC_MAIN_01",
            SectionTitle = "基本临床记录章节",
            RenderMode = "GRID_FORM",
            IsRequired = true,
            SortOrder = 1
        };
        sec.Fields.Add(new EmrVisualFieldItemModel
        {
            ElementCode = "CHIEF_COMPLAINT",
            ElementName = "主诉与症状分析",
            ControlType = "MULTI_TEXT",
            Row = 0,
            Col = 0,
            ColSpan = 2,
            HeightPx = 60,
            PrintHeightMm = 20.0m
        });
        newTpl.Sections.Add(sec);

        AllTemplates.Insert(0, newTpl);
        ApplyFilter();
        SelectedTemplate = newTpl;
    }

    private void ApplyFilter()
    {
        FilteredTemplates.Clear();
        var query = AllTemplates.AsEnumerable();

        if (SelectedScene != "ALL")
        {
            query = query.Where(t => t.EmrScene.Equals(SelectedScene, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var kw = SearchKeyword.Trim();
            query = query.Where(t => t.TemplateName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                  || t.TemplateCode.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var t in query)
        {
            FilteredTemplates.Add(t);
        }
    }

    // DTO records
    public sealed record TemplateSummaryDto(
        string Id,
        string TemplateCode,
        int TemplateVersion,
        string TemplateName,
        string EmrScene,
        string DocTypeCode,
        string? DeptCategory,
        string? TargetDeptId,
        int TimeLimitHours,
        int RequiredSignLevels,
        string? OfdLayoutId,
        string LayoutName,
        string PageSizeCode,
        int SectionCount,
        short IsPublished
    );

    public sealed record TemplateDetailResponseDto(
        object Template,
        LayoutDto? Layout,
        List<SectionDetailDto>? Sections
    );

    public sealed record LayoutDto(
        string PageSizeCode,
        decimal PageWidthMm,
        decimal PageHeightMm,
        decimal MarginTopMm,
        decimal MarginBottomMm,
        decimal MarginLeftMm,
        decimal MarginRightMm
    );

    public sealed record SectionDetailDto(
        string SectionId,
        string SectionCode,
        string SectionTitle,
        string RenderMode,
        short IsRequired,
        string VisibilityExpr,
        int SortOrder,
        List<ElementDto>? Elements,
        List<ControlDto>? LayoutControls,
        decimal EstimatedHeightMm
    );

    public sealed record ElementDto(
        string ElementCode,
        string ElementName,
        string? NhcStdCode,
        string DataType,
        string UrsaControlType,
        string? UnitText,
        string GenderExclusive
    );

    public sealed record ControlDto(
        string ElementCode,
        string ElementName,
        string ControlType,
        int Row,
        int Col,
        int ColSpan,
        int RowSpan,
        int WidthPx,
        int HeightPx,
        decimal PrintHeightMm,
        string? DefaultValue,
        string? Placeholder,
        string? UnitText,
        string? DictCode,
        bool IsRequired
    );

    public sealed record SaveControlItemDto(
        string ElementCode,
        string ElementName,
        string ControlType,
        int Row,
        int Col,
        int ColSpan,
        int RowSpan,
        int WidthPx,
        int HeightPx,
        decimal PrintHeightMm,
        string? DefaultValue,
        string? Placeholder,
        string? UnitText,
        string? DictCode,
        bool IsRequired
    );

    public sealed record SaveSectionItemDto(
        string SectionCode,
        string SectionTitle,
        string RenderMode,
        List<string> ElementCodes,
        short IsRequired,
        string VisibilityExpr,
        int SortOrder,
        List<SaveControlItemDto> LayoutControls,
        decimal EstimatedHeightMm
    );

    public sealed record SaveDesignRequestDto(
        string TemplateCode,
        string TemplateName,
        string EmrScene,
        string DocTypeCode,
        string? DeptCategory,
        string? TargetDeptId,
        string? DiseaseIcdPrefix,
        int TimeLimitHours,
        int RequiredSignLevels,
        string? OfdLayoutId,
        short IsPublished,
        List<SaveSectionItemDto> Sections,
        string DesignMode,
        string PageSizeCode,
        decimal PageWidthMm,
        decimal PageHeightMm,
        decimal MarginTopMm,
        decimal MarginBottomMm,
        decimal MarginLeftMm,
        decimal MarginRightMm
    );
}
