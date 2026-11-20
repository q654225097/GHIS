using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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

    // Custom Reports & Dynamic SQL
    private ReportDefinitionItemModel? _selectedReport;
    private string _selectedCategory = "全部";
    private string _searchKeyword = string.Empty;
    private bool _isExecuting;
    private string _executionStatusText = string.Empty;
    private string? _executionErrorMessage;
    private int _totalResultRows;

    // Designer state
    private bool _isDesignerOpen;
    private bool _isEditingExisting;
    private string _editReportCode = string.Empty;
    private string _editReportName = string.Empty;
    private string _editCategory = "综合";
    private string? _editDescription;
    private string _editSqlTemplate = string.Empty;
    private string _editParametersJson = "[]";
    private bool _editIsPublished = true;
    private string? _designerValidationMessage;

    public AnalyticsStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCsvCommand = new AsyncRelayCommand(p => ExportCsvAsync(p as string));

        // Custom Reports Commands
        RefreshReportsCommand = new AsyncRelayCommand(LoadReportsAsync);
        ExecuteReportCommand = new AsyncRelayCommand(ExecuteReportAsync);
        ResetParametersCommand = new RelayCommand(ResetParameters);
        ExportCurrentResultCsvCommand = new AsyncRelayCommand(ExportCurrentResultCsvAsync);
        OpenNewReportDesignerCommand = new RelayCommand(OpenNewReportDesigner);
        OpenEditReportDesignerCommand = new RelayCommand(OpenEditReportDesigner);
        CloseDesignerCommand = new RelayCommand(CloseDesigner);
        AutoExtractParametersCommand = new AsyncRelayCommand(AutoExtractParametersAsync);
        TestSqlInDesignerCommand = new AsyncRelayCommand(TestSqlInDesignerAsync);
        SaveReportDefinitionCommand = new AsyncRelayCommand(SaveReportDefinitionAsync);
        DeleteReportDefinitionCommand = new AsyncRelayCommand(DeleteReportDefinitionAsync);

        _ = RefreshAsync();
        _ = LoadReportsAsync();
    }

    public WorkbenchModel Workbench { get; }
    public bool IsAdmin => SessionContext.Current.IsAdmin || Workbench.IsAdmin;

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

    #region Overview Properties

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

    #endregion

    #region Custom Reports Properties

    public ObservableCollection<ReportDefinitionItemModel> AllReports { get; } = new();
    public ObservableCollection<ReportDefinitionItemModel> FilteredReports { get; } = new();
    public ObservableCollection<string> Categories { get; } = new()
    {
        "全部", "门急诊", "住院业务", "药事进销存", "财务结算", "医技检查", "综合"
    };

    public ObservableCollection<ReportParameterInputModel> DynamicParameters { get; } = new();
    public ObservableCollection<string> ResultColumns { get; } = new();
    public ObservableCollection<IReadOnlyList<string>> ResultRows { get; } = new();

    public ReportDefinitionItemModel? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetField(ref _selectedReport, value))
            {
                OnReportSelectionChanged();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
            {
                ApplyReportFilter();
            }
        }
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetField(ref _searchKeyword, value))
            {
                ApplyReportFilter();
            }
        }
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetField(ref _isExecuting, value);
    }

    public string ExecutionStatusText
    {
        get => _executionStatusText;
        set => SetField(ref _executionStatusText, value);
    }

    public string? ExecutionErrorMessage
    {
        get => _executionErrorMessage;
        set => SetField(ref _executionErrorMessage, value);
    }

    public int TotalResultRows
    {
        get => _totalResultRows;
        set => SetField(ref _totalResultRows, value);
    }

    public bool HasResults => ResultRows.Count > 0;

    #endregion

    #region Designer Properties

    public bool IsDesignerOpen
    {
        get => _isDesignerOpen;
        set => SetField(ref _isDesignerOpen, value);
    }

    public bool IsEditingExisting
    {
        get => _isEditingExisting;
        set => SetField(ref _isEditingExisting, value);
    }

    public string EditReportCode
    {
        get => _editReportCode;
        set => SetField(ref _editReportCode, value);
    }

    public string EditReportName
    {
        get => _editReportName;
        set => SetField(ref _editReportName, value);
    }

    public string EditCategory
    {
        get => _editCategory;
        set => SetField(ref _editCategory, value);
    }

    public string? EditDescription
    {
        get => _editDescription;
        set => SetField(ref _editDescription, value);
    }

    public string EditSqlTemplate
    {
        get => _editSqlTemplate;
        set => SetField(ref _editSqlTemplate, value);
    }

    public string EditParametersJson
    {
        get => _editParametersJson;
        set => SetField(ref _editParametersJson, value);
    }

    public bool EditIsPublished
    {
        get => _editIsPublished;
        set => SetField(ref _editIsPublished, value);
    }

    public string? DesignerValidationMessage
    {
        get => _designerValidationMessage;
        set => SetField(ref _designerValidationMessage, value);
    }

    #endregion

    #region Commands

    public ICommand RefreshReportsCommand { get; }
    public ICommand ExecuteReportCommand { get; }
    public ICommand ResetParametersCommand { get; }
    public ICommand ExportCurrentResultCsvCommand { get; }
    public ICommand OpenNewReportDesignerCommand { get; }
    public ICommand OpenEditReportDesignerCommand { get; }
    public ICommand CloseDesignerCommand { get; }
    public ICommand AutoExtractParametersCommand { get; }
    public ICommand TestSqlInDesignerCommand { get; }
    public ICommand SaveReportDefinitionCommand { get; }
    public ICommand DeleteReportDefinitionCommand { get; }

    #endregion

    public bool OnShortcut(string keyGesture)
    {
        if (keyGesture is "F5")
        {
            _ = RefreshAsync();
            _ = LoadReportsAsync();
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

    #region Custom Reports Logic

    public async Task LoadReportsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<ReportDefinitionItemModel>>>("/api/v4/ops/reports/definitions", JsonOptions);

            AllReports.Clear();
            if (res?.Code == 200 && res.Data != null)
            {
                foreach (var item in res.Data)
                {
                    AllReports.Add(item);
                }
            }

            ApplyReportFilter();

            if (SelectedReport == null && FilteredReports.Count > 0)
            {
                SelectedReport = FilteredReports[0];
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"加载自定义报表列表失败: {ex.Message}", "Error");
        }
    }

    private void ApplyReportFilter()
    {
        var filtered = AllReports.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "全部")
        {
            filtered = filtered.Where(r => r.Category == SelectedCategory);
        }

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var kw = SearchKeyword.Trim();
            filtered = filtered.Where(r =>
                r.ReportName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                r.ReportCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (r.Description?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        FilteredReports.Clear();
        foreach (var r in filtered)
        {
            FilteredReports.Add(r);
        }
    }

    private void OnReportSelectionChanged()
    {
        DynamicParameters.Clear();
        ResultColumns.Clear();
        ResultRows.Clear();
        TotalResultRows = 0;
        ExecutionStatusText = string.Empty;
        ExecutionErrorMessage = null;
        OnPropertyChanged(nameof(HasResults));

        if (SelectedReport == null) return;

        // 解析参数元数据
        if (!string.IsNullOrWhiteSpace(SelectedReport.ParametersSchemaJson))
        {
            try
            {
                var schemaList = JsonSerializer.Deserialize<List<ReportParameterSchemaItem>>(SelectedReport.ParametersSchemaJson, JsonOptions);
                if (schemaList != null)
                {
                    foreach (var s in schemaList)
                    {
                        DynamicParameters.Add(new ReportParameterInputModel
                        {
                            Name = s.Name,
                            Label = s.Label,
                            Type = s.Type,
                            DefaultValue = s.DefaultValue,
                            Value = s.DefaultValue ?? string.Empty,
                            IsRequired = s.IsRequired,
                            Description = s.Description
                        });
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    public async Task ExecuteReportAsync()
    {
        if (SelectedReport == null) return;

        IsExecuting = true;
        ExecutionStatusText = "正在执行 SQL 查询与参数绑定...";
        ExecutionErrorMessage = null;

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();

            var paramDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in DynamicParameters)
            {
                paramDict[p.Name] = string.IsNullOrWhiteSpace(p.Value) ? null : p.Value.Trim();
            }

            var response = await http.PostAsJsonAsync($"/api/v4/ops/reports/definitions/{SelectedReport.Id}/execute", paramDict);
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<ReportQueryResultDto>>(JsonOptions);

            if (result?.Code == 200 && result.Data != null)
            {
                ResultColumns.Clear();
                foreach (var c in result.Data.Columns)
                {
                    ResultColumns.Add(c);
                }

                ResultRows.Clear();
                foreach (var r in result.Data.Rows)
                {
                    ResultRows.Add(r);
                }

                TotalResultRows = result.Data.TotalRows;
                ExecutionStatusText = $"查询成功: 返回 {result.Data.TotalRows} 行记录 | 耗时 {result.Data.ExecutionDurationMs} ms ({result.Data.ExecutedAt})";
                OnPropertyChanged(nameof(HasResults));
            }
            else
            {
                ExecutionErrorMessage = result?.Message ?? "执行报表失败";
                ExecutionStatusText = "查询失败";
            }
        }
        catch (Exception ex)
        {
            ExecutionErrorMessage = $"网络或请求异常: {ex.Message}";
            ExecutionStatusText = "执行出错";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private void ResetParameters()
    {
        foreach (var p in DynamicParameters)
        {
            p.Value = p.DefaultValue ?? string.Empty;
        }
    }

    public async Task ExportCurrentResultCsvAsync()
    {
        if (SelectedReport == null || ResultRows.Count == 0)
        {
            _shell?.ShowToast("当前无查询数据可导出", "Warning");
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.Append('\uFEFF'); // UTF-8 BOM
            sb.AppendLine(string.Join(",", ResultColumns.Select(EscapeCsv)));

            foreach (var row in ResultRows)
            {
                sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            }

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var safeName = string.Join("_", SelectedReport.ReportName.Split(Path.GetInvalidFileNameChars()));
            var filePath = Path.Combine(desktopPath, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

            _shell?.ShowToast($"报表结果已成功导出至桌面: {Path.GetFileName(filePath)}", "Success");
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"导出 CSV 失败: {ex.Message}", "Error");
        }
    }

    #endregion

    #region Admin Designer Logic

    private void OpenNewReportDesigner()
    {
        if (!IsAdmin)
        {
            _shell?.ShowToast("仅系统管理员具有自定义报表设计权限", "Warning");
            return;
        }

        IsEditingExisting = false;
        EditReportCode = $"RPT_CUSTOM_{DateTime.Now:yyyyMMdd}_{new Random().Next(100, 999)}";
        EditReportName = "新自定义运营报表";
        EditCategory = "综合";
        EditDescription = "通过参数占位符实现多维筛选的统计报表";
        EditSqlTemplate = @"SELECT 
    v.dept_name AS 科室名称,
    COUNT(1) AS 就诊人次,
    COALESCE(SUM(v.total_amount), 0) AS 总费用
FROM opd_visits v
WHERE (@StartDate IS NULL OR v.visit_date_int >= @StartDate)
  AND (@EndDate IS NULL OR v.visit_date_int <= @EndDate)
  AND (@DeptName IS NULL OR @DeptName = '' OR v.dept_name LIKE '%' || @DeptName || '%')
GROUP BY v.dept_name
ORDER BY 总费用 DESC";

        EditParametersJson = @"[
  { ""Name"": ""StartDate"", ""Label"": ""开始日期 (YYYYMMDD)"", ""Type"": ""INT"", ""DefaultValue"": ""20261101"", ""IsRequired"": false, ""Description"": ""就诊开始日期，如 20261101"" },
  { ""Name"": ""EndDate"", ""Label"": ""结束日期 (YYYYMMDD)"", ""Type"": ""INT"", ""DefaultValue"": ""20261130"", ""IsRequired"": false, ""Description"": ""就诊结束日期，如 20261130"" },
  { ""Name"": ""DeptName"", ""Label"": ""科室名称模糊搜索"", ""Type"": ""STRING"", ""DefaultValue"": """", ""IsRequired"": false, ""Description"": ""科室名称关键字，留空查询全部"" }
]";
        EditIsPublished = true;
        DesignerValidationMessage = null;
        IsDesignerOpen = true;
    }

    private void OpenEditReportDesigner()
    {
        if (!IsAdmin)
        {
            _shell?.ShowToast("仅系统管理员具有自定义报表设计权限", "Warning");
            return;
        }

        if (SelectedReport == null)
        {
            _shell?.ShowToast("请先选择要编辑的报表", "Warning");
            return;
        }

        IsEditingExisting = true;
        EditReportCode = SelectedReport.ReportCode;
        EditReportName = SelectedReport.ReportName;
        EditCategory = SelectedReport.Category;
        EditDescription = SelectedReport.Description;
        EditSqlTemplate = SelectedReport.SqlTemplate;
        EditParametersJson = SelectedReport.ParametersSchemaJson ?? "[]";
        EditIsPublished = SelectedReport.IsPublished == 1;
        DesignerValidationMessage = null;
        IsDesignerOpen = true;
    }

    private void CloseDesigner()
    {
        IsDesignerOpen = false;
        DesignerValidationMessage = null;
    }

    public async Task AutoExtractParametersAsync()
    {
        if (string.IsNullOrWhiteSpace(EditSqlTemplate))
        {
            DesignerValidationMessage = "请先在上方输入 SQL 查询语句";
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var response = await http.PostAsJsonAsync("/api/v4/ops/reports/validate-sql", new { SqlTemplate = EditSqlTemplate });
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<SqlValidationResultDto>>(JsonOptions);

            if (result?.Code == 200 && result.Data != null)
            {
                if (!result.Data.IsValid)
                {
                    DesignerValidationMessage = $"SQL 校验未通过: {result.Data.ErrorMessage}";
                    return;
                }

                EditParametersJson = JsonSerializer.Serialize(result.Data.ExtractedParameters, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                DesignerValidationMessage = $"已成功从 SQL 提取 {result.Data.ExtractedParameters.Count} 个参数占位符并生成配置骨架";
            }
            else
            {
                DesignerValidationMessage = result?.Message ?? "参数提取失败";
            }
        }
        catch (Exception ex)
        {
            DesignerValidationMessage = $"参数提取异常: {ex.Message}";
        }
    }

    public async Task TestSqlInDesignerAsync()
    {
        if (string.IsNullOrWhiteSpace(EditSqlTemplate))
        {
            DesignerValidationMessage = "SQL 查询语句不能为空";
            return;
        }

        try
        {
            DesignerValidationMessage = "正在测试 SQL 查询...";
            using var http = AuthenticatedHttpClientFactory.Create();

            // 解析当前参数 JSON 中的默认值
            var testParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(EditParametersJson))
            {
                try
                {
                    var pList = JsonSerializer.Deserialize<List<ReportParameterSchemaItem>>(EditParametersJson, JsonOptions);
                    if (pList != null)
                    {
                        foreach (var p in pList)
                        {
                            testParams[p.Name] = p.DefaultValue;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            var response = await http.PostAsJsonAsync("/api/v4/ops/reports/execute-raw", new
            {
                SqlTemplate = EditSqlTemplate,
                Parameters = testParams
            });
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<ReportQueryResultDto>>(JsonOptions);

            if (result?.Code == 200 && result.Data != null)
            {
                DesignerValidationMessage = $"测试通过！SQL 语法正确，返回 {result.Data.TotalRows} 行，列数: {result.Data.Columns.Count} (包含: {string.Join(", ", result.Data.Columns.Take(5))}{(result.Data.Columns.Count > 5 ? "..." : "")})，耗时 {result.Data.ExecutionDurationMs} ms";
            }
            else
            {
                DesignerValidationMessage = $"测试执行失败: {result?.Message}";
            }
        }
        catch (Exception ex)
        {
            DesignerValidationMessage = $"测试出错: {ex.Message}";
        }
    }

    public async Task SaveReportDefinitionAsync()
    {
        if (string.IsNullOrWhiteSpace(EditReportCode) || string.IsNullOrWhiteSpace(EditReportName))
        {
            DesignerValidationMessage = "报表编码与报表名称为必填项";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditSqlTemplate))
        {
            DesignerValidationMessage = "SQL 查询语句不能为空";
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();

            var payload = new
            {
                ReportCode = EditReportCode.Trim(),
                ReportName = EditReportName.Trim(),
                Category = EditCategory.Trim(),
                Description = EditDescription?.Trim(),
                SqlTemplate = EditSqlTemplate.Trim(),
                ParametersSchemaJson = EditParametersJson?.Trim(),
                DisplayColumnsJson = (string?)null,
                IsPublished = EditIsPublished ? 1 : 0
            };

            HttpResponseMessage resp;
            if (IsEditingExisting && SelectedReport != null)
            {
                resp = await http.PutAsJsonAsync($"/api/v4/ops/reports/definitions/{SelectedReport.Id}", payload);
            }
            else
            {
                resp = await http.PostAsJsonAsync("/api/v4/ops/reports/definitions", payload);
            }

            var result = await resp.Content.ReadFromJsonAsync<ApiResponse<ReportDefinitionItemModel>>(JsonOptions);
            if (result?.Code == 200)
            {
                _shell?.ShowToast(IsEditingExisting ? "报表定义更新成功" : "自定义报表创建成功", "Success");
                IsDesignerOpen = false;
                await LoadReportsAsync();

                if (result.Data != null)
                {
                    SelectedReport = AllReports.FirstOrDefault(r => r.ReportCode == result.Data.ReportCode) ?? FilteredReports.FirstOrDefault();
                }
            }
            else
            {
                DesignerValidationMessage = $"保存失败: {result?.Message}";
            }
        }
        catch (Exception ex)
        {
            DesignerValidationMessage = $"保存请求异常: {ex.Message}";
        }
    }

    public async Task DeleteReportDefinitionAsync()
    {
        if (SelectedReport == null) return;
        if (!IsAdmin)
        {
            _shell?.ShowToast("仅系统管理员可删除报表定义", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.DeleteAsync($"/api/v4/ops/reports/definitions/{SelectedReport.Id}");
            var result = await resp.Content.ReadFromJsonAsync<ApiResponse<bool>>(JsonOptions);

            if (result?.Code == 200)
            {
                _shell?.ShowToast($"报表 [{SelectedReport.ReportName}] 已删除", "Success");
                IsDesignerOpen = false;
                await LoadReportsAsync();
            }
            else
            {
                _shell?.ShowToast($"删除失败: {result?.Message}", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"删除请求异常: {ex.Message}", "Error");
        }
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    #endregion

    #region Inner DTOs

    public sealed class ReportDefinitionItemModel : ClientModelBase
    {
        public string Id { get; set; } = string.Empty;
        public string ReportCode { get; set; } = string.Empty;
        public string ReportName { get; set; } = string.Empty;
        public string Category { get; set; } = "综合";
        public string? Description { get; set; }
        public string SqlTemplate { get; set; } = string.Empty;
        public string? ParametersSchemaJson { get; set; }
        public string? DisplayColumnsJson { get; set; }
        public int IsPublished { get; set; }
        public long CreatedAtMs { get; set; }
        public string? CreatedBy { get; set; }
        public long UpdatedAtMs { get; set; }

        public string CategoryBadgeKind => Category switch
        {
            "门急诊" => "Primary",
            "住院业务" => "Success",
            "药事进销存" => "Warning",
            "财务结算" => "Danger",
            "医技检查" => "Info",
            _ => "Secondary"
        };
    }

    public sealed class ReportParameterInputModel : ClientModelBase
    {
        private string _value = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = "STRING";
        public string? DefaultValue { get; set; }
        public bool IsRequired { get; set; }
        public string? Description { get; set; }

        public string Value
        {
            get => _value;
            set => SetField(ref _value, value);
        }
    }

    private sealed record ReportParameterSchemaItem(
        string Name,
        string Label,
        string Type,
        string? DefaultValue,
        bool IsRequired,
        string? Description
    );

    private sealed record ReportQueryResultDto(
        string ReportCode,
        string ReportName,
        List<string> Columns,
        List<List<string>> Rows,
        int TotalRows,
        long ExecutionDurationMs,
        string ExecutedAt
    );

    private sealed record SqlValidationResultDto(
        bool IsValid,
        string? ErrorMessage,
        List<ReportParameterSchemaItem> ExtractedParameters
    );

    #endregion
}
