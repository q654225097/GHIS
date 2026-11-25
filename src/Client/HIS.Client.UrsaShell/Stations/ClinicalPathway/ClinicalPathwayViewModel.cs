using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.ClinicalPathway;

public sealed class CpwPathwayMatchVm
{
    public string PathwayId { get; set; } = string.Empty;
    public string PathwayCode { get; set; } = string.Empty;
    public string PathwayName { get; set; } = string.Empty;
    public string PrimaryIcdPrefix { get; set; } = string.Empty;
    public int StandardDays { get; set; }
    public decimal BenchmarkCost { get; set; }
    public string DisplayLine => $"[{PathwayCode}] {PathwayName} · 标准住院 {StandardDays} 天 · 费用基准 ¥{BenchmarkCost:0.00}";
}

public sealed class CpwEnrollmentVm
{
    public string Id { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PathwayId { get; set; } = string.Empty;
    public string PathwayName { get; set; } = string.Empty;
    public string CurrentStageId { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class ClinicalPathwayViewModel : ClientModelBase, IStationLifecycle
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;

    private string _encounterId = "ADM20261105001";
    private string _patientId = "PAT85061801";
    private string _icdCode = "I25";
    private CpwPathwayMatchVm? _selectedMatch;
    private CpwEnrollmentVm? _activeEnrollment;
    private string _comparisonSummary = "尚未执行路径医嘱对比";
    private string _emrFusionSnippet = "入径后可自动生成结构化临床路径病历融合段落";
    private string _varianceType = "POSITIVE";
    private string _varianceReasonCategory = "PATIENT_CONDITION";
    private string _varianceReasonDetail = "合并症需要追加专科对症治疗";
    private string _statusMessage = "临床路径 (CPW) · 匹配入径、医嘱对比、变异管控及病历融合";

    public ClinicalPathwayViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;

        MatchPathwaysCommand = new AsyncRelayCommand(MatchAsync);
        RefreshEnrollmentCommand = new AsyncRelayCommand(LoadEnrollmentAsync);
        EnrollCommand = new AsyncRelayCommand(EnrollAsync);
        CompareOrdersCommand = new AsyncRelayCommand(CompareOrdersAsync);
        RecordVarianceCommand = new AsyncRelayCommand(RecordVarianceAsync);
        PullEmrFusionCommand = new AsyncRelayCommand(PullEmrFusionAsync);
    }

    public ObservableCollection<CpwPathwayMatchVm> MatchedPathways { get; } = new();

    public string EncounterId { get => _encounterId; set => SetField(ref _encounterId, value ?? string.Empty); }
    public string PatientId { get => _patientId; set => SetField(ref _patientId, value ?? string.Empty); }
    public string IcdCode { get => _icdCode; set => SetField(ref _icdCode, value ?? string.Empty); }
    public CpwPathwayMatchVm? SelectedMatch { get => _selectedMatch; set => SetField(ref _selectedMatch, value); }
    public CpwEnrollmentVm? ActiveEnrollment
    {
        get => _activeEnrollment;
        set
        {
            if (SetField(ref _activeEnrollment, value))
            {
                OnPropertyChanged(nameof(EnrollmentHeader));
                OnPropertyChanged(nameof(HasEnrollment));
            }
        }
    }
    public string ComparisonSummary { get => _comparisonSummary; set => SetField(ref _comparisonSummary, value); }
    public string EmrFusionSnippet { get => _emrFusionSnippet; set => SetField(ref _emrFusionSnippet, value); }
    public string VarianceType { get => _varianceType; set => SetField(ref _varianceType, value); }
    public string VarianceReasonCategory { get => _varianceReasonCategory; set => SetField(ref _varianceReasonCategory, value); }
    public string VarianceReasonDetail { get => _varianceReasonDetail; set => SetField(ref _varianceReasonDetail, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public IReadOnlyList<string> VarianceTypeOptions { get; } = new[] { "POSITIVE", "NEGATIVE", "EXIT" };
    public IReadOnlyList<string> VarianceCategoryOptions { get; } = new[] { "PATIENT_CONDITION", "HOSPITAL_SYSTEM", "PATIENT_REFUSAL" };
    public bool HasEnrollment => ActiveEnrollment != null;
    public string EnrollmentHeader => ActiveEnrollment == null
        ? "当前患者暂未入径（请选择左侧匹配路径并点击「准入入径」）"
        : $"✓ 已入径：{ActiveEnrollment.PathwayName} · 状态：{ActiveEnrollment.Status} · 阶段：{ActiveEnrollment.CurrentStageName}";

    public ICommand MatchPathwaysCommand { get; }
    public ICommand RefreshEnrollmentCommand { get; }
    public ICommand EnrollCommand { get; }
    public ICommand CompareOrdersCommand { get; }
    public ICommand RecordVarianceCommand { get; }
    public ICommand PullEmrFusionCommand { get; }

    public void Activate() => _ = InitializeAsync();
    public void Deactivate() { }
    public void Dispose() { }

    private async Task InitializeAsync()
    {
        if (_workbench.SelectedInpatient != null)
        {
            EncounterId = string.IsNullOrWhiteSpace(_workbench.SelectedInpatient.EncounterId)
                ? _workbench.SelectedInpatient.AdmissionNo
                : _workbench.SelectedInpatient.EncounterId;
            PatientId = string.IsNullOrWhiteSpace(_workbench.SelectedInpatient.PatientId)
                ? _workbench.SelectedInpatient.MedicalRecordNo
                : _workbench.SelectedInpatient.PatientId;
        }
        await MatchAsync();
        await LoadEnrollmentAsync();
    }

    private async Task MatchAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.GetAsync($"/api/v4/cpw/match?icdCode={Uri.EscapeDataString(IcdCode.Trim())}");
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<List<CpwPathwayMatchVm>>>(JsonOpts);
            MatchedPathways.Clear();
            if (env?.Data != null)
            {
                foreach (var it in env.Data) MatchedPathways.Add(it);
            }
            SelectedMatch ??= MatchedPathways.FirstOrDefault();
            StatusMessage = $"已根据 ICD [{IcdCode}] 匹配到 {MatchedPathways.Count} 个临床路径模板";
        }
        catch (Exception ex) { StatusMessage = "匹配路径异常：" + ex.Message; }
    }

    private async Task LoadEnrollmentAsync()
    {
        if (string.IsNullOrWhiteSpace(EncounterId)) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.GetAsync($"/api/v4/cpw/enrollments/by-encounter/{Uri.EscapeDataString(EncounterId.Trim())}");
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<CpwEnrollmentVm>>(JsonOpts);
            ActiveEnrollment = env?.Data;
        }
        catch { }
    }

