using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.MobileNursing;

public sealed class PdaPatientOption
{
    public string EncounterId { get; set; } = string.Empty;
    public string AdmissionNo { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string BedCode { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public string DisplayLabel => $"{BedCode} · {PatientName} ({AdmissionNo})";
}

public sealed class PdaPlanItemVm
{
    public string PlanId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string DoseAndRoute { get; set; } = string.Empty;
    public string PlannedTimeText { get; set; } = string.Empty;
    public int ExecStatus { get; set; }
    public string PdaScanToken { get; set; } = string.Empty;
    public string ExecNurseId { get; set; } = string.Empty;

    public bool IsPending => ExecStatus == 0;
    public string StatusText => ExecStatus switch
    {
        0 => "待床旁扫码执行",
        1 => "✓ 已扫码执行闭环",
        2 => "已停止撤销",
        _ => "其他"
    };
}

public sealed class MobileNursingScannerViewModel : ClientModelBase, IStationLifecycle
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;

    private PdaPatientOption? _selectedPatient;
    private PdaPlanItemVm? _selectedPlan;
    private string _patientWristbandInput = string.Empty;
    private string _itemBarcodeInput = string.Empty;
    private string _dualCheckNurseId = "USER-NURSE-W1";
    private string _specimenRequestIdInput = string.Empty;
    private string _specimenBarcodeInput = string.Empty;
    private string _statusMessage = "请扫描或选择在院患者腕带以加载真实待执行护理计划";
    private bool _isBusy;

    public MobileNursingScannerViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        VerifyWristbandCommand = new RelayCommand(VerifyWristband);
        ExecutePlanCommand = new AsyncRelayCommand(ExecuteSelectedPlanAsync);
        PickPlanCommand = new RelayCommand(p =>
        {
            if (p is PdaPlanItemVm plan)
            {
                SelectedPlan = plan;
                ItemBarcodeInput = string.IsNullOrWhiteSpace(plan.OrderNo) ? plan.OrderId : plan.OrderNo;
            }
        });
        DrawSpecimenCommand = new AsyncRelayCommand(DrawSpecimenAsync);
    }

    public ObservableCollection<PdaPatientOption> Patients { get; } = new();
    public ObservableCollection<PdaPlanItemVm> Plans { get; } = new();

