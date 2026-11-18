using System;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Consumables;

public sealed class ConsumablesWardViewModel : ClientModelBase, IShortcutTarget
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel? _shell;

    private int _selectedTabIndex;
    private bool _isLoading;

    // Requisition form
    private string _targetDeptId = "DEPT-WARD-01";
    private string _reqConsumableId = "CSM-001";
    private int _reqQuantity = 50;
    private string _reqRemark = "病区常规补货";

    // High value trace code form
    private string _scanTraceCode = "UDI-HC-2026110001";
    private string _patientId = "PAT85061801";
    private string _encounterId = "ADM20261105001";

    public ConsumablesWardViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        SubmitRequisitionCommand = new AsyncRelayCommand(SubmitRequisitionAsync);
        BindTraceCodeCommand = new AsyncRelayCommand(BindTraceCodeAsync);
        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync);

        _ = RefreshHistoryAsync();
    }

    public WorkbenchModel Workbench { get; }
    public ObservableCollection<ConsumableRequisitionItemModel> MyRequisitions { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    // Form fields
    public string TargetDeptId { get => _targetDeptId; set => SetField(ref _targetDeptId, value); }
    public string ReqConsumableId { get => _reqConsumableId; set => SetField(ref _reqConsumableId, value); }
    public int ReqQuantity { get => _reqQuantity; set => SetField(ref _reqQuantity, value); }
    public string ReqRemark { get => _reqRemark; set => SetField(ref _reqRemark, value); }

    public string ScanTraceCode { get => _scanTraceCode; set => SetField(ref _scanTraceCode, value); }
    public string PatientId { get => _patientId; set => SetField(ref _patientId, value); }
    public string EncounterId { get => _encounterId; set => SetField(ref _encounterId, value); }

    public ICommand SubmitRequisitionCommand { get; }
    public ICommand BindTraceCodeCommand { get; }
    public ICommand RefreshHistoryCommand { get; }

    public bool OnShortcut(string keyGesture)
    {
        if (keyGesture is "F5")
        {
            _ = RefreshHistoryAsync();
            return true;
        }
        return false;
    }

    public async Task RefreshHistoryAsync()
    {
        IsLoading = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<ConsumableRequisitionItemModel>>>("/api/v4/csm/requisitions", JsonOptions);
            if (res?.Data != null)
            {
                MyRequisitions.Clear();
                foreach (var r in res.Data)
                {
                    MyRequisitions.Add(r);
                }
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"加载科室请领记录失败: {ex.Message}", "Error");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SubmitRequisitionAsync()
    {
        if (string.IsNullOrWhiteSpace(ReqConsumableId) || ReqQuantity <= 0)
        {
            _shell?.ShowToast("请选择耗材并填写大于0的数量", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                WardId = TargetDeptId,
                DeptId = TargetDeptId,
                Remark = ReqRemark,
                Items = new[]
                {
                    new
                    {
                        ConsumableId = ReqConsumableId,
                        Quantity = ReqQuantity,
                        UnitPrice = 1.50m
                    }
                }
            };

            var res = await http.PostAsJsonAsync("/api/v4/csm/requisitions", payload, JsonOptions);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast("耗材请领单提交成功，已推送中心库房审核", "Success");
                await RefreshHistoryAsync();
            }
            else
            {
                _shell?.ShowToast("耗材请领提交失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"请领异常: {ex.Message}", "Error");
        }
    }

    private async Task BindTraceCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(ScanTraceCode) || string.IsNullOrWhiteSpace(PatientId))
        {
            _shell?.ShowToast("请扫码录入耗材追溯码并填写患者ID", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                TraceCode = ScanTraceCode.Trim(),
                PatientId = PatientId.Trim(),
                EncounterId = EncounterId.Trim()
            };

            var res = await http.PostAsJsonAsync("/api/v4/csm/trace/use", payload, JsonOptions);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"高值耗材 [{ScanTraceCode}] 已成功核销并绑定患者病历流水", "Success");
            }
            else
            {
                _shell?.ShowToast("高值耗材扫码核销失败，请核查追溯码是否存在或已被使用", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"扫码核销异常: {ex.Message}", "Error");
        }
    }
}
