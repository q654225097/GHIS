using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.PharmacyWindow;

public class SampledPrescriptionModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public long CreatedAtMs { get; set; }
    public string TimeText => CreatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}

public class MedicationReconciliationModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string DosageText { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string ReconciledBy { get; set; } = string.Empty;
    public long CreatedAtMs { get; set; }
    public string TimeText => CreatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}

public sealed partial class PharmacyWindowViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private int _localTabIndex;
    private string _prescriptionFilter = string.Empty;
    private bool _subscribed;

    public PharmacyWindowViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        DispenseOutpatientCommand = new AsyncRelayCommand(DispenseOutpatientWithFeedbackAsync, () => Pharmacy.CanDispenseSelected);
        FilterWardCommand = new AsyncRelayCommand(() => Pharmacy.FilterWardAsync());
        WardDispenseCommand = new AsyncRelayCommand(WardDispenseWithFeedbackAsync);
        RequestReturnCommand = new RelayCommand(ConfirmAndReturn, () => Pharmacy.CanReturnSelected);
        ClearFilterCommand = new RelayCommand(() => PrescriptionFilter = string.Empty);

        // Level 7 Commands
        SamplePrescriptionsCommand = new AsyncRelayCommand(SamplePrescriptionsAsync);
        SubmitCommentCommand = new AsyncRelayCommand(SubmitCommentAsync);
        CreatePivasBatchCommand = new AsyncRelayCommand(CreatePivasBatchAsync);
        LoadMedicationReconCommand = new AsyncRelayCommand(LoadMedicationReconAsync);
        SaveMedicationReconCommand = new AsyncRelayCommand(SaveMedicationReconAsync);

        ApplyPrescriptionFilter();
    }

    public WorkbenchModel Workbench { get; }
    public PharmacyWorkstationViewModel Pharmacy => Workbench.Pharmacy;

    public ObservableCollection<ReadyPrescriptionRow> FilteredReadyPrescriptions { get; } = new();

    public Action? FocusSearchBoxAction { get; set; }

    public int LocalTabIndex
    {
        get => _localTabIndex;
        set
        {
            if (SetField(ref _localTabIndex, value))
            {
                Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(value);
            }
        }
    }

    public string PrescriptionFilter
    {
        get => _prescriptionFilter;
        set
        {
            if (SetField(ref _prescriptionFilter, value))
            {
                ApplyPrescriptionFilter();
            }
        }
    }

    public string DispenseBlockedReason
    {
        get
        {
            if (Pharmacy.SelectedPharmacy == null)
            {
                return "请先在顶部选择当前发药窗口对应的发药药房";
            }
            if (Pharmacy.SelectedReadyPrescription == null)
            {
                return "请先在左侧列表选择一条已收费处方";
            }
            if (Pharmacy.SelectedReadyPrescription.IsOrderClosed)
            {
                return "该发药单已关单归档，禁止二次发药";
            }
            if (!Pharmacy.SelectedReadyPrescription.CanDispense)
            {
                return $"当前处方状态为“{Pharmacy.SelectedReadyPrescription.StateText}”，暂不可发药";
            }
            return Pharmacy.IsBusy ? "正在执行发药提交，请稍候…" : string.Empty;
        }
    }

    public string ReturnBlockedReason
    {
        get
        {
            if (Pharmacy.SelectedPharmacy == null)
            {
                return "请先在顶部选择发药药房";
            }
            if (Pharmacy.SelectedDispense == null)
            {
                return "请先在左侧选择一条历史发药单";
            }
            if (Pharmacy.SelectedDispense.IsReturned)
            {
                return "该发药单已完成退药质检入库，不可重复退药";
            }
            return Pharmacy.IsBusy ? "正在提交退药入库，请稍候…" : string.Empty;
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand DispenseOutpatientCommand { get; }
    public ICommand FilterWardCommand { get; }
    public ICommand WardDispenseCommand { get; }
    public ICommand RequestReturnCommand { get; }
    public ICommand ClearFilterCommand { get; }

    // Level 7: 处方点评与抽检
    public ObservableCollection<SampledPrescriptionModel> SampledPrescriptions { get; } = new();
    private string _reviewSourceType = "OPD";
    public string ReviewSourceType { get => _reviewSourceType; set => SetField(ref _reviewSourceType, value); }
    private int _sampleCount = 10;
    public int SampleCount { get => _sampleCount; set => SetField(ref _sampleCount, value); }
    private SampledPrescriptionModel? _selectedSamplePrescription;
    public SampledPrescriptionModel? SelectedSamplePrescription { get => _selectedSamplePrescription; set => SetField(ref _selectedSamplePrescription, value); }
    private bool _commentIsRational = true;
    public bool CommentIsRational { get => _commentIsRational; set => SetField(ref _commentIsRational, value); }
    private string _commentDefectCodes = string.Empty;
    public string CommentDefectCodes { get => _commentDefectCodes; set => SetField(ref _commentDefectCodes, value); }
    private string _commentText = "用药指征明确，用法用量适宜";
    public string CommentText { get => _commentText; set => SetField(ref _commentText, value); }

    // Level 7: PIVAS & 入院用药重整
    public ObservableCollection<MedicationReconciliationModel> Reconciliations { get; } = new();
    private string _pivasWardId = "DEPT-WARD-02";
    public string PivasWardId { get => _pivasWardId; set => SetField(ref _pivasWardId, value); }
    private string _pivasBatchNo = "BATCH-PIVAS-" + DateTime.Now.ToString("yyyyMMdd-01");
    public string PivasBatchNo { get => _pivasBatchNo; set => SetField(ref _pivasBatchNo, value); }
    private string _pivasPrepType = "ANTIBIOTICS";
    public string PivasPrepType { get => _pivasPrepType; set => SetField(ref _pivasPrepType, value); }
    private string _pivasResult = string.Empty;
    public string PivasResult { get => _pivasResult; set => SetField(ref _pivasResult, value); }

    private string _reconEncounterId = string.Empty;
    public string ReconEncounterId { get => _reconEncounterId; set => SetField(ref _reconEncounterId, value); }
    private string _reconPatientId = string.Empty;
    public string ReconPatientId { get => _reconPatientId; set => SetField(ref _reconPatientId, value); }
    private string _reconDrugName = string.Empty;
    public string ReconDrugName { get => _reconDrugName; set => SetField(ref _reconDrugName, value); }
    private string _reconDosage = string.Empty;
    public string ReconDosage { get => _reconDosage; set => SetField(ref _reconDosage, value); }
    private string _reconDecision = "CONTINUE";
    public string ReconDecision { get => _reconDecision; set => SetField(ref _reconDecision, value); }

    public ICommand SamplePrescriptionsCommand { get; }
    public ICommand SubmitCommentCommand { get; }
    public ICommand CreatePivasBatchCommand { get; }
    public ICommand LoadMedicationReconCommand { get; }
    public ICommand SaveMedicationReconCommand { get; }

    public async Task OnAttachedAsync()
    {
        if (!_subscribed)
        {
            Pharmacy.PropertyChanged += OnPharmacyPropertyChanged;
            Pharmacy.ReadyPrescriptions.CollectionChanged += OnReadyCollectionChanged;
            _subscribed = true;
        }
        Pharmacy.AttachSession();
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(_localTabIndex);
        await Pharmacy.EnsureLoadedAsync();
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(_localTabIndex);
        ApplyPrescriptionFilter();
        RaiseAllComputed();
        _ = LoadPivasOrdersAsync();
    }

    public void OnDetached()
    {
        if (_subscribed)
        {
            Pharmacy.PropertyChanged -= OnPharmacyPropertyChanged;
            Pharmacy.ReadyPrescriptions.CollectionChanged -= OnReadyCollectionChanged;
            _subscribed = false;
        }
        Pharmacy.DetachSession();
    }

    public async Task OnScopePharmacyChangedAsync()
    {
        if (Pharmacy.SelectedPharmacy != null)
        {
            await Pharmacy.RefreshPharmacyScopeAsync();
            ApplyPrescriptionFilter();
            RaiseAllComputed();
        }
    }

    public async Task SelectDispenseAsync(DispenseRow? row)
    {
        await Pharmacy.SelectDispenseAsync(row);
        RaiseAllComputed();
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                LocalTabIndex = 0;
                FocusSearchBoxAction?.Invoke();
                return true;
            case "F5":
                if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
                return true;
            case "Ctrl+Enter":
                if (LocalTabIndex == 0 && DispenseOutpatientCommand.CanExecute(null))
                {
                    DispenseOutpatientCommand.Execute(null);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private async Task RefreshAsync()
    {
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(LocalTabIndex);
        await Pharmacy.RefreshActiveTabAsync();
        ApplyPrescriptionFilter();
        RaiseAllComputed();
        if (!Pharmacy.HasError)
        {
            _shell?.ShowToast("已刷新当前发药队列", "Info");
        }
    }

    private async Task DispenseOutpatientWithFeedbackAsync()
    {
        await Pharmacy.DispenseOutpatientAsync();
        ApplyPrescriptionFilter();
        RaiseAllComputed();
        ForwardStatusToast();
    }

    private async Task WardDispenseWithFeedbackAsync()
    {
        await Pharmacy.WardDispenseAsync();
        RaiseAllComputed();
        ForwardStatusToast();
    }

    private void ConfirmAndReturn()
    {
        if (Pharmacy.SelectedDispense == null)
        {
            return;
        }

        async Task ExecuteAsync()
        {
            await Pharmacy.ConfirmReturnAsync();
            RaiseAllComputed();
            ForwardStatusToast();
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认退药质检入库？",
                $"发药单 {Pharmacy.SelectedDispense.DispenseNo} 完成退药入库后，库存将回补原发药批次并推送至财务待退费队列。",
                "确认退药入库",
                () => _ = ExecuteAsync());
            return;
        }

        _ = ExecuteAsync();
    }

    private void ForwardStatusToast()
    {
        if (Pharmacy.HasError)
        {
            _shell?.ShowToast(Pharmacy.ErrorMessage, "Error");
        }
        else if (Pharmacy.HasNotice)
        {
            _shell?.ShowToast(Pharmacy.Notice, "Success");
        }
    }

    private void ApplyPrescriptionFilter()
    {
        FilteredReadyPrescriptions.Clear();
        var kw = _prescriptionFilter.Trim();
        foreach (var row in Pharmacy.ReadyPrescriptions)
        {
            if (string.IsNullOrEmpty(kw)
                || row.PrescriptionNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || row.PatientName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || row.MedicalRecordNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                FilteredReadyPrescriptions.Add(row);
            }
        }
    }

    private static int MapLocalTabToSharedIndex(int localIndex) => localIndex switch
    {
        0 => 4, // 门诊发药
        1 => 5, // 住院发药
        2 => 3, // 发药记录与退药质检
        _ => 4
    };

    private void OnReadyCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ApplyPrescriptionFilter();
    }

    public async Task SamplePrescriptionsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/pharmacy/comments/sample?sourceType={Uri.EscapeDataString(_reviewSourceType)}&sampleCount={_sampleCount}";
            var res = await http.PostAsync(url, null);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<ApiResponse<List<SampledPrescriptionModel>>>();
                if (body?.Data != null)
                {
                    SampledPrescriptions.Clear();
                    foreach (var p in body.Data)
                    {
                        SampledPrescriptions.Add(p);
                    }
                    _shell?.ShowToast($"随机抽样完成，获取待点评处方 {SampledPrescriptions.Count} 条", "Success");
                }
            }
            else
            {
                _shell?.ShowToast("抽样失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"抽样异常: {ex.Message}", "Error");
        }
    }

    public async Task SubmitCommentAsync()
    {
        if (SelectedSamplePrescription == null)
        {
            _shell?.ShowToast("请先在左侧选择待点评处方", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                SourceType = _reviewSourceType,
                TargetId = SelectedSamplePrescription.Id,
                DoctorId = "DOC-OPD-01",
                PharmacistId = SessionContext.Current.UserId ?? "PHARMACIST-01",
                IsRational = _commentIsRational,
                DefectCodes = string.IsNullOrWhiteSpace(_commentDefectCodes) ? new List<string>() : _commentDefectCodes.Split(',', ';').Select(s => s.Trim()).ToList(),
                CommentText = _commentText,
                ErrorCategory = _commentIsRational ? null : "DOSAGE_ERROR"
            };
            var res = await http.PostAsJsonAsync("/api/v4/pharmacy/comments", payload);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast("处方点评与专家质控意见提交成功", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                _shell?.ShowToast($"点评失败: {err}", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"点评异常: {ex.Message}", "Error");
        }
    }

    public async Task CreatePivasBatchAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                EncounterId = (string?)null,
                WardId = _pivasWardId,
                BatchNo = _pivasBatchNo,
                PrepType = _pivasPrepType,
                PharmacistId = SessionContext.Current.UserId ?? "PHARMACIST-01"
            };
            var res = await http.PostAsJsonAsync("/api/v4/pharmacy/pivas/batches", payload);
            if (res.IsSuccessStatusCode)
            {
                PivasResult = $"✓ PIVAS 批次 [{_pivasBatchNo}] 配置成功，已下发静配贴签与全流程条码";
                _shell?.ShowToast("PIVAS 批次创建成功", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                PivasResult = $"创建失败: {err}";
                _shell?.ShowToast($"PIVAS 批次失败: {err}", "Warning");
            }
        }
        catch (Exception ex)
        {
            PivasResult = $"异常: {ex.Message}";
            _shell?.ShowToast($"PIVAS 异常: {ex.Message}", "Error");
        }
    }

    public async Task LoadMedicationReconAsync()
    {
        if (string.IsNullOrWhiteSpace(_reconEncounterId))
        {
            _shell?.ShowToast("请输入就诊/住院编号", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<MedicationReconciliationModel>>>($"/api/v4/pharmacy/reconciliations/{Uri.EscapeDataString(_reconEncounterId.Trim())}");
            if (res?.Data != null)
            {
                Reconciliations.Clear();
                foreach (var r in res.Data)
                {
                    Reconciliations.Add(r);
                }
                _shell?.ShowToast($"已加载用药重整记录 {Reconciliations.Count} 条", "Info");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"查询重整记录异常: {ex.Message}", "Error");
        }
    }

    public async Task SaveMedicationReconAsync()
    {
        if (string.IsNullOrWhiteSpace(_reconEncounterId) || string.IsNullOrWhiteSpace(_reconDrugName))
        {
            _shell?.ShowToast("就诊编号与药品名称不能为空", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                EncounterId = _reconEncounterId.Trim(),
                PatientId = string.IsNullOrWhiteSpace(_reconPatientId) ? "PAT-RECON" : _reconPatientId.Trim(),
                Source = "PATIENT_REPORTED",
                DrugName = _reconDrugName.Trim(),
                DosageText = _reconDosage.Trim(),
                Decision = _reconDecision,
                ReconciledBy = SessionContext.Current.UserId ?? "PHARMACIST-01"
            };
            var res = await http.PostAsJsonAsync("/api/v4/pharmacy/reconciliations", payload);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast("入院患者既往用药重整记录已入库", "Success");
                _ = LoadMedicationReconAsync();
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                _shell?.ShowToast($"保存重整记录失败: {err}", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"保存重整记录异常: {ex.Message}", "Error");
        }
    }

    private void OnPharmacyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PharmacyWorkstationViewModel.CanDispenseSelected)
            or nameof(PharmacyWorkstationViewModel.CanReturnSelected)
            or nameof(PharmacyWorkstationViewModel.SelectedPharmacy)
            or nameof(PharmacyWorkstationViewModel.SelectedReadyPrescription)
            or nameof(PharmacyWorkstationViewModel.SelectedDispense)
            or nameof(PharmacyWorkstationViewModel.IsBusy))
        {
            RaiseAllComputed();
        }
    }

    private void RaiseAllComputed()
    {
        OnPropertyChanged(nameof(DispenseBlockedReason));
        OnPropertyChanged(nameof(ReturnBlockedReason));
        (DispenseOutpatientCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RequestReturnCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
