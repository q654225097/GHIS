using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.MedTechReport;

public sealed class ReportSummaryDto
{
    public int PendingWriteCount { get; set; }
    public int PendingReviewCount { get; set; }
    public int DeviceAutoPendingCount { get; set; }
    public int SignedCount { get; set; }
    public int CriticalCount { get; set; }
}

public sealed class ReportFieldSchemaDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FieldType { get; set; } = "TEXT"; // TEXT, MULTILINE, SELECT, NUMBER
    public bool Required { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    public string Options { get; set; } = string.Empty; // pipe-separated for SELECT
}

public sealed class ReportDynamicFieldValue : ClientModelBase
{
    private string _value = string.Empty;

    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FieldType { get; set; } = "TEXT";
    public bool Required { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    public List<string> OptionList { get; set; } = new();

    public string Value { get => _value; set => SetField(ref _value, value); }

    public string DisplayLabel => Required ? $"{Label} *" : Label;
    public bool IsMultiline => string.Equals(FieldType, "MULTILINE", StringComparison.OrdinalIgnoreCase);
    public bool IsSelect => string.Equals(FieldType, "SELECT", StringComparison.OrdinalIgnoreCase) && OptionList.Count > 0;
    public bool IsSingleLine => !IsMultiline && !IsSelect;
}

public sealed class MtcReportTemplateRow
{
    public string Id { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string MedtechDeptCode { get; set; } = "RAD";
    public string MedtechDeptName { get; set; } = "放射科";
    public string ModalityType { get; set; } = "CT/DR";
    public string EditorSchemaJson { get; set; } = "[]";
    public decimal PrintPaperWidthMm { get; set; } = 210m;
    public decimal PrintPaperHeightMm { get; set; } = 297m;
    public string PrintHeaderTitle { get; set; } = "GHIS 示范医院 医技诊断报告单";
    public string? PrintSubTitle { get; set; }
    public string? PrintFooterNote { get; set; }
    public string? PrintTemplateId { get; set; }
    public short IsDefault { get; set; }
    public string? Remark { get; set; }

    public string PaperDisplay => $"{PrintPaperWidthMm:0.#}×{PrintPaperHeightMm:0.#}mm";
    public override string ToString() => $"[{MedtechDeptName}] {TemplateName}";
}

public sealed class MtcDiagnosticReportRow
{
    public string Id { get; set; } = string.Empty;
    public string ReportNo { get; set; } = string.Empty;
    public string? ServiceRequestId { get; set; }
    public string? RequestNo { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; }
    public string? MedicalRecordNo { get; set; }
    public string? OrderDeptName { get; set; }
    public string MedtechDeptCode { get; set; } = "RAD";
    public string MedtechDeptName { get; set; } = "放射科";
    public string? ExamItemCode { get; set; }
    public string ExamItemName { get; set; } = string.Empty;
    public string? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public string ResultSource { get; set; } = "MANUAL";
    public string? DeviceName { get; set; }
    public string Status { get; set; } = "PENDING_WRITE";
    public string ContentJson { get; set; } = "{}";
    public string? FindingsSummary { get; set; }
    public string? DiagnosisImpression { get; set; }
    public short IsCriticalValue { get; set; }
    public string? WriterName { get; set; }
    public string? ReviewerName { get; set; }
    public string? RejectReason { get; set; }
    public string? SignatureHash { get; set; }

    public string StatusDisplay => Status switch
    {
        "PENDING_WRITE" => "待书写",
        "DRAFT" => "草稿中",
        "PENDING_REVIEW" => ResultSource == "DEVICE_AUTO" ? "待审签 (机器直出)" : "待审签",
        "REJECTED" => "已退回修改",
        "SIGNED" => "已双签发布",
        _ => Status
    };
    public string SourceBadge => ResultSource == "DEVICE_AUTO" ? $"仪器直传·{DeviceName}" : "人工书写";
    public bool CanEdit => Status != "SIGNED";
    public bool CanApprove => Status == "PENDING_REVIEW";
}

public sealed class MedTechReportViewModel : ClientModelBase
{
    private readonly ShellViewModel? _shell;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private int _selectedTabIndex;

    // KPI
    private int _pendingWriteCount;
    private int _pendingReviewCount;
    private int _deviceAutoPendingCount;
    private int _signedCount;
    private int _criticalCount;

    // Queue filters
    private string _filterDeptCode = "ALL";
    private string _filterStatus = "ALL";
    private string _searchKeyword = string.Empty;

    // Active report editing
    private MtcDiagnosticReportRow? _selectedReport;
    private MtcReportTemplateRow? _activeEditorTemplate;
    private bool _editIsCriticalValue;
    private string _rejectReasonInput = string.Empty;