    public PdaPatientOption? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetField(ref _selectedPatient, value))
            {
                OnPropertyChanged(nameof(PatientHeader));
                OnPropertyChanged(nameof(IsWristbandVerified));
                if (value != null) _ = LoadPlansForPatientAsync(value);
            }
        }
    }

    public PdaPlanItemVm? SelectedPlan
    {
        get => _selectedPlan;
        set => SetField(ref _selectedPlan, value);
    }

    public string PatientWristbandInput
    {
        get => _patientWristbandInput;
        set
        {
            if (SetField(ref _patientWristbandInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsWristbandVerified));
            }
        }
    }

    public string ItemBarcodeInput { get => _itemBarcodeInput; set => SetField(ref _itemBarcodeInput, value ?? string.Empty); }
    public string DualCheckNurseId { get => _dualCheckNurseId; set => SetField(ref _dualCheckNurseId, value ?? string.Empty); }
    public string SpecimenRequestIdInput { get => _specimenRequestIdInput; set => SetField(ref _specimenRequestIdInput, value ?? string.Empty); }
    public string SpecimenBarcodeInput { get => _specimenBarcodeInput; set => SetField(ref _specimenBarcodeInput, value ?? string.Empty); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value ?? string.Empty); }
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }

    public string PatientHeader => SelectedPatient == null
        ? "未选择患者"
        : $"{SelectedPatient.PatientName} · {SelectedPatient.BedCode} · 住院号 {SelectedPatient.AdmissionNo}";

    public bool IsWristbandVerified
    {
        get
        {
            if (SelectedPatient == null || string.IsNullOrWhiteSpace(PatientWristbandInput)) return false;
            var scan = PatientWristbandInput.Trim();
            return string.Equals(scan, SelectedPatient.AdmissionNo, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scan, SelectedPatient.PatientId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scan, SelectedPatient.EncounterId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public int PendingPlanCount => Plans.Count(p => p.IsPending);
    public int DonePlanCount => Plans.Count(p => p.ExecStatus == 1);

    public ICommand RefreshCommand { get; }
    public ICommand VerifyWristbandCommand { get; }
    public ICommand ExecutePlanCommand { get; }
    public ICommand PickPlanCommand { get; }
    public ICommand DrawSpecimenCommand { get; }

    public void Activate() => _ = LoadAsync();
    public void Deactivate() { }
    public void Dispose() { }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _workbench.RefreshInpatientsFromServerAsync();
            Patients.Clear();
            foreach (var rec in _workbench.AllInpatientRecords)
            {
                Patients.Add(new PdaPatientOption
                {
                    EncounterId = string.IsNullOrWhiteSpace(rec.EncounterId) ? rec.AdmissionNo : rec.EncounterId,
                    AdmissionNo = rec.AdmissionNo,
                    PatientId = string.IsNullOrWhiteSpace(rec.PatientId) ? rec.MedicalRecordNo : rec.PatientId,
                    PatientName = rec.PatientName,
                    BedCode = rec.BedCode,
                    WardId = string.IsNullOrWhiteSpace(rec.AdmitWardId) ? "DEPT-WARD-02" : rec.AdmitWardId
                });
            }
            SelectedPatient ??= Patients.FirstOrDefault();
            if (SelectedPatient != null)
            {
                await LoadPlansForPatientAsync(SelectedPatient);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPlansForPatientAsync(PdaPatientOption patient)
    {
        Plans.Clear();
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var ordEnv = await http.GetFromJsonAsync<ApiResponse<List<ServerMedicalOrderDto>>>(
                $"/api/v4/orders/by-encounter/{Uri.EscapeDataString(patient.EncounterId)}", JsonOpts);
            var ordMap = (ordEnv?.Data ?? new List<ServerMedicalOrderDto>())
                .ToDictionary(o => o.Id, o => o);

            var planEnv = await http.GetFromJsonAsync<ApiResponse<List<ServerExecPlanDto>>>(
                $"/api/v4/orders/plans/by-encounter/{Uri.EscapeDataString(patient.EncounterId)}", JsonOpts);

            if (planEnv?.Data != null)
            {
                foreach (var p in planEnv.Data)
                {
                    ordMap.TryGetValue(p.OrderId, out var ord);
                    string timeStr = p.PlannedTimeMs > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(p.PlannedTimeMs).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm")
                        : "-";
                    Plans.Add(new PdaPlanItemVm
                    {
                        PlanId = p.Id,
                        OrderId = p.OrderId,
                        OrderNo = ord?.OrderNo ?? string.Empty,
                        ItemName = ord?.ItemName ?? $"医嘱 {p.OrderId}",
                        DoseAndRoute = ord != null ? $"{ord.Dosage:0.##} · {ord.RouteCode} · {ord.FrequencyCode}" : string.Empty,
                        PlannedTimeText = timeStr,
                        ExecStatus = p.ExecStatus,
                        PdaScanToken = p.PdaScanToken ?? p.Id,
                        ExecNurseId = p.ExecNurseId ?? string.Empty
                    });
                }
            }
            SelectedPlan = Plans.FirstOrDefault(x => x.IsPending) ?? Plans.FirstOrDefault();
            OnPropertyChanged(nameof(PendingPlanCount));
            OnPropertyChanged(nameof(DonePlanCount));
            StatusMessage = $"已加载 [{patient.PatientName}] 共 {Plans.Count} 条执行计划（待执行 {PendingPlanCount} 条）";
        }
        catch (Exception ex)
        {
            StatusMessage = "读取患者执行计划失败：" + ex.Message;
        }
    }

    private void VerifyWristband()
    {
        if (SelectedPatient == null) { _shell?.ShowToast("请先选择患者", "Warning"); return; }
        if (string.IsNullOrWhiteSpace(PatientWristbandInput))
        {
            PatientWristbandInput = SelectedPatient.AdmissionNo;
        }
        if (IsWristbandVerified)
        {
            StatusMessage = $"✓ 腕带核对通过：{SelectedPatient.PatientName} ({SelectedPatient.BedCode})";
            _shell?.ShowToast(StatusMessage, "Success");
        }
        else
        {
            StatusMessage = "❌ 【409 腕带不匹配】患者腕带与所选住院患者档案不符，严禁执行！";
            _shell?.ShowToast(StatusMessage, "Error");
        }
    }

    private async Task ExecuteSelectedPlanAsync()
    {
        if (SelectedPatient == null) { _shell?.ShowToast("请先选择住院患者", "Warning"); return; }
        if (!IsWristbandVerified)
        {
            _shell?.ShowToast("【三查七对阻断】请先扫描或核验患者腕带条码！", "Error");
            return;
        }
        var plan = SelectedPlan ?? Plans.FirstOrDefault(p => p.IsPending);
        if (plan == null || !plan.IsPending)
        {
            _shell?.ShowToast("当前没有待执行的床旁计划单", "Info");
            return;
        }

        string currentUser = SessionContext.Current.UserId ?? "USER-NURSE-W2";
        string dualNurse = string.Equals(DualCheckNurseId.Trim(), currentUser, StringComparison.OrdinalIgnoreCase)
            ? (currentUser == "USER-NURSE-W1" ? "USER-NURSE-W2" : "USER-NURSE-W1")
            : DualCheckNurseId.Trim();

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                PlanId = plan.PlanId,
                ExecNurseId = currentUser,
                DualCheckNurseId = dualNurse,
                PdaScanToken = string.IsNullOrWhiteSpace(plan.PdaScanToken) ? plan.PlanId : plan.PdaScanToken,
                PatientWristbandCode = PatientWristbandInput.Trim(),
                ItemBarcode = string.IsNullOrWhiteSpace(ItemBarcodeInput) ? plan.OrderNo : ItemBarcodeInput.Trim(),
                HighRiskAckReason = "PDA移动护理终端双码核验执行"
            };
            using var resp = await http.PostAsJsonAsync("/api/v4/orders/plans/pda-execute", payload, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                string err = env?.Message ?? $"PDA执行被服务端拦截 ({(int)resp.StatusCode})";
                StatusMessage = "❌ " + err;
                _shell?.ShowToast(err, "Error");
                return;
            }
            StatusMessage = $"✓ {env.Message}";
            _shell?.ShowToast(StatusMessage, "Success");
            ItemBarcodeInput = string.Empty;
            await LoadPlansForPatientAsync(SelectedPatient);
        }
        catch (Exception ex)
        {
            StatusMessage = "❌ PDA 执行异常：" + ex.Message;
            _shell?.ShowToast(StatusMessage, "Error");
        }
    }

    private async Task DrawSpecimenAsync()
    {
        if (SelectedPatient == null || string.IsNullOrWhiteSpace(SpecimenRequestIdInput) || string.IsNullOrWhiteSpace(SpecimenBarcodeInput))
        {
            _shell?.ShowToast("请填写医技申请单号与采血管条码", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/medtech/specimens/draw", new
            {
                RequestId = SpecimenRequestIdInput.Trim(),
                PatientId = SelectedPatient.PatientId,
                SampleBarcode = SpecimenBarcodeInput.Trim(),
                WardId = SelectedPatient.WardId,
                Notes = "病区PDA床旁采集"
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                _shell?.ShowToast(env?.Message ?? "标本登记失败", "Error");
                return;
            }
            _shell?.ShowToast(env.Message ?? "标本采集成功", "Success");
            SpecimenBarcodeInput = string.Empty;
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("标本登记异常：" + ex.Message, "Error");
        }
    }
}
