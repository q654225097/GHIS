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

namespace HIS.Client.UrsaShell.Stations.PublicHealthSupply;

public sealed class PhCardRow
{
    public string Id { get; set; } = string.Empty;
    public string CardNo { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string IcdCode { get; set; } = string.Empty;
    public string DiseaseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class FollowupPlanRow
{
    public string Id { get; set; } = string.Empty;
    public string PlanNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string SpecialtyTag { get; set; } = string.Empty;
    public int PlannedDateInt { get; set; }
    public string Channel { get; set; } = string.Empty;
    public int? SatisfactionScore { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class UdiInventoryRow
{
    public string Id { get; set; } = string.Empty;
    public string UdiFull { get; set; } = string.Empty;
    public string UdiDi { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public int ExpiryDateInt { get; set; }
    public decimal UnitPrice { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class NrsScreeningRow
{
    public string Id { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int DiseaseSeverityScore { get; set; }
    public int NutritionalImpairedScore { get; set; }
    public int TotalScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public string InterventionType { get; set; } = string.Empty;
}

public sealed class PublicHealthSupplyViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _summaryBanner = "加载 P1 公卫、随访、UDI 耗材与临床营养数据...";

    private string _phPatientName = string.Empty, _phIcdCode = "A15.0";
    private PhCardRow? _selectedPhCard;

    private string _fuPatientName = string.Empty, _fuSpecialtyTag = "PCI术后专病随访";
    private FollowupPlanRow? _selectedFollowup;

    private string _udiFull = "(01)06912345678901(17)281231(10)LOT2026(21)SN0001";
    private string _udiMaterialName = "药物洗脱冠脉支架 3.0x18mm", _udiSupplier = "微创医疗供应链";
    private UdiInventoryRow? _selectedUdi;

    private string _nutPatientName = string.Empty;
    private int _nutDiseaseScore = 2, _nutImpairedScore = 1, _nutAgeYears = 72;

    public ObservableCollection<PhCardRow> PhCards { get; } = new();
    public ObservableCollection<FollowupPlanRow> FollowupPlans { get; } = new();
    public ObservableCollection<UdiInventoryRow> UdiItems { get; } = new();
    public ObservableCollection<NrsScreeningRow> NrsScreenings { get; } = new();

    public string SummaryBanner { get => _summaryBanner; set => SetField(ref _summaryBanner, value); }
    public string PhPatientName { get => _phPatientName; set => SetField(ref _phPatientName, value); }
    public string PhIcdCode { get => _phIcdCode; set => SetField(ref _phIcdCode, value); }
    public PhCardRow? SelectedPhCard { get => _selectedPhCard; set => SetField(ref _selectedPhCard, value); }

    public string FuPatientName { get => _fuPatientName; set => SetField(ref _fuPatientName, value); }
    public string FuSpecialtyTag { get => _fuSpecialtyTag; set => SetField(ref _fuSpecialtyTag, value); }
    public FollowupPlanRow? SelectedFollowup { get => _selectedFollowup; set => SetField(ref _selectedFollowup, value); }

    public string UdiFull { get => _udiFull; set => SetField(ref _udiFull, value); }
    public string UdiMaterialName { get => _udiMaterialName; set => SetField(ref _udiMaterialName, value); }
    public string UdiSupplier { get => _udiSupplier; set => SetField(ref _udiSupplier, value); }
    public UdiInventoryRow? SelectedUdi { get => _selectedUdi; set => SetField(ref _selectedUdi, value); }

    public string NutPatientName { get => _nutPatientName; set => SetField(ref _nutPatientName, value); }
    public int NutDiseaseScore { get => _nutDiseaseScore; set => SetField(ref _nutDiseaseScore, value); }
    public int NutImpairedScore { get => _nutImpairedScore; set => SetField(ref _nutImpairedScore, value); }
    public int NutAgeYears { get => _nutAgeYears; set => SetField(ref _nutAgeYears, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand TriggerPhCardCommand { get; }
    public ICommand SubmitPhCardCommand { get; }
    public ICommand AuditPhCardCommand { get; }
    public ICommand CreateFollowupCommand { get; }
    public ICommand CompleteFollowupCommand { get; }
    public ICommand ReceiveUdiCommand { get; }
    public ICommand ConsumeUdiCommand { get; }
    public ICommand GenerateStatementCommand { get; }
    public ICommand SubmitNrs2002Command { get; }
    public ICommand CreateDietOrderCommand { get; }

    public PublicHealthSupplyViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        TriggerPhCardCommand = new RelayCommand(async () => await TriggerPhCardAsync());
        SubmitPhCardCommand = new RelayCommand(async () => await SubmitPhCardAsync());
        AuditPhCardCommand = new RelayCommand(async () => await AuditPhCardAsync());
        CreateFollowupCommand = new RelayCommand(async () => await CreateFollowupAsync());
        CompleteFollowupCommand = new RelayCommand(async () => await CompleteFollowupAsync());
        ReceiveUdiCommand = new RelayCommand(async () => await ReceiveUdiAsync());
        ConsumeUdiCommand = new RelayCommand(async () => await ConsumeUdiAsync());
        GenerateStatementCommand = new RelayCommand(async () => await GenerateStatementAsync());
        SubmitNrs2002Command = new RelayCommand(async () => await SubmitNrs2002Async());
        CreateDietOrderCommand = new RelayCommand(async () => await CreateDietOrderAsync());
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadPlainListAsync(http, "api/v4/public-health/cards", PhCards);
            await LoadPlainListAsync(http, "api/v4/followup/plans", FollowupPlans);
            await LoadPlainListAsync(http, "api/v4/udi/inventory", UdiItems);
            await LoadPlainListAsync(http, "api/v4/nutrition/nrs2002", NrsScreenings);
            SummaryBanner = $"公卫报卡 {PhCards.Count} · 院后随访计划 {FollowupPlans.Count} · UDI高值耗材 {UdiItems.Count} · NRS-2002营养筛查 {NrsScreenings.Count}";
        }
        catch (Exception ex) { _shell.ShowToast($"刷新 P1 数据失败：{ex.Message}", "Error"); }
    }

    private static async Task LoadPlainListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var list = await http.GetFromJsonAsync<List<T>>(url, JsonOpts);
        target.Clear();
        if (list != null) foreach (var it in list) target.Add(it);
    }

    private async Task TriggerPhCardAsync()
    {
        if (string.IsNullOrWhiteSpace(PhPatientName)) { _shell.ShowToast("请填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/public-health/evaluate-diagnosis", new
        {
            PatientId = $"PAT-{DateTime.UtcNow:HHmmss}", PatientName = PhPatientName, VisitOrEncounterId = $"VIS-{DateTime.UtcNow:HHmmss}", DeptName = "呼吸内科", IcdCode = PhIcdCode
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "诊断公卫触发评估已完成" : "评估失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task SubmitPhCardAsync()
    {
        if (SelectedPhCard == null) { _shell.ShowToast("请先选择报卡", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/public-health/cards/{SelectedPhCard.Id}/submit", new { ClinicalPayloadJson = "{\"confirmed\":true}" });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "报卡已提交" : "提交失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task AuditPhCardAsync()
    {
        if (SelectedPhCard == null) { _shell.ShowToast("请先选择报卡", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/public-health/cards/{SelectedPhCard.Id}/audit", new { Approved = true, AuditNote = "核对无误准予直报" });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "公卫审核通过" : "审核失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateFollowupAsync()
    {
        if (string.IsNullOrWhiteSpace(FuPatientName)) { _shell.ShowToast("请填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/followup/plans/generate-from-discharge", new
        {
            EncounterId = $"ENC-{DateTime.UtcNow:HHmmss}", PatientId = $"PAT-{DateTime.UtcNow:HHmmss}", PatientName = FuPatientName, SpecialtyTag = FuSpecialtyTag, FollowupAfterDays = 7, Channel = "PHONE"
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "7天随访计划已生成" : "生成失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CompleteFollowupAsync()
    {
        if (SelectedFollowup == null) { _shell.ShowToast("请先选择随访计划", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/followup/plans/{SelectedFollowup.Id}/complete", new { SymptomStatus = "恢复良好", MedicationAdherence = "规范服药", SatisfactionScore = 5, FollowupNote = "电话随访无不适" });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "随访已完结" : "登记失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ReceiveUdiAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/udi/inventory/receive", new
        {
            UdiFull = UdiFull, MaterialCode = "MAT-STENT-3018", MaterialName = UdiMaterialName, Spec = "3.0x18mm",
            SupplierName = UdiSupplier, IsConsignment = true, CostPrice = 6800m, UnitPrice = 7500m
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "GS1 UDI 高值耗材已入库" : "入库失败（可能重复或过期）", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ConsumeUdiAsync()
    {
        if (SelectedUdi == null) { _shell.ShowToast("请先选择在库 UDI 耗材", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/udi/inventory/consume", new { UdiFull = SelectedUdi.UdiFull, EncounterId = "ENC-OR-01", PatientId = "PAT-OR-01", SurgeryId = "SURG-01" });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "已扫码消耗并自动记入住院费用台账" : "消耗记账失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task GenerateStatementAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/udi/consignment/statements/generate", new { SupplierName = UdiSupplier, PeriodYm = DateTime.UtcNow.ToString("yyyy-MM") });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "代销月结对账单已生成" : "暂无待对账耗材", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task SubmitNrs2002Async()
    {
        if (string.IsNullOrWhiteSpace(NutPatientName)) { _shell.ShowToast("请填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nutrition/nrs2002", new
        {
            EncounterId = $"ENC-{DateTime.UtcNow:HHmmss}", PatientId = $"PAT-{DateTime.UtcNow:HHmmss}", PatientName = NutPatientName,
            Bmi = 18.2m, DiseaseSeverityScore = NutDiseaseScore, NutritionalImpairedScore = NutImpairedScore, AgeYears = NutAgeYears
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "NRS-2002 营养风险筛查已保存" : "保存失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateDietOrderAsync()
    {
        if (string.IsNullOrWhiteSpace(NutPatientName)) { _shell.ShowToast("请填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nutrition/diet-orders", new
        {
            EncounterId = $"ENC-{DateTime.UtcNow:HHmmss}", PatientId = $"PAT-{DateTime.UtcNow:HHmmss}", PatientName = NutPatientName,
            WardName = "内分泌病区", BedCode = "12床", DietType = "DIABETIC_LOW_SALT", KcalTarget = 1800, ProteinGrams = 70
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "治疗膳食配餐单已下达" : "下达失败", resp.IsSuccessStatusCode ? "Success" : "Error");
    }
}