    // Quick register / device ingest inputs
    private string _newPatName = string.Empty;
    private string _newPatGender = "男";
    private int _newPatAge = 42;
    private string _newExamItem = string.Empty;
    private string _newDeptCode = "RAD";
    private string _newDeviceName = "罗氏 Cobas c702 全自动生化分析仪";

    // Admin Template Editor (Tab 2)
    private MtcReportTemplateRow? _selectedAdminTemplate;
    private string _tplEditId = string.Empty;
    private string _tplEditCode = string.Empty;
    private string _tplEditName = string.Empty;
    private string _tplEditDeptCode = "RAD";
    private string _tplEditDeptName = "放射科";
    private string _tplEditModality = "CT/DR";
    private decimal _tplPrintW = 210m;
    private decimal _tplPrintH = 297m;
    private string _tplPrintHeader = "GHIS 示范医院 医技诊断报告单";
    private string _tplPrintSubTitle = string.Empty;
    private string _tplPrintFooter = "本报告仅供临床医师参考。";
    private bool _tplIsDefault = true;

    // Add field inputs for Tab 2
    private string _addFieldKey = string.Empty;
    private string _addFieldLabel = string.Empty;
    private string _addFieldType = "TEXT";
    private string _addFieldDefault = string.Empty;
    private string _addFieldOptions = string.Empty;
    private bool _addFieldRequired = true;

    public MedTechReportViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new RelayCommand(() => _ = LoadAllAsync());
        SearchQueueCommand = new RelayCommand(() => _ = LoadQueueAsync());
        SyncRequestsCommand = new RelayCommand(() => _ = SyncFromRequestsAsync());
        CreateManualTaskCommand = new RelayCommand(() => _ = CreateManualTaskAsync());
        DeviceIngestCommand = new RelayCommand(() => _ = IngestDeviceResultAsync());

        FillDefaultTemplateCommand = new RelayCommand(FillDefaultsFromTemplate);
        SaveDraftCommand = new RelayCommand(() => _ = SaveDraftAsync());
        SubmitReviewCommand = new RelayCommand(() => _ = SubmitReviewAsync());
        ApproveSignCommand = new RelayCommand(() => _ = ApproveSignAsync());
        RejectCommand = new RelayCommand(() => _ = RejectReportAsync());
        PrintReportCommand = new RelayCommand(() => _ = PrintSelectedReportAsync());

        NewTemplateCommand = new RelayCommand(ClearTemplateEditor);
        AddSchemaFieldCommand = new RelayCommand(AddSchemaField);
        RemoveSchemaFieldCommand = new RelayCommand(p => RemoveSchemaField(p as ReportFieldSchemaDto));
        SaveTemplateCommand = new RelayCommand(() => _ = SaveTemplateAsync());
        DisableTemplateCommand = new RelayCommand(() => _ = DisableTemplateAsync());

        _ = LoadAllAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<MtcDiagnosticReportRow> ReportQueue { get; } = new();
    public ObservableCollection<MtcReportTemplateRow> AllTemplates { get; } = new();
    public ObservableCollection<MtcReportTemplateRow> DeptTemplates { get; } = new();
    public ObservableCollection<ReportDynamicFieldValue> DynamicFields { get; } = new();
    public ObservableCollection<ReportFieldSchemaDto> EditingSchemaFields { get; } = new();

    public IReadOnlyList<string> DeptFilterOptions { get; } = new[] { "ALL", "RAD", "US", "LAB", "ECG", "ENDO" };
    public IReadOnlyList<string> DeptCodeOptions { get; } = new[] { "RAD", "US", "LAB", "ECG", "ENDO" };
    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "ALL", "WRITE_GROUP", "PENDING_REVIEW", "SIGNED" };
    public IReadOnlyList<string> GenderOptions { get; } = new[] { "男", "女" };
    public IReadOnlyList<string> FieldTypeOptions { get; } = new[] { "TEXT", "MULTILINE", "SELECT", "NUMBER" };

    public ICommand RefreshCommand { get; }
    public ICommand SearchQueueCommand { get; }
    public ICommand SyncRequestsCommand { get; }
    public ICommand CreateManualTaskCommand { get; }
    public ICommand DeviceIngestCommand { get; }
    public ICommand FillDefaultTemplateCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand SubmitReviewCommand { get; }
    public ICommand ApproveSignCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand PrintReportCommand { get; }
    public ICommand NewTemplateCommand { get; }
    public ICommand AddSchemaFieldCommand { get; }
    public ICommand RemoveSchemaFieldCommand { get; }
    public ICommand SaveTemplateCommand { get; }
    public ICommand DisableTemplateCommand { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetField(ref _selectedTabIndex, value); }

