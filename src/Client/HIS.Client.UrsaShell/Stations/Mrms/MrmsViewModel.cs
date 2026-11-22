using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Mrms;

public sealed class MrmsCaseRow
{
    public string Id { get; set; } = string.Empty;
    public string CaseNo { get; set; } = string.Empty;
    public string? EncounterId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; }
    public string? DischargeDeptName { get; set; }
    public int? DischargeDateInt { get; set; }
    public string? PrimaryIcd10Code { get; set; }
    public string? PrimaryIcd10Name { get; set; }
    public string? PrimaryIcd9Code { get; set; }
    public string? PrimaryIcd9Name { get; set; }
    public string? DrgGroupCode { get; set; }
    public string Status { get; set; } = "PENDING_RETRIEVE";
    public string? ShelfLocation { get; set; }
    public string? QcNote { get; set; }
    public string? CoderName { get; set; }

    public string StatusDisplay => Status switch
    {
        "PENDING_RETRIEVE" => "待回收",
        "RETRIEVED" => "已回收·待编码",
        "CODED" => "已编码·待归档",
        "QC_RETURNED" => "质控打回",
        "ARCHIVED" => "已上架归档",
        "SEALED" => "依法封存",
        _ => Status
    };
}

public sealed class MrmsBorrowRow
{
    public string Id { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string CaseNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string ActionType { get; set; } = "BORROW";
    public string ApplicantName { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public int? DueDateInt { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string? HandledBy { get; set; }

    public string ActionDisplay => ActionType switch
    {
        "COPY" => "病案复印",
        "SEAL" => "依法封存",
        _ => "病案借阅"
    };
}

public sealed class MrmsViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _searchKeyword = string.Empty;
    private string _filterStatus = "ALL";
    private MrmsCaseRow? _selectedCase;

    // 编码与归档表单
    private string _editIcd10Code = string.Empty;
    private string _editIcd10Name = string.Empty;
    private string _editIcd9Code = string.Empty;
    private string _editIcd9Name = string.Empty;
    private string _editDrgCode = string.Empty;
    private string _editStatus = "ARCHIVED";
    private string _editShelfLocation = "A区-03架-02层";
    private string _editQcNote = string.Empty;

    // 借阅/复印/封存表单
    private string _borrowActionType = "BORROW";
    private string _borrowApplicant = string.Empty;
    private string _borrowPurpose = "科研教学/医保核查";
    private int _borrowDueDateInt = int.Parse(DateTime.Now.AddDays(7).ToString("yyyyMMdd"));

    public ObservableCollection<MrmsCaseRow> Cases { get; } = new();
    public ObservableCollection<MrmsBorrowRow> BorrowLogs { get; } = new();

    public string[] StatusFilterOptions { get; } = new[] { "ALL", "PENDING_RETRIEVE", "RETRIEVED", "CODED", "QC_RETURNED", "ARCHIVED", "SEALED" };
    public string[] EditStatusOptions { get; } = new[] { "RETRIEVED", "CODED", "QC_RETURNED", "ARCHIVED", "SEALED" };
    public string[] BorrowActionOptions { get; } = new[] { "BORROW", "COPY", "SEAL" };

    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value); }
    public string FilterStatus { get => _filterStatus; set => SetField(ref _filterStatus, value); }
    public MrmsCaseRow? SelectedCase
    {
        get => _selectedCase;
        set
        {
            if (SetField(ref _selectedCase, value) && value != null)
            {
                EditIcd10Code = value.PrimaryIcd10Code ?? string.Empty;
                EditIcd10Name = value.PrimaryIcd10Name ?? string.Empty;
                EditIcd9Code = value.PrimaryIcd9Code ?? string.Empty;
                EditIcd9Name = value.PrimaryIcd9Name ?? string.Empty;
                EditDrgCode = value.DrgGroupCode ?? string.Empty;
                EditStatus = value.Status == "PENDING_RETRIEVE" ? "RETRIEVED" : value.Status;
                EditShelfLocation = value.ShelfLocation ?? "A区-03架-02层";
                EditQcNote = value.QcNote ?? string.Empty;
            }
        }
    }