    private async Task EnrollAsync()
    {
        if (SelectedMatch == null) { _shell?.ShowToast("请先匹配并选中临床路径", "Warning"); return; }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/cpw/enroll", new
            {
                EncounterId = EncounterId.Trim(),
                PatientId = PatientId.Trim(),
                PathwayId = SelectedMatch.PathwayId,
                EnrolledBy = SessionContext.Current.UserId ?? "DOCTOR"
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<CpwEnrollmentVm>>(JsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0) || env.Data == null)
            {
                _shell?.ShowToast(env?.Message ?? "入径失败", "Error");
                return;
            }
            ActiveEnrollment = env.Data;
            _shell?.ShowToast(env.Message ?? "患者入径成功", "Success");
        }
        catch (Exception ex) { _shell?.ShowToast("入径异常：" + ex.Message, "Error"); }
    }

    private async Task CompareOrdersAsync()
    {
        if (ActiveEnrollment == null) { _shell?.ShowToast("请先完成患者入径", "Warning"); return; }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsync($"/api/v4/cpw/enrollments/{Uri.EscapeDataString(ActiveEnrollment.Id)}/compare-orders", null);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            ComparisonSummary = env?.Code == 200 ? env.Data.ToString() : (env?.Message ?? "对比失败");
            _shell?.ShowToast("路径医嘱吻合度核查完成", "Success");
        }
        catch (Exception ex) { _shell?.ShowToast("医嘱对比异常：" + ex.Message, "Error"); }
    }

    private async Task RecordVarianceAsync()
    {
        if (ActiveEnrollment == null) { _shell?.ShowToast("请先完成患者入径", "Warning"); return; }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync($"/api/v4/cpw/enrollments/{Uri.EscapeDataString(ActiveEnrollment.Id)}/variances", new
            {
                StageId = string.IsNullOrWhiteSpace(ActiveEnrollment.CurrentStageId) ? "STAGE-1" : ActiveEnrollment.CurrentStageId,
                StageItemId = (string?)null,
                RelatedOrderId = (string?)null,
                VarianceType,
                ReasonCategory = VarianceReasonCategory,
                ReasonDetail = VarianceReasonDetail,
                RecordedBy = SessionContext.Current.UserId ?? "DOCTOR"
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<string>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "变异记录提交", env?.Code == 200 ? "Success" : "Error");
        }
        catch (Exception ex) { _shell?.ShowToast("记录变异异常：" + ex.Message, "Error"); }
    }

    private async Task PullEmrFusionAsync()
    {
        if (ActiveEnrollment == null) { _shell?.ShowToast("请先完成患者入径", "Warning"); return; }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.GetAsync($"/api/v4/cpw/enrollments/{Uri.EscapeDataString(ActiveEnrollment.Id)}/emr-fusion");
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            EmrFusionSnippet = env?.Code == 200 ? env.Data.ToString() : (env?.Message ?? "获取失败");
        }
        catch (Exception ex) { EmrFusionSnippet = "异常：" + ex.Message; }
    }
}
