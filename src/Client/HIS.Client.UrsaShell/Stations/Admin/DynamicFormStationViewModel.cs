using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;



public sealed class DictCategoryOption
{
    public string DictType { get; set; } = string.Empty;
    public string DictName { get; set; } = string.Empty;
    public int ActiveItemCount { get; set; }
    public override string ToString() => string.IsNullOrWhiteSpace(DictType) ? "（不引用主数据字典）" : $"{DictName} ({DictType} · {ActiveItemCount}项)";
}

public sealed class DictItemSimpleOption
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
}

public sealed class BoTypeOption
{
    public string EntityType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int BoundSchemaCount { get; set; }
    public override string ToString() => $"{DisplayName} ({EntityType} · {ModuleName})";
}

public sealed class BoRecordOption
{
    public string Id { get; set; } = string.Empty;
    public string BusinessNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SubTitle { get; set; } = string.Empty;
    public override string ToString() => $"{BusinessNo} · {Title} ({SubTitle})";
}

public sealed class DynamicFormStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private string _searchKeyword = string.Empty;
    private DynamicFormTemplateModel? _selectedTemplate;
    private bool _subscribed;
    private int _templateLoadVersion;
    private CancellationTokenSource? _templateLoadCts;

    public DynamicFormStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        DesignTemplateCommand = new RelayCommand(param =>
        {
            if (param is DynamicFormTemplateModel template)
            {
                SelectedTemplate = template;
            }
        });

        DesignPrescriptionSchemaCommand = new AsyncRelayCommand(OpenPrescriptionSchemaDesignerAsync);
        AddPrescriptionFieldCommand = new AsyncRelayCommand(OpenAddPrescriptionFieldAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewTemplateCommand = new RelayCommand(CreateTemplate);
        AddFieldCommand = new RelayCommand(AddField);
        EditFieldCommand = new RelayCommand(EditField);
        MoveFieldUpCommand = new RelayCommand(param => MoveField(param, -1));
        MoveFieldDownCommand = new RelayCommand(param => MoveField(param, 1));
        RemoveFieldCommand = new RelayCommand(RemoveField);
        SaveFieldCommand = new AsyncRelayCommand(SaveFieldAsync, () => Workbench.EditingField != null);
        CancelFieldCommand = new RelayCommand(CancelFieldEdit, () => Workbench.EditingField != null);
        SaveTemplateCommand = new AsyncRelayCommand(SaveTemplateAsync, () => Workbench.EditingTemplate != null);

        RebuildFiltered();
        _ = LoadBoTypesAsync();
        _ = LoadMasterDictionariesAsync();
    }

    public WorkbenchModel Workbench { get; }
    public ObservableCollection<DynamicFormTemplateModel> FilteredTemplates { get; } = new();
    public Action? FocusSearchAction { get; set; }
    public bool CanConfigureDynamicForms => Workbench.CanConfigureDynamicForms;
    public string PermissionBlockedReason => CanConfigureDynamicForms
        ? string.Empty
        : "动态表单与处方字段配置仅系统管理员可用";

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetField(ref _searchKeyword, value))
            {
                RebuildFiltered();
            }
        }
    }

    public DynamicFormTemplateModel? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetField(ref _selectedTemplate, value) && value != null)
            {
                _ = OpenFormDesignerAsync(value);
                SyncSelectedBoTypeFromEditingTemplate();
            }
        }
    }

    public bool HasEditingTemplate => Workbench.EditingTemplate != null;
    public bool HasEditingField => Workbench.EditingField != null;
    public bool IsPreviewEmpty => Workbench.IsPreviewEmpty;
    public string PreviewFieldCountText => $"{Workbench.PreviewFieldCount} 个字段";
    public string SelectedTemplateMeta => Workbench.EditingTemplate == null
        ? "选择左侧模板开始配置"
        : $"{Workbench.EditingTemplate.TemplateCode} · {Workbench.EditingTemplate.DeptScope} · {Workbench.EditingTemplate.Status}";
    public string FieldEditorTitle => Workbench.EditingField == null
        ? "字段属性"
        : $"字段属性 · {Workbench.EditingField.FieldLabel}";

    public ICommand DesignTemplateCommand { get; }
    public ICommand DesignPrescriptionSchemaCommand { get; }
    public ICommand AddPrescriptionFieldCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand NewTemplateCommand { get; }
    public ICommand AddFieldCommand { get; }
    public ICommand EditFieldCommand { get; }
    public ICommand MoveFieldUpCommand { get; }
    public ICommand MoveFieldDownCommand { get; }
    public ICommand RemoveFieldCommand { get; }
    public ICommand SaveFieldCommand { get; }
    public ICommand CancelFieldCommand { get; }
    public ICommand SaveTemplateCommand { get; }

    public void Attach()
    {
        if (!_subscribed)
        {
            Workbench.DynamicFormTemplates.CollectionChanged += OnTemplatesChanged;
            Workbench.PropertyChanged += OnWorkbenchPropertyChanged;
            _subscribed = true;
        }

        RebuildFiltered();
        if (SelectedTemplate == null && FilteredTemplates.Count > 0)
        {
            SelectedTemplate = FilteredTemplates[0];
        }
    }

    public void Detach()
    {
        _templateLoadCts?.Cancel();
        _templateLoadCts?.Dispose();
        _templateLoadCts = null;
        if (_subscribed)
        {
            Workbench.DynamicFormTemplates.CollectionChanged -= OnTemplatesChanged;
            Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                AddFieldCommand.Execute(null);
                return true;
            case "Ctrl+F":
                FocusSearchAction?.Invoke();
                return true;
            case "Ctrl+S":
                if (Workbench.EditingField != null)
                {
                    SaveFieldCommand.Execute(null);
                    return true;
                }
                if (Workbench.EditingTemplate != null)
                {
                    SaveTemplateCommand.Execute(null);
                    return true;
                }
                return false;
            case "F5":
                RefreshCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private void OnTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildFiltered();

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.EditingTemplate)
            or nameof(WorkbenchModel.EditingField)
            or nameof(WorkbenchModel.PreviewFields)
            or nameof(WorkbenchModel.PreviewFieldCount)
            or nameof(WorkbenchModel.IsPreviewEmpty)
            or nameof(WorkbenchModel.IsPrescriptionSchemaDesigner))
        {
            NotifyDesignerState();
        }
    }

    private async Task RefreshAsync()
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        await Workbench.LoadDynamicFormTemplatesAsync();
        await Workbench.LoadPrescriptionColumnsAsync();
        RebuildFiltered();

        var target = Workbench.SelectedFormTemplate ?? FilteredTemplates.FirstOrDefault();
        if (target != null)
        {
            if (await OpenFormDesignerAsync(target))
            {
                SelectedTemplate = target;
            }
        }

        _shell?.ShowToast(
            $"已刷新 {FilteredTemplates.Count} 个表单模板与 {Workbench.PrescriptionColumns.Count} 个处方字段。",
            "Info");
    }

    private void CreateTemplate()
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        Workbench.BeginNewFormTemplate();
        Workbench.IsFormDesignerOpen = false;
        RebuildFiltered();
        SelectedTemplate = Workbench.EditingTemplate;
        _shell?.ShowToast("已创建草稿模板，请填写模板信息并保存。", "Info");
    }

    private void AddField()
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        if (Workbench.EditingTemplate == null)
        {
            _shell?.ShowToast("请先选择左侧表单模板。", "Warning");
            return;
        }

        Workbench.BeginAddField();
        Workbench.IsFieldEditorOpen = Workbench.EditingField != null;
        NotifyDesignerState();
    }

    private void EditField(object? parameter)
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        if (parameter is DynamicFormFieldModel field)
        {
            Workbench.BeginEditField(field);
            Workbench.IsFieldEditorOpen = Workbench.EditingField != null;
            NotifyDesignerState();
        }
    }

    private void MoveField(object? parameter, int offset)
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        if (parameter is DynamicFormFieldModel field)
        {
            Workbench.MoveField(field, offset);
            Workbench.BeginEditField(field);
            Workbench.IsFieldEditorOpen = true;
            NotifyDesignerState();
        }
    }

    private void RemoveField(object? parameter)
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        if (parameter is not DynamicFormFieldModel field)
        {
            return;
        }

        Workbench.BeginEditField(field);
        Workbench.RemoveEditingField();
        NotifyDesignerState();
        _shell?.ShowToast(
            Workbench.SystemNotice,
            field.IsProtectedField ? "Warning" : "Info");
    }

    private async Task SaveFieldAsync()
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        bool ok = await Workbench.SaveEditingFieldAndApplyAsync();
        NotifyDesignerState();
        _shell?.ShowToast(Workbench.SystemNotice, ok ? "Success" : "Warning");
    }

    private void CancelFieldEdit()
    {
        Workbench.CancelFieldEdit();
        NotifyDesignerState();
    }

    private async Task SaveTemplateAsync()
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        bool ok = await Workbench.SaveEditingTemplateAsync();
        if (!ok)
        {
            _shell?.ShowToast(Workbench.SystemNotice, "Warning");
            return;
        }

        RebuildFiltered();
        NotifyDesignerState();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private async Task OpenPrescriptionSchemaDesignerAsync()
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        await Workbench.BeginDesignPrescriptionItemFieldsAsync();
        Workbench.IsFormDesignerOpen = false;
        Workbench.IsFieldEditorOpen = false;
        RebuildFiltered();
        SelectedTemplate = Workbench.EditingTemplate;
        NotifyDesignerState();
    }

    private async Task OpenAddPrescriptionFieldAsync()
    {
        if (!EnsureConfigPermission())
        {
            return;
        }
        await Workbench.BeginAddPrescriptionItemFieldAsync();
        Workbench.IsFormDesignerOpen = false;
        Workbench.IsFieldEditorOpen = Workbench.EditingField != null;
        RebuildFiltered();
        SelectedTemplate = Workbench.EditingTemplate;
        NotifyDesignerState();
    }

    private async Task<bool> OpenFormDesignerAsync(DynamicFormTemplateModel template)
    {
        if (ReferenceEquals(Workbench.EditingTemplate, template))
        {
            NotifyDesignerState();
            return true;
        }

        if (!EnsureConfigPermission())
        {
            return false;
        }

        int version = ++_templateLoadVersion;
        _templateLoadCts?.Cancel();
        _templateLoadCts?.Dispose();
        _templateLoadCts = new CancellationTokenSource();
        CancellationToken token = _templateLoadCts.Token;
        bool opened;
        try
        {
            opened = await Workbench.BeginDesignFormAsync(template, token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        if (!opened || version != _templateLoadVersion || token.IsCancellationRequested)
        {
            return false;
        }
        Workbench.IsFormDesignerOpen = false;
        Workbench.IsFieldEditorOpen = false;
        NotifyDesignerState();
        return true;
    }

    private void RebuildFiltered()
    {
        FilteredTemplates.Clear();
        string keyword = (SearchKeyword ?? string.Empty).Trim();
        foreach (var template in Workbench.DynamicFormTemplates)
        {
            if (!string.IsNullOrEmpty(keyword)
                && !template.TemplateName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && !template.TemplateCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && !template.EntityType.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredTemplates.Add(template);
        }

        if (SelectedTemplate != null && !FilteredTemplates.Contains(SelectedTemplate))
        {
            _selectedTemplate = null;
            OnPropertyChanged(nameof(SelectedTemplate));
        }
    }

    private void NotifyDesignerState()
    {
        OnPropertyChanged(nameof(CanConfigureDynamicForms));
        OnPropertyChanged(nameof(PermissionBlockedReason));
        OnPropertyChanged(nameof(HasEditingTemplate));
        OnPropertyChanged(nameof(HasEditingField));
        OnPropertyChanged(nameof(IsPreviewEmpty));
        OnPropertyChanged(nameof(PreviewFieldCountText));
        OnPropertyChanged(nameof(SelectedTemplateMeta));
        OnPropertyChanged(nameof(FieldEditorTitle));
        (SaveFieldCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelFieldCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool EnsureConfigPermission()
    {
        if (CanConfigureDynamicForms)
        {
            return true;
        }
        _shell?.ShowToast(PermissionBlockedReason, "Warning");
        return false;
    }

    private static readonly JsonSerializerOptions BoJsonOpts = new() { PropertyNameCaseInsensitive = true };
    private BoTypeOption? _selectedBoType;
    private BoRecordOption? _selectedBoRecord;

    public ObservableCollection<BoTypeOption> BoTypes { get; } = new();
    public ObservableCollection<BoRecordOption> BoRecords { get; } = new();

    public BoTypeOption? SelectedBoType
    {
        get => _selectedBoType;
        set
        {
            if (SetField(ref _selectedBoType, value) && value != null)
            {
                if (Workbench.EditingTemplate != null && !string.Equals(Workbench.EditingTemplate.EntityType, value.EntityType, StringComparison.OrdinalIgnoreCase))
                {
                    Workbench.EditingTemplate.EntityType = value.EntityType;
                    OnPropertyChanged(nameof(SelectedTemplateMeta));
                }
                _ = LoadBoRecordsAsync(value.EntityType);
            }
        }
    }

    public BoRecordOption? SelectedBoRecord
    {
        get => _selectedBoRecord;
        set => SetField(ref _selectedBoRecord, value);
    }

    private async Task LoadBoTypesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<BoTypeOption>>>("/api/v4/config/bo/types", BoJsonOpts);
            BoTypes.Clear();
            if (res?.Data != null)
            {
                foreach (var item in res.Data) BoTypes.Add(item);
            }
            SyncSelectedBoTypeFromEditingTemplate();
        }
        catch { }
    }

    private async Task LoadBoRecordsAsync(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType)) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<BoRecordOption>>>($"/api/v4/config/bo/records/{Uri.EscapeDataString(entityType)}", BoJsonOpts);
            BoRecords.Clear();
            if (res?.Data != null)
            {
                foreach (var r in res.Data) BoRecords.Add(r);
            }
            SelectedBoRecord = BoRecords.FirstOrDefault();
        }
        catch { }
    }

    private void SyncSelectedBoTypeFromEditingTemplate()
    {
        string? et = Workbench.EditingTemplate?.EntityType;
        if (string.IsNullOrWhiteSpace(et) || BoTypes.Count == 0) return;
        var match = BoTypes.FirstOrDefault(b => string.Equals(b.EntityType, et, StringComparison.OrdinalIgnoreCase)) ?? BoTypes[0];
        if (!ReferenceEquals(_selectedBoType, match))
        {
            _selectedBoType = match;
            OnPropertyChanged(nameof(SelectedBoType));
            _ = LoadBoRecordsAsync(match.EntityType);
        }
    }

    private DictCategoryOption? _selectedFieldDictCategory;
    public ObservableCollection<DictCategoryOption> DictCategories { get; } = new();
    public ObservableCollection<string> StandardUnits { get; } = new();

    public DictCategoryOption? SelectedFieldDictCategory
    {
        get => _selectedFieldDictCategory;
        set
        {
            if (SetField(ref _selectedFieldDictCategory, value) && Workbench.EditingField != null)
            {
                string code = value?.DictType ?? string.Empty;
                Workbench.EditingField.DictCode = string.IsNullOrWhiteSpace(code) ? null : code;
                if (!string.IsNullOrWhiteSpace(code))
                {
                    Workbench.EditingField.UrsaControlType = "ComboBox";
                    _ = PopulatePreviewOptionsFromDictAsync(code);
                }
            }
        }
    }

    private async Task LoadMasterDictionariesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var catEnv = await http.GetFromJsonAsync<ClientApiEnvelope<List<DictCategoryOption>>>("/api/v4/config/dictionaries/categories", BoJsonOpts);
            DictCategories.Clear();
            DictCategories.Add(new DictCategoryOption { DictType = string.Empty, DictName = "不引用字典" });
            if (catEnv?.Data != null) foreach (var c in catEnv.Data) DictCategories.Add(c);

            var unitEnv = await http.GetFromJsonAsync<ClientApiEnvelope<List<DictItemSimpleOption>>>("/api/v4/config/dictionaries/units", BoJsonOpts);
            StandardUnits.Clear();
            StandardUnits.Add(string.Empty);
            if (unitEnv?.Data != null)
            {
                foreach (var u in unitEnv.Data)
                {
                    if (!string.IsNullOrWhiteSpace(u.ItemName) && !StandardUnits.Contains(u.ItemName))
                        StandardUnits.Add(u.ItemName);
                }
            }
        }
        catch { }
    }

    private async Task PopulatePreviewOptionsFromDictAsync(string dictType)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<DictItemSimpleOption>>>($"/api/v4/config/dictionaries/items/{Uri.EscapeDataString(dictType)}", BoJsonOpts);
            if (env?.Data != null && Workbench.EditingField != null && string.Equals(Workbench.EditingField.DictCode, dictType, StringComparison.OrdinalIgnoreCase))
            {
                Workbench.EditingField.OptionsText = string.Join(Environment.NewLine, env.Data.Select(x => x.ItemName));
            }
        }
        catch { }
    }
}