    public string EditIcd10Code { get => _editIcd10Code; set => SetField(ref _editIcd10Code, value); }
    public string EditIcd10Name { get => _editIcd10Name; set => SetField(ref _editIcd10Name, value); }
    public string EditIcd9Code { get => _editIcd9Code; set => SetField(ref _editIcd9Code, value); }
    public string EditIcd9Name { get => _editIcd9Name; set => SetField(ref _editIcd9Name, value); }
    public string EditDrgCode { get => _editDrgCode; set => SetField(ref _editDrgCode, value); }
    public string EditStatus { get => _editStatus; set => SetField(ref _editStatus, value); }
    public string EditShelfLocation { get => _editShelfLocation; set => SetField(ref _editShelfLocation, value); }
    public string EditQcNote { get => _editQcNote; set => SetField(ref _editQcNote, value); }

    public string BorrowActionType { get => _borrowActionType; set => SetField(ref _borrowActionType, value); }
    public string BorrowApplicant { get => _borrowApplicant; set => SetField(ref _borrowApplicant, value); }
    public string BorrowPurpose { get => _borrowPurpose; set => SetField(ref _borrowPurpose, value); }
    public int BorrowDueDateInt { get => _borrowDueDateInt; set => SetField(ref _borrowDueDateInt, value); }

    public ICommand RefreshCommand { get; }
    public ICommand SyncDischargedCommand { get; }
    public ICommand SaveCodingCommand { get; }
    public ICommand CreateBorrowCommand { get; }

    public MrmsViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        SyncDischargedCommand = new RelayCommand(async () => await SyncDischargedAsync());
        SaveCodingCommand = new RelayCommand(async () => await SaveCodingAsync());
        CreateBorrowCommand = new RelayCommand(async () => await CreateBorrowAsync());
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"api/v4/mrms/cases?status={Uri.EscapeDataString(FilterStatus)}&keyword={Uri.EscapeDataString(SearchKeyword ?? string.Empty)}";
            var cEnv = await http.GetFromJsonAsync<ClientApiEnvelope<List<MrmsCaseRow>>>(url, JsonOpts);
            Cases.Clear();
            if (cEnv?.Data != null) foreach (var r in cEnv.Data) Cases.Add(r);

            var bEnv = await http.GetFromJsonAsync<ClientApiEnvelope<List<MrmsBorrowRow>>>("api/v4/mrms/borrows", JsonOpts);
            BorrowLogs.Clear();
            if (bEnv?.Data != null) foreach (var r in bEnv.Data) BorrowLogs.Add(r);
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"加载病案数据失败：{ex.Message}", "Error");
        }
    }

    private async Task SyncDischargedAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsync("api/v4/mrms/cases/sync-discharged", null);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _shell.ShowToast(env?.Message ?? "已同步出院病案", (env?.Code is 200 or 0) ? "Success" : "Error");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"同步出院病案失败：{ex.Message}", "Error");
        }
    }

    private async Task SaveCodingAsync()
    {
        if (SelectedCase == null)
        {
            _shell.ShowToast("请先选择一份病案", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PutAsJsonAsync($"api/v4/mrms/cases/{SelectedCase.Id}/coding", new
            {
                PrimaryIcd10Code = EditIcd10Code,
                PrimaryIcd10Name = EditIcd10Name,
                PrimaryIcd9Code = string.IsNullOrWhiteSpace(EditIcd9Code) ? null : EditIcd9Code,
                PrimaryIcd9Name = string.IsNullOrWhiteSpace(EditIcd9Name) ? null : EditIcd9Name,
                DrgGroupCode = string.IsNullOrWhiteSpace(EditDrgCode) ? null : EditDrgCode,
                Status = EditStatus,
                ShelfLocation = EditShelfLocation,
                QcNote = EditQcNote
            });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _shell.ShowToast(env?.Message ?? "已更新病案编码与归档状态", (env?.Code is 200 or 0) ? "Success" : "Error");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"保存编码失败：{ex.Message}", "Error");
        }
    }

    private async Task CreateBorrowAsync()
    {
        if (SelectedCase == null || string.IsNullOrWhiteSpace(BorrowApplicant))
        {
            _shell.ShowToast("请先选择病案并填写申请人姓名", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("api/v4/mrms/borrows", new
            {
                CaseId = SelectedCase.Id,
                ActionType = BorrowActionType,
                ApplicantName = BorrowApplicant,
                Purpose = BorrowPurpose,
                DueDateInt = BorrowDueDateInt
            });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _shell.ShowToast(env?.Message ?? "登记成功", (env?.Code is 200 or 0) ? "Success" : "Error");
            BorrowApplicant = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"登记借阅/复印/封存失败：{ex.Message}", "Error");
        }
    }
}
