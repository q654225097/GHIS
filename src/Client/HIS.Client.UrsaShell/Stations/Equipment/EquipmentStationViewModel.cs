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

namespace HIS.Client.UrsaShell.Stations.Equipment;

public sealed class EquipmentItemVm
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ModelSpec { get; set; }
    public string DeptId { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Status { get; set; } = "NORMAL";
    public short IsMandatoryInspection { get; set; }
    public string? NextInspectionDate { get; set; }
    public string DisplayLine => $"[{Code}] {Name} · {(ModelSpec ?? "-")} · {Status} · 原值 ¥{Price:0.00}"
        + (IsMandatoryInspection == 1 ? $" · 强检 {NextInspectionDate}" : "");
}

public sealed class EquipmentStationViewModel : ClientModelBase, IStationLifecycle
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;

    private string _code = $"EQP-{DateTime.Now:yyyyMMdd}-01";
    private string _name = "128层螺旋CT扫描仪";
    private string _modelSpec = "SOMATOM Definition";
    private string _deptId = "DEPT-MEDTECH-RAD";
    private decimal _price = 4500000m;
    private bool _isMandatory = true;
    private string _nextInspectionDate = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");

    private string _maintType = "CALIBRATION";
    private decimal _maintCost = 1200m;
    private string _maintVendor = "原厂医学工程团队";
    private string _maintEngineer = "张工程师";
    private string _maintResult = "QUALIFIED";
    private EquipmentItemVm? _selectedEquipment;
    private string _summaryText = "设备资产台账、预防性维保 (PM) 与计量强检到期管理";

    public EquipmentStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RegisterEquipmentCommand = new AsyncRelayCommand(RegisterAsync);
        RecordMaintenanceCommand = new AsyncRelayCommand(RecordMaintAsync);
    }

    public ObservableCollection<EquipmentItemVm> Equipments { get; } = new();
    public IReadOnlyList<string> MaintTypes { get; } = new[] { "CALIBRATION", "PREVENTIVE", "REPAIR" };
    public IReadOnlyList<string> MaintResults { get; } = new[] { "QUALIFIED", "REPAIRED", "FAILED" };

    public string Code { get => _code; set => SetField(ref _code, value ?? string.Empty); }
    public string Name { get => _name; set => SetField(ref _name, value ?? string.Empty); }
    public string ModelSpec { get => _modelSpec; set => SetField(ref _modelSpec, value ?? string.Empty); }
    public string DeptId { get => _deptId; set => SetField(ref _deptId, value ?? string.Empty); }
    public decimal Price { get => _price; set => SetField(ref _price, value); }
    public bool IsMandatory { get => _isMandatory; set => SetField(ref _isMandatory, value); }
    public string NextInspectionDate { get => _nextInspectionDate; set => SetField(ref _nextInspectionDate, value ?? string.Empty); }

    public string MaintType { get => _maintType; set => SetField(ref _maintType, value ?? "CALIBRATION"); }
    public decimal MaintCost { get => _maintCost; set => SetField(ref _maintCost, value); }
    public string MaintVendor { get => _maintVendor; set => SetField(ref _maintVendor, value ?? string.Empty); }
    public string MaintEngineer { get => _maintEngineer; set => SetField(ref _maintEngineer, value ?? string.Empty); }
    public string MaintResult { get => _maintResult; set => SetField(ref _maintResult, value ?? "QUALIFIED"); }
    public EquipmentItemVm? SelectedEquipment { get => _selectedEquipment; set => SetField(ref _selectedEquipment, value); }
    public string SummaryText { get => _summaryText; set => SetField(ref _summaryText, value ?? string.Empty); }

    public ICommand RefreshCommand { get; }
    public ICommand RegisterEquipmentCommand { get; }
    public ICommand RecordMaintenanceCommand { get; }

    public void Activate() => _ = RefreshAsync();
    public void Deactivate() { }
    public void Dispose() { }

    private async Task RefreshAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var listEnv = await http.GetFromJsonAsync<ApiResponse<List<EquipmentItemVm>>>("/api/v4/equipment/equipments", JsonOpts);
            Equipments.Clear();
            if (listEnv?.Data != null) foreach (var e in listEnv.Data) Equipments.Add(e);
            SelectedEquipment ??= Equipments.FirstOrDefault();

            var repEnv = await http.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/v4/equipment/report", JsonOpts);
            if (repEnv?.Code == 200)
            {
                var el = repEnv.Data;
                SummaryText = $"在账 {el.GetProperty("totalEquipmentCount").GetInt32()} 台 · 正常 {el.GetProperty("normalCount").GetInt32()} · 停机 {el.GetProperty("outOfServiceCount").GetInt32()} · 待强检 {el.GetProperty("pendingCalibrationCount").GetInt32()} · 资产总值 ¥{el.GetProperty("totalAssetValue").GetDecimal():N2}";
            }
        }
        catch (Exception ex) { SummaryText = "读取设备台账失败：" + ex.Message; }
    }

    private async Task RegisterAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/equipment/equipments", new
            {
                Code = Code.Trim(),
                Name = Name.Trim(),
                ModelSpec,
                DeptId,
                PurchaseDate = DateTime.Today.ToString("yyyy-MM-dd"),
                Price,
                WarrantyEnd = DateTime.Today.AddYears(3).ToString("yyyy-MM-dd"),
                IsMandatoryInspection = IsMandatory,
                NextInspectionDate
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<EquipmentItemVm>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "资产建档完成", env?.Code == 200 ? "Success" : "Error");
            if (env?.Code == 200) Code = $"EQP-{DateTime.Now:yyyyMMddHHmmss}";
            await RefreshAsync();
        }
        catch (Exception ex) { _shell?.ShowToast("登记异常：" + ex.Message, "Error"); }
    }

    private async Task RecordMaintAsync()
    {
        if (SelectedEquipment == null) { _shell?.ShowToast("请先选择一台在账设备", "Warning"); return; }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync($"/api/v4/equipment/equipments/{Uri.EscapeDataString(SelectedEquipment.Id)}/maintenance", new
            {
                Type = MaintType,
                MaintenanceDate = DateTime.Today.ToString("yyyy-MM-dd"),
                Cost = MaintCost,
                Vendor = MaintVendor,
                EngineerName = MaintEngineer,
                Result = MaintResult,
                Remark = "医学工程处周期维保及计量强检"
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "维保记录成功", env?.Code == 200 ? "Success" : "Error");
            await RefreshAsync();
        }
        catch (Exception ex) { _shell?.ShowToast("维保记录异常：" + ex.Message, "Error"); }
    }
}