    public int PendingWriteCount { get => _pendingWriteCount; set => SetField(ref _pendingWriteCount, value); }
    public int PendingReviewCount { get => _pendingReviewCount; set => SetField(ref _pendingReviewCount, value); }
    public int DeviceAutoPendingCount { get => _deviceAutoPendingCount; set => SetField(ref _deviceAutoPendingCount, value); }
    public int SignedCount { get => _signedCount; set => SetField(ref _signedCount, value); }
    public int CriticalCount { get => _criticalCount; set => SetField(ref _criticalCount, value); }

    public string FilterDeptCode
    {
        get => _filterDeptCode;
        set { if (SetField(ref _filterDeptCode, value)) _ = LoadQueueAsync(); }
    }
    public string FilterStatus
    {
        get => _filterStatus;
        set { if (SetField(ref _filterStatus, value)) _ = LoadQueueAsync(); }
    }
    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value); }

    public MtcDiagnosticReportRow? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetField(ref _selectedReport, value))
            {
                OnPropertyChanged(nameof(HasSelectedReport));
                OnPropertyChanged(nameof(CanEditSelectedReport));
                OnPropertyChanged(nameof(CanApproveSelectedReport));
                BindReportToDynamicEditor(value);
            }
        }
    }
    public bool HasSelectedReport => _selectedReport != null;
    public bool CanEditSelectedReport => _selectedReport?.CanEdit ?? false;
    public bool CanApproveSelectedReport => _selectedReport?.CanApprove ?? false;

    public MtcReportTemplateRow? ActiveEditorTemplate
    {
        get => _activeEditorTemplate;
        set
        {
            if (SetField(ref _activeEditorTemplate, value) && value != null && _selectedReport != null)
            {
                RebuildDynamicFieldsFromTemplate(value, _selectedReport.ContentJson);
            }
        }
    }

    public bool EditIsCriticalValue { get => _editIsCriticalValue; set => SetField(ref _editIsCriticalValue, value); }
    public string RejectReasonInput { get => _rejectReasonInput; set => SetField(ref _rejectReasonInput, value); }

    public string NewPatName { get => _newPatName; set => SetField(ref _newPatName, value); }
    public string NewPatGender { get => _newPatGender; set => SetField(ref _newPatGender, value); }
    public int NewPatAge { get => _newPatAge; set => SetField(ref _newPatAge, value); }
    public string NewExamItem { get => _newExamItem; set => SetField(ref _newExamItem, value); }
    public string NewDeptCode { get => _newDeptCode; set => SetField(ref _newDeptCode, value); }
    public string NewDeviceName { get => _newDeviceName; set => SetField(ref _newDeviceName, value); }

    // Admin template editor properties
    public MtcReportTemplateRow? SelectedAdminTemplate
    {
        get => _selectedAdminTemplate;
        set
        {
            if (SetField(ref _selectedAdminTemplate, value) && value != null)
                LoadAdminTemplate(value);
        }
    }
    public string TplEditCode { get => _tplEditCode; set => SetField(ref _tplEditCode, value); }
    public string TplEditName { get => _tplEditName; set => SetField(ref _tplEditName, value); }
    public string TplEditDeptCode { get => _tplEditDeptCode; set => SetField(ref _tplEditDeptCode, value); }
    public string TplEditDeptName { get => _tplEditDeptName; set => SetField(ref _tplEditDeptName, value); }
    public string TplEditModality { get => _tplEditModality; set => SetField(ref _tplEditModality, value); }
    public decimal TplPrintW { get => _tplPrintW; set => SetField(ref _tplPrintW, Math.Clamp(value, 40m, 400m)); }
    public decimal TplPrintH { get => _tplPrintH; set => SetField(ref _tplPrintH, Math.Clamp(value, 40m, 500m)); }
    public string TplPrintHeader { get => _tplPrintHeader; set => SetField(ref _tplPrintHeader, value); }
    public string TplPrintSubTitle { get => _tplPrintSubTitle; set => SetField(ref _tplPrintSubTitle, value); }
    public string TplPrintFooter { get => _tplPrintFooter; set => SetField(ref _tplPrintFooter, value); }
    public bool TplIsDefault { get => _tplIsDefault; set => SetField(ref _tplIsDefault, value); }

    public string AddFieldKey { get => _addFieldKey; set => SetField(ref _addFieldKey, value); }
    public string AddFieldLabel { get => _addFieldLabel; set => SetField(ref _addFieldLabel, value); }
    public string AddFieldType { get => _addFieldType; set => SetField(ref _addFieldType, value); }
    public string AddFieldDefault { get => _addFieldDefault; set => SetField(ref _addFieldDefault, value); }
    public string AddFieldOptions { get => _addFieldOptions; set => SetField(ref _addFieldOptions, value); }
    public bool AddFieldRequired { get => _addFieldRequired; set => SetField(ref _addFieldRequired, value); }

    private static string DeptNameByCode(string code) => code.ToUpperInvariant() switch
    {
        "RAD" => "放射科",
        "US" => "超声科",
        "LAB" => "检验科",
        "ECG" => "心电图室",
        "ENDO" => "内镜中心",
        _ => code
    };

    public async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var tplRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<MtcReportTemplateRow>>>("/api/v4/medtech-reports/templates", JsonOpts);
            string? prevAdminId = _selectedAdminTemplate?.Id;
            AllTemplates.Clear();
            if (tplRes?.Data != null) foreach (var t in tplRes.Data) AllTemplates.Add(t);
            SelectedAdminTemplate = (prevAdminId != null ? AllTemplates.FirstOrDefault(t => t.Id == prevAdminId) : null) ?? AllTemplates.FirstOrDefault();

            await LoadSummaryAsync();
            await LoadQueueAsync();
            StatusMessage = $"已加载 {ReportQueue.Count} 份医技报告，{AllTemplates.Count} 套分科室编辑与打印模板";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadSummaryAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        string url = "/api/v4/medtech-reports/summary" + (FilterDeptCode == "ALL" ? "" : "?deptCode=" + FilterDeptCode);
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<ReportSummaryDto>>(url, JsonOpts);
        if (res?.Data != null)
        {
            PendingWriteCount = res.Data.PendingWriteCount;
            PendingReviewCount = res.Data.PendingReviewCount;
            DeviceAutoPendingCount = res.Data.DeviceAutoPendingCount;
            SignedCount = res.Data.SignedCount;
            CriticalCount = res.Data.CriticalCount;
        }
    }

    private async Task LoadQueueAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var qs = new List<string>();
        if (FilterDeptCode != "ALL") qs.Add("deptCode=" + Uri.EscapeDataString(FilterDeptCode));
        if (FilterStatus != "ALL") qs.Add("status=" + Uri.EscapeDataString(FilterStatus));
        if (!string.IsNullOrWhiteSpace(SearchKeyword)) qs.Add("keyword=" + Uri.EscapeDataString(SearchKeyword.Trim()));
        string url = "/api/v4/medtech-reports/queue" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<MtcDiagnosticReportRow>>>(url, JsonOpts);
        string? prevId = _selectedReport?.Id;
        ReportQueue.Clear();
        if (res?.Data != null) foreach (var r in res.Data) ReportQueue.Add(r);
        SelectedReport = (prevId != null ? ReportQueue.FirstOrDefault(r => r.Id == prevId) : null) ?? ReportQueue.FirstOrDefault();
    }

    private void BindReportToDynamicEditor(MtcDiagnosticReportRow? rpt)
    {
        DynamicFields.Clear();
        DeptTemplates.Clear();
        RejectReasonInput = string.Empty;
        if (rpt == null) return;

        EditIsCriticalValue = rpt.IsCriticalValue == 1;
        var matchingDeptTpls = AllTemplates.Where(t => string.Equals(t.MedtechDeptCode, rpt.MedtechDeptCode, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var t in matchingDeptTpls) DeptTemplates.Add(t);
        if (DeptTemplates.Count == 0)
            foreach (var t in AllTemplates) DeptTemplates.Add(t);

        var tpl = AllTemplates.FirstOrDefault(t => t.Id == rpt.TemplateId) ?? DeptTemplates.FirstOrDefault();
        _activeEditorTemplate = tpl;
        OnPropertyChanged(nameof(ActiveEditorTemplate));
        if (tpl != null)
            RebuildDynamicFieldsFromTemplate(tpl, rpt.ContentJson);
    }

    private void RebuildDynamicFieldsFromTemplate(MtcReportTemplateRow tpl, string? contentJson)
    {
        DynamicFields.Clear();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(contentJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(contentJson, JsonOpts);
                if (parsed != null)
                    foreach (var kv in parsed) values[kv.Key] = kv.Value ?? string.Empty;
            }
            catch { }
        }

        List<ReportFieldSchemaDto> schema = new();
        try
        {
            schema = JsonSerializer.Deserialize<List<ReportFieldSchemaDto>>(tpl.EditorSchemaJson, JsonOpts) ?? new();
        }
        catch { }

        foreach (var f in schema)
        {
            var opts = string.IsNullOrWhiteSpace(f.Options)
                ? new List<string>()
                : f.Options.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            string initialVal = values.TryGetValue(f.Key, out var existingVal) ? existingVal : string.Empty;
            DynamicFields.Add(new ReportDynamicFieldValue
            {
                Key = f.Key,
                Label = f.Label,
                FieldType = f.FieldType,
                Required = f.Required,
                DefaultValue = f.DefaultValue,
                OptionList = opts,
                Value = initialVal
            });
        }
    }

    private void FillDefaultsFromTemplate()
    {
        foreach (var f in DynamicFields)
        {
            if (string.IsNullOrWhiteSpace(f.Value) && !string.IsNullOrWhiteSpace(f.DefaultValue))
                f.Value = f.DefaultValue;
        }
        _shell?.ShowToast("已按本科室模板填充默认标准描述词条。", "Info");
    }

    private (string ContentJson, string Findings, string Impression) CollectDynamicContent()
    {
        var dict = new Dictionary<string, string>();
        foreach (var f in DynamicFields)
            dict[f.Key] = f.Value ?? string.Empty;

        dict.TryGetValue("findings", out string? findings);
        dict.TryGetValue("impression", out string? impression);
        if (string.IsNullOrWhiteSpace(findings))
            findings = string.Join("；", DynamicFields.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => $"{x.Label}:{x.Value}"));
        if (string.IsNullOrWhiteSpace(impression))
            impression = DynamicFields.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.Value))?.Value ?? string.Empty;

        return (JsonSerializer.Serialize(dict), findings ?? string.Empty, impression ?? string.Empty);
    }

    private async Task SyncFromRequestsAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync("/api/v4/medtech-reports/sync-from-requests", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell?.ShowToast(env?.Message ?? "已同步医技申请单", "Info");
        await LoadAllAsync();
    }

    private async Task CreateManualTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPatName) || string.IsNullOrWhiteSpace(NewExamItem))
        {
            _shell?.ShowToast("请填写患者姓名和检查项目名称。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/medtech-reports/create-manual", new
        {
            PatientName = NewPatName.Trim(),
            Gender = NewPatGender,
            Age = NewPatAge,
            MedtechDeptCode = NewDeptCode,
            MedtechDeptName = DeptNameByCode(NewDeptCode),
            ExamItemName = NewExamItem.Trim()
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcDiagnosticReportRow>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast("已分配至【待书写】队列。", "Success");
            NewPatName = string.Empty;
            NewExamItem = string.Empty;
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "创建失败", "Error");
    }

    private async Task IngestDeviceResultAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPatName) || string.IsNullOrWhiteSpace(NewExamItem))
        {
            _shell?.ShowToast("请填写患者姓名与检测项目后再接入仪器直传结果。", "Warning");
            return;
        }
        string dept = NewDeptCode;
        var sampleDict = dept == "ECG"
            ? new Dictionary<string, string>
            {
                ["heart_rate"] = "78",
                ["rhythm"] = "窦性心律",
                ["intervals"] = "PR: 152ms | QRS: 86ms | QT/QTc: 388/412ms",
                ["findings"] = "仪器自动分析：窦性心律，各导联ST-T未见显著偏移。",
                ["impression"] = "窦性心律，大致正常心电图（待医师审签确认）。"
            }
            : new Dictionary<string, string>
            {
                ["specimen_type"] = "血清(黄头促凝管)",
                ["analyzer"] = NewDeviceName,
                ["findings"] = "尿素(BUN): 5.4 mmol/L (参考 3.1-8.0)\n肌酐(Cr): 74.2 umol/L (参考 57-97)\n尿酸(UA): 436.0 umol/L ↑ (参考 208-428)\n胱抑素C: 0.89 mg/L (参考 0.51-1.09)",
                ["impression"] = "尿酸轻度偏高，余指标在参考范围内，仪器质控在控。"
            };

        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/medtech-reports/device-ingest", new
        {
            PatientName = NewPatName.Trim(),
            Gender = NewPatGender,
            Age = NewPatAge,
            MedtechDeptCode = dept,
            MedtechDeptName = DeptNameByCode(dept),
            ExamItemName = NewExamItem.Trim(),
            DeviceName = NewDeviceName,
            ContentJson = JsonSerializer.Serialize(sampleDict),
            FindingsSummary = sampleDict["findings"],
            DiagnosisImpression = sampleDict["impression"],
            IsCriticalValue = false
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcDiagnosticReportRow>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "仪器结果已直接进入【待审签】队列。", "Success");
            NewPatName = string.Empty;
            NewExamItem = string.Empty;
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "仪器结果接入失败", "Error");
    }

    private async Task SaveDraftAsync()
    {
        if (SelectedReport == null) return;
        var (json, findings, impression) = CollectDynamicContent();
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"/api/v4/medtech-reports/{SelectedReport.Id}/save-draft", new
        {
            TemplateId = ActiveEditorTemplate?.Id,
            TemplateName = ActiveEditorTemplate?.TemplateName,
            ContentJson = json,
            FindingsSummary = findings,
            DiagnosisImpression = impression,
            IsCriticalValue = EditIsCriticalValue
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcDiagnosticReportRow>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast("报告草稿已保存。", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "保存失败", "Error");
    }

    private async Task SubmitReviewAsync()
    {
        if (SelectedReport == null) return;
        var missing = DynamicFields.FirstOrDefault(f => f.Required && string.IsNullOrWhiteSpace(f.Value));
        if (missing != null)
        {
            _shell?.ShowToast($"请填写必填项：{missing.Label}", "Warning");
            return;
        }
        var (json, findings, impression) = CollectDynamicContent();
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/medtech-reports/{SelectedReport.Id}/submit-review", new
        {
            TemplateId = ActiveEditorTemplate?.Id,
            TemplateName = ActiveEditorTemplate?.TemplateName,
            ContentJson = json,
            FindingsSummary = findings,
            DiagnosisImpression = impression,
            IsCriticalValue = EditIsCriticalValue
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcDiagnosticReportRow>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast("报告已提交至【待审签】队列。", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "提交审签失败", "Error");
    }

    private async Task ApproveSignAsync()
    {
        if (SelectedReport == null) return;
        var (json, findings, impression) = CollectDynamicContent();
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/medtech-reports/{SelectedReport.Id}/approve", new
        {
            TemplateId = ActiveEditorTemplate?.Id,
            TemplateName = ActiveEditorTemplate?.TemplateName,
            ContentJson = json,
            FindingsSummary = findings,
            DiagnosisImpression = impression,
            IsCriticalValue = EditIsCriticalValue
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcDiagnosticReportRow>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast("报告已审签双签发布，并自动回写临床申请单状态。", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "审签发布失败", "Error");
    }

    private async Task RejectReportAsync()
    {
        if (SelectedReport == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/medtech-reports/{SelectedReport.Id}/reject", new
        {
            Reason = string.IsNullOrWhiteSpace(RejectReasonInput) ? "请核对描述与诊断结论后重新提交" : RejectReasonInput.Trim()
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcDiagnosticReportRow>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast("已驳回退回报告医师修改。", "Warning");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "驳回失败", "Error");
    }

    /// <summary>
    /// 按本科室自定义打印样式（自定义纸张宽高 mm、抬头、副标题、动态字段排版、条码、双签名页脚）生成精确打印单并调起系统打印
    /// </summary>
    private async Task PrintSelectedReportAsync()
    {
        if (SelectedReport == null) return;
        var rpt = SelectedReport;
        var tpl = ActiveEditorTemplate ?? new MtcReportTemplateRow();
        double wMm = (double)tpl.PrintPaperWidthMm;
        double hMm = (double)tpl.PrintPaperHeightMm;
        string barcodeSvg = Code128BarcodeRenderer.RenderSvg(rpt.ReportNo, 52, 12, true);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>{WebUtility.HtmlEncode(tpl.PrintHeaderTitle)} - {WebUtility.HtmlEncode(rpt.PatientName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($"@page {{ size: {wMm:0.#}mm {hMm:0.#}mm; margin: 10mm; }}");
        sb.AppendLine("body { font-family: 'Microsoft YaHei','PingFang SC',sans-serif; color: #111; margin: 0; padding: 12px; background: #f3f4f6; }");
        sb.AppendLine($".page {{ width: {wMm - 20:0.#}mm; min-height: {hMm - 20:0.#}mm; margin: 0 auto; background: #fff; padding: 10mm; box-sizing: border-box; box-shadow: 0 2px 10px rgba(0,0,0,.1); }}");
        sb.AppendLine("@media print { body { background: #fff; padding: 0; } .page { width: auto; min-height: auto; padding: 0; box-shadow: none; } }");
        sb.AppendLine(".hdr { display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #111; padding-bottom: 6px; margin-bottom: 10px; }");
        sb.AppendLine(".hdr h1 { margin: 0; font-size: 16pt; } .hdr .sub { font-size: 10pt; color: #444; margin-top: 2px; }");
        sb.AppendLine(".meta { width: 100%; border-collapse: collapse; margin-bottom: 12px; font-size: 10pt; }");
        sb.AppendLine(".meta td { padding: 4px 6px; border-bottom: 1px solid #e5e7eb; }");
        sb.AppendLine(".sec { margin-bottom: 12px; } .sec-lbl { font-weight: bold; font-size: 10.5pt; margin-bottom: 4px; color: #1f2937; }");
        sb.AppendLine(".sec-val { font-size: 10pt; white-space: pre-wrap; line-height: 1.55; padding-left: 8px; }");
        sb.AppendLine(".crit { display: inline-block; background: #fee2e2; color: #b91c1c; border: 1px solid #ef4444; padding: 2px 8px; font-weight: bold; font-size: 9pt; border-radius: 4px; }");
        sb.AppendLine(".foot { margin-top: 24px; border-top: 1px solid #111; padding-top: 8px; font-size: 9.5pt; }");
        sb.AppendLine(".sigs { display: flex; justify-content: space-between; margin-bottom: 6px; }");
        sb.AppendLine(".note { font-size: 8.5pt; color: #555; }");
        sb.AppendLine("</style></head><body><div class=\"page\">");

        sb.AppendLine("<div class=\"hdr\"><div>");
        sb.AppendLine($"<h1>{WebUtility.HtmlEncode(tpl.PrintHeaderTitle)}</h1>");
        sb.AppendLine($"<div class=\"sub\">{WebUtility.HtmlEncode(tpl.PrintSubTitle ?? rpt.MedtechDeptName)} · 报告号：{WebUtility.HtmlEncode(rpt.ReportNo)}</div>");
        sb.AppendLine("</div><div>" + barcodeSvg + "</div></div>");

        sb.AppendLine("<table class=\"meta\">");
        sb.AppendLine($"<tr><td><b>姓名：</b>{WebUtility.HtmlEncode(rpt.PatientName)}</td><td><b>性别：</b>{WebUtility.HtmlEncode(rpt.Gender)}</td><td><b>年龄：</b>{rpt.Age}岁</td><td><b>病历号：</b>{WebUtility.HtmlEncode(rpt.MedicalRecordNo ?? "-")}</td></tr>");
        sb.AppendLine($"<tr><td><b>申请科室：</b>{WebUtility.HtmlEncode(rpt.OrderDeptName ?? "-")}</td><td colspan=\"2\"><b>检查项目：</b>{WebUtility.HtmlEncode(rpt.ExamItemName)}</td><td><b>来源：</b>{WebUtility.HtmlEncode(rpt.SourceBadge)}</td></tr>");
        sb.AppendLine("</table>");

        if (EditIsCriticalValue || rpt.IsCriticalValue == 1)
            sb.AppendLine("<div style=\"margin-bottom:10px;\"><span class=\"crit\">⚠ 危急值报告 — 已按规范触发临床危急值通知</span></div>");

        foreach (var f in DynamicFields)
        {
            sb.AppendLine("<div class=\"sec\">");
            sb.AppendLine($"<div class=\"sec-lbl\">【{WebUtility.HtmlEncode(f.Label)}】</div>");
            sb.AppendLine($"<div class=\"sec-val\">{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(f.Value) ? "—" : f.Value)}</div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<div class=\"foot\"><div class=\"sigs\">");
        sb.AppendLine($"<span>报告医师：{WebUtility.HtmlEncode(rpt.WriterName ?? "-")}</span>");
        sb.AppendLine($"<span>审签医师：{WebUtility.HtmlEncode(rpt.ReviewerName ?? "待审签")}</span>");
        sb.AppendLine($"<span>打印时间：{DateTime.Now:yyyy-MM-dd HH:mm}</span>");
        sb.AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(rpt.SignatureHash))
            sb.AppendLine($"<div class=\"note\">SM3 电子签章防伪摘要：{WebUtility.HtmlEncode(rpt.SignatureHash)}</div>");
        sb.AppendLine($"<div class=\"note\">{WebUtility.HtmlEncode(tpl.PrintFooterNote ?? "")}</div>");
        sb.AppendLine("</div></div><script>window.onload=function(){setTimeout(function(){window.print();},200);};</script></body></html>");

        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GHIS_PrintSpool");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"MTC_RPT_{rpt.ReportNo}_{DateTime.Now:HHmmss}.html");
        await File.WriteAllTextAsync(file, sb.ToString(), Encoding.UTF8);
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { }

        _shell?.ShowToast($"已按 [{tpl.MedtechDeptName}] 自定义打印样式（{tpl.PaperDisplay}）生成报告并调起打印。", "Success");
    }

    // ===================== Tab 2: 管理员分科室模板维护 =====================

    private void LoadAdminTemplate(MtcReportTemplateRow tpl)
    {
        _tplEditId = tpl.Id;
        TplEditCode = tpl.TemplateCode;
        TplEditName = tpl.TemplateName;
        TplEditDeptCode = tpl.MedtechDeptCode;
        TplEditDeptName = tpl.MedtechDeptName;
        TplEditModality = tpl.ModalityType;
        TplPrintW = tpl.PrintPaperWidthMm;
        TplPrintH = tpl.PrintPaperHeightMm;
        TplPrintHeader = tpl.PrintHeaderTitle;
        TplPrintSubTitle = tpl.PrintSubTitle ?? string.Empty;
        TplPrintFooter = tpl.PrintFooterNote ?? string.Empty;
        TplIsDefault = tpl.IsDefault == 1;

        EditingSchemaFields.Clear();
        try
        {
            var list = JsonSerializer.Deserialize<List<ReportFieldSchemaDto>>(tpl.EditorSchemaJson, JsonOpts);
            if (list != null) foreach (var f in list) EditingSchemaFields.Add(f);
        }
        catch { }
    }

    private void ClearTemplateEditor()
    {
        _tplEditId = string.Empty;
        TplEditCode = $"TPL-{TplEditDeptCode}-{DateTime.Now:HHmm}";
        TplEditName = "新建医技科室报告模板";
        TplEditDeptName = DeptNameByCode(TplEditDeptCode);
        TplPrintW = 210m;
        TplPrintH = 297m;
        TplPrintHeader = $"GHIS 示范医院 {TplEditDeptName}诊断报告单";
        TplPrintSubTitle = TplEditDeptName;
        TplPrintFooter = "本报告仅供临床医师参考。";
        TplIsDefault = false;
        EditingSchemaFields.Clear();
        EditingSchemaFields.Add(new ReportFieldSchemaDto { Key = "findings", Label = "检查所见", FieldType = "MULTILINE", Required = true });
        EditingSchemaFields.Add(new ReportFieldSchemaDto { Key = "impression", Label = "诊断意见", FieldType = "MULTILINE", Required = true });
    }

    private void AddSchemaField()
    {
        if (string.IsNullOrWhiteSpace(AddFieldKey) || string.IsNullOrWhiteSpace(AddFieldLabel))
        {
            _shell?.ShowToast("请填写字段键名（英文）与字段显示名称。", "Warning");
            return;
        }
        EditingSchemaFields.Add(new ReportFieldSchemaDto
        {
            Key = AddFieldKey.Trim(),
            Label = AddFieldLabel.Trim(),
            FieldType = AddFieldType,
            DefaultValue = AddFieldDefault,
            Options = AddFieldOptions,
            Required = AddFieldRequired
        });
        AddFieldKey = string.Empty;
        AddFieldLabel = string.Empty;
        AddFieldDefault = string.Empty;
        AddFieldOptions = string.Empty;
    }

    private void RemoveSchemaField(ReportFieldSchemaDto? field)
    {
        if (field != null) EditingSchemaFields.Remove(field);
    }

    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(TplEditName) || EditingSchemaFields.Count == 0)
        {
            _shell?.ShowToast("请填写模板名称并至少保留 1 个编辑字段。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/medtech-reports/templates", new
        {
            Id = string.IsNullOrWhiteSpace(_tplEditId) ? null : _tplEditId,
            TemplateCode = TplEditCode,
            TemplateName = TplEditName.Trim(),
            MedtechDeptCode = TplEditDeptCode,
            MedtechDeptName = string.IsNullOrWhiteSpace(TplEditDeptName) ? DeptNameByCode(TplEditDeptCode) : TplEditDeptName.Trim(),
            ModalityType = TplEditModality,
            EditorSchemaJson = JsonSerializer.Serialize(EditingSchemaFields.ToList()),
            PrintPaperWidthMm = TplPrintW,
            PrintPaperHeightMm = TplPrintH,
            PrintHeaderTitle = TplPrintHeader,
            PrintSubTitle = TplPrintSubTitle,
            PrintFooterNote = TplPrintFooter,
            PrintTemplateId = (string?)null,
            IsDefault = TplIsDefault
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcReportTemplateRow>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast("科室报告编辑与打印模板已保存生效。", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "保存模板失败", "Error");
    }

    private async Task DisableTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(_tplEditId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        await http.DeleteAsync($"/api/v4/medtech-reports/templates/{_tplEditId}");
        _shell?.ShowToast("已停用该报告模板。", "Warning");
        await LoadAllAsync();
    }
}
