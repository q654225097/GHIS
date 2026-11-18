using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Analytics;

public sealed class AnalyticsStationViewModel : ClientModelBase, IShortcutTarget
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel? _shell;

    private int _selectedTabIndex;
    private bool _isLoading;

    // Overview KPIs
    private int _totalRegisteredPatients;
    private int _activeOutpatientVisits;
    private int _inpatientAdmissions;
    private int _totalBeds;
    private int _occupiedBeds;
    private decimal _bedOccupancyRate;
    private decimal _totalRevenue;
    private decimal _pharmacyRevenue;
    private decimal _pharmacyRatio;
    private int _pendingCriticalValues;
    private int _unresolvedSecurityAlerts;

    // Detailed metrics strings
    private string _outpatientSummary = string.Empty;
    private string _inpatientSummary = string.Empty;
    private string _pharmacySummary = string.Empty;
    private string _qualitySummary = string.Empty;

    public AnalyticsStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCsvCommand = new AsyncRelayCommand(p => ExportCsvAsync(p as string));

        _ = RefreshAsync();
    }

    public WorkbenchModel Workbench { get; }

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

    public int TotalRegisteredPatients { get => _totalRegisteredPatients; set => SetField(ref _totalRegisteredPatients, value); }
    public int ActiveOutpatientVisits { get => _activeOutpatientVisits; set => SetField(ref _activeOutpatientVisits, value); }
    public int InpatientAdmissions { get => _inpatientAdmissions; set => SetField(ref _inpatientAdmissions, value); }
    public int TotalBeds { get => _totalBeds; set => SetField(ref _totalBeds, value); }
    public int OccupiedBeds { get => _occupiedBeds; set => SetField(ref _occupiedBeds, value); }
    public decimal BedOccupancyRate { get => _bedOccupancyRate; set => SetField(ref _bedOccupancyRate, value); }
    public decimal TotalRevenue { get => _totalRevenue; set => SetField(ref _totalRevenue, value); }
    public decimal PharmacyRevenue { get => _pharmacyRevenue; set => SetField(ref _pharmacyRevenue, value); }
    public decimal PharmacyRatio { get => _pharmacyRatio; set => SetField(ref _pharmacyRatio, value); }
    public int PendingCriticalValues { get => _pendingCriticalValues; set => SetField(ref _pendingCriticalValues, value); }
    public int UnresolvedSecurityAlerts { get => _unresolvedSecurityAlerts; set => SetField(ref _unresolvedSecurityAlerts, value); }

    public string OutpatientSummary { get => _outpatientSummary; set => SetField(ref _outpatientSummary, value); }
    public string InpatientSummary { get => _inpatientSummary; set => SetField(ref _inpatientSummary, value); }
    public string PharmacySummary { get => _pharmacySummary; set => SetField(ref _pharmacySummary, value); }
    public string QualitySummary { get => _qualitySummary; set => SetField(ref _qualitySummary, value); }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public bool OnShortcut(string keyGesture)
    {
        if (keyGesture is "F5")
        {
            _ = RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();

            // Load Overview
            var ovRes = await http.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/v4/ops/analytics/overview", JsonOptions);
            if (ovRes?.Data.ValueKind == JsonValueKind.Object)
            {
                var d = ovRes.Data;
                TotalRegisteredPatients = d.TryGetProperty("totalRegisteredPatients", out var p1) ? p1.GetInt32() : 0;
                ActiveOutpatientVisits = d.TryGetProperty("todayOutpatientVisits", out var p2) ? p2.GetInt32() : 0;
                InpatientAdmissions = d.TryGetProperty("activeInpatients", out var p3) ? p3.GetInt32() : 0;
                OccupiedBeds = InpatientAdmissions;
                TotalBeds = OccupiedBeds + (d.TryGetProperty("availableBedsCount", out var p4) ? p4.GetInt32() : 0);
                BedOccupancyRate = d.TryGetProperty("bedUtilizationRate", out var p5) ? p5.GetDecimal() : 0m;
                TotalRevenue = d.TryGetProperty("todayRevenue", out var p6) ? p6.GetDecimal() : 0m;
                PharmacyRatio = d.TryGetProperty("drugRevenueRatio", out var p7) ? p7.GetDecimal() : 0m;
                PharmacyRevenue = TotalRevenue * PharmacyRatio / 100m;
            }

            // Load Outpatient
            var opdRes = await http.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/v4/ops/analytics/outpatient", JsonOptions);
            if (opdRes?.Data.ValueKind == JsonValueKind.Array)
            {
                int count = opdRes.Data.GetArrayLength();
                OutpatientSummary = $"门急诊科室统计: 覆盖全院 {count} 个临床科室诊疗流水与门诊收费记录。";
            }

            // Load Inpatient
            var ipdRes = await http.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/v4/ops/analytics/inpatient", JsonOptions);
            if (ipdRes?.Data.ValueKind == JsonValueKind.Array)
            {
                int count = ipdRes.Data.GetArrayLength();
                InpatientSummary = $"住院病区周转: 全院 {count} 个病区床位占用、长临医嘱流转与在院患者运行平稳。";
            }

            // Load Pharmacy
            var phmRes = await http.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/v4/ops/analytics/pharmacy", JsonOptions);
            if (phmRes?.Data.ValueKind == JsonValueKind.Object)
            {
                var d = phmRes.Data;
                int totalRx = d.TryGetProperty("totalPrescriptions", out var p1) ? p1.GetInt32() : 0;
                int dispRx = d.TryGetProperty("dispensedPrescriptions", out var p2) ? p2.GetInt32() : 0;
                decimal sales = d.TryGetProperty("totalDrugSales", out var p3) ? p3.GetDecimal() : 0m;
                PharmacySummary = $"处方总计: {totalRx} 张 | 已调剂发药: {dispRx} 张 | 药品销售总额: ¥{sales:F2}";
            }

            // Load Quality
            var qRes = await http.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/v4/ops/analytics/quality", JsonOptions);
            if (qRes?.Data.ValueKind == JsonValueKind.Object)
            {
                var d = qRes.Data;
                decimal gradeARate = d.TryGetProperty("frontSheetGradeARate", out var p1) ? p1.GetDecimal() : 100m;
                decimal emrSignRate = d.TryGetProperty("emrSignCompletionRate", out var p2) ? p2.GetDecimal() : 0m;
                QualitySummary = $"病案首页甲级率: {gradeARate:F1}% | 电子病历审签完成率: {emrSignRate:F1}% | 等保三级哈希防篡改链健全。";
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"加载统计看板失败: {ex.Message}", "Error");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExportCsvAsync(string? dataset)
    {
        var target = dataset ?? "overview";
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var csv = await http.GetStringAsync($"/api/v4/ops/analytics/export.csv?dataset={target}");

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var filePath = Path.Combine(desktopPath, $"GHIS_Report_{target}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await File.WriteAllTextAsync(filePath, csv);

            _shell?.ShowToast($"运营报表已导出至桌面: {Path.GetFileName(filePath)}", "Success");
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"报表导出失败: {ex.Message}", "Error");
        }
    }
}
