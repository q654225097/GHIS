using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.CostCenter;

public sealed class CostCenterReportVm
{
    public string DeptId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public decimal DirectCost { get; set; }
    public decimal AllocatedCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Revenue { get; set; }
    public decimal ProfitMargin { get; set; }
    public string AllocationMethod { get; set; } = "STEP_DOWN";
    public string SummaryLine => $"{Period} · {DeptId} · 直接¥{DirectCost:N2} + 分摊¥{AllocatedCost:N2} = 总成本¥{TotalCost:N2} | 收入¥{Revenue:N2} | 结余率 {ProfitMargin:0.00}%";
}

public sealed class CostCenterStationViewModel : ClientModelBase, IStationLifecycle
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;

    private string _deptId = "DEPT-IPD-01";
    private string _period = DateTime.Now.ToString("yyyy-MM");
    private decimal _directCost = 185000m;
    private decimal _allocatedCost = 42000m;
    private decimal _revenue = 290000m;
    private string _allocationMethod = "STEP_DOWN";
    private string _statusMessage = "全成本核算中心 · 阶梯分摊法 (直接成本 + 行政后勤间接分摊)";

    public CostCenterStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        AllocateCommand = new AsyncRelayCommand(AllocateAsync);
    }

    public ObservableCollection<CostCenterReportVm> Reports { get; } = new();
    public IReadOnlyList<string> Methods { get; } = new[] { "STEP_DOWN", "DIRECT_RATIO", "ACTIVITY_BASED" };

    public string DeptId { get => _deptId; set => SetField(ref _deptId, value ?? string.Empty); }
    public string Period { get => _period; set => SetField(ref _period, value ?? string.Empty); }
    public decimal DirectCost { get => _directCost; set => SetField(ref _directCost, value); }
    public decimal AllocatedCost { get => _allocatedCost; set => SetField(ref _allocatedCost, value); }
    public decimal Revenue { get => _revenue; set => SetField(ref _revenue, value); }
    public string AllocationMethod { get => _allocationMethod; set => SetField(ref _allocationMethod, value ?? "STEP_DOWN"); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value ?? string.Empty); }

    public ICommand RefreshCommand { get; }
    public ICommand AllocateCommand { get; }

    public void Activate() => _ = RefreshAsync();
    public void Deactivate() { }
    public void Dispose() { }

    private async Task RefreshAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var env = await http.GetFromJsonAsync<ApiResponse<List<CostCenterReportVm>>>("/api/v4/finance/cost-centers/report", JsonOpts);
            Reports.Clear();
            if (env?.Data != null) foreach (var r in env.Data) Reports.Add(r);
            StatusMessage = $"已加载 {Reports.Count} 条科室全成本核算损益表";
        }
        catch (Exception ex) { StatusMessage = "加载失败：" + ex.Message; }
    }

    private async Task AllocateAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/finance/cost-centers/allocate", new
            {
                DeptId = DeptId.Trim(),
                Period = Period.Trim(),
                DirectCost,
                AllocatedCost,
                Revenue,
                AllocationMethod
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "分摊完成", env?.Code == 200 ? "Success" : "Error");
            await RefreshAsync();
        }
        catch (Exception ex) { _shell?.ShowToast("核算异常：" + ex.Message, "Error"); }
    }
}
