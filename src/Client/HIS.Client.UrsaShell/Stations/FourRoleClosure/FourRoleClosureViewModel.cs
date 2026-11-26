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

namespace HIS.Client.UrsaShell.Stations.FourRoleClosure;

public sealed class RefundReqRow
{
    public string Id { get; set; } = string.Empty;
    public string RequestNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string InitiatorDoctor { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class CriticalNoticeRow
{
    public string Id { get; set; } = string.Empty;
    public string NoticeNo { get; set; } = string.Empty;
    public string NoticeLevel { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string IssuedDoctor { get; set; } = string.Empty;
    public string? FamilySignerName { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class SkinTestRow
{
    public string Id { get; set; } = string.Empty;
    public string TestNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string? ReaderName { get; set; }
    public string? CheckerName { get; set; }
    public short AllergySynced { get; set; }
    public string AllergyDisplay => AllergySynced == 1 ? "⚠ 已写入过敏史" : "-";
}

public sealed class CopyRequestRow
{
    public string Id { get; set; } = string.Empty;
    public string CopyNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string DeliveryMode { get; set; } = string.Empty;
    public string? ExpressNo { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class FourRoleClosureViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _bannerText = "加载四角色闭环运营数据...";
    private string _patientServiceSummary = "患者满意度与投诉统计：待加载";

    private string _rfPatientName = string.Empty, _rfItemName = "胸部CT平扫", _rfReason = "患者暂缓检查申请退费";
    private RefundReqRow? _selectedRefund;

    private string _critPatientName = string.Empty, _critCondition = "急性呼吸衰竭伴血流动力学不稳定，随时有生命危险", _familySignerName = "张某某(配偶)";
    private CriticalNoticeRow? _selectedCritical;

    private string _stPatientName = string.Empty, _stDrugName = "注射用青霉素钠 80万U";
    private SkinTestRow? _selectedSkinTest;

    private string _copyPatientName = string.Empty, _copyMailAddress = "上海市浦东新区张江路88号", _copyExpressNo = "SF1029384756";
    private CopyRequestRow? _selectedCopyReq;

    public ObservableCollection<RefundReqRow> RefundRequests { get; } = new();
    public ObservableCollection<CriticalNoticeRow> CriticalNotices { get; } = new();
    public ObservableCollection<SkinTestRow> SkinTests { get; } = new();
    public ObservableCollection<CopyRequestRow> CopyRequests { get; } = new();

    public string BannerText { get => _bannerText; set => SetField(ref _bannerText, value); }
    public string PatientServiceSummary { get => _patientServiceSummary; set => SetField(ref _patientServiceSummary, value); }

    public string RfPatientName { get => _rfPatientName; set => SetField(ref _rfPatientName, value); }
    public string RfItemName { get => _rfItemName; set => SetField(ref _rfItemName, value); }
    public string RfReason { get => _rfReason; set => SetField(ref _rfReason, value); }
    public RefundReqRow? SelectedRefund { get => _selectedRefund; set => SetField(ref _selectedRefund, value); }

    public string CritPatientName { get => _critPatientName; set => SetField(ref _critPatientName, value); }
    public string CritCondition { get => _critCondition; set => SetField(ref _critCondition, value); }
    public string FamilySignerName { get => _familySignerName; set => SetField(ref _familySignerName, value); }
    public CriticalNoticeRow? SelectedCritical { get => _selectedCritical; set => SetField(ref _selectedCritical, value); }

    public string StPatientName { get => _stPatientName; set => SetField(ref _stPatientName, value); }
    public string StDrugName { get => _stDrugName; set => SetField(ref _stDrugName, value); }
    public SkinTestRow? SelectedSkinTest { get => _selectedSkinTest; set => SetField(ref _selectedSkinTest, value); }

    public string CopyPatientName { get => _copyPatientName; set => SetField(ref _copyPatientName, value); }
    public string CopyMailAddress { get => _copyMailAddress; set => SetField(ref _copyMailAddress, value); }
    public string CopyExpressNo { get => _copyExpressNo; set => SetField(ref _copyExpressNo, value); }
    public CopyRequestRow? SelectedCopyReq { get => _selectedCopyReq; set => SetField(ref _selectedCopyReq, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand CreateRefundReqCommand { get; }
    public ICommand ConfirmRefundExecCommand { get; }
    public ICommand ExecuteCashierRefundCommand { get; }
    public ICommand ReleaseExpiredCommand { get; }
    public ICommand IssueCriticalNoticeCommand { get; }
    public ICommand FamilySignCriticalCommand { get; }
    public ICommand ReportReturnCheckinCommand { get; }
    public ICommand InjectSkinTestCommand { get; }
    public ICommand JudgeSkinTestPosCommand { get; }
    public ICommand EmergencyStopInfusionCommand { get; }
    public ICommand PackUdOralCommand { get; }
    public ICommand SubmitCopyRequestCommand { get; }
    public ICommand ShipCopyRequestCommand { get; }
    public ICommand PreviewGuideSheetCommand { get; }

    public FourRoleClosureViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        CreateRefundReqCommand = new RelayCommand(async () => await CreateRefundReqAsync());
        ConfirmRefundExecCommand = new RelayCommand(async () => await ConfirmRefundExecAsync());
        ExecuteCashierRefundCommand = new RelayCommand(async () => await ExecuteCashierRefundAsync());
        ReleaseExpiredCommand = new RelayCommand(async () => await ReleaseExpiredAsync());
        IssueCriticalNoticeCommand = new RelayCommand(async () => await IssueCriticalNoticeAsync());
        FamilySignCriticalCommand = new RelayCommand(async () => await FamilySignCriticalAsync());
        ReportReturnCheckinCommand = new RelayCommand(async () => await ReportReturnCheckinAsync());
        InjectSkinTestCommand = new RelayCommand(async () => await InjectSkinTestAsync());
        JudgeSkinTestPosCommand = new RelayCommand(async () => await JudgeSkinTestPosAsync());
        EmergencyStopInfusionCommand = new RelayCommand(async () => await EmergencyStopInfusionAsync());
        PackUdOralCommand = new RelayCommand(async () => await PackUdOralAsync());
        SubmitCopyRequestCommand = new RelayCommand(async () => await SubmitCopyRequestAsync());
        ShipCopyRequestCommand = new RelayCommand(async () => await ShipCopyRequestAsync());
        PreviewGuideSheetCommand = new RelayCommand(async () => await PreviewGuideSheetAsync());
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadListAsync(http, "api/v4/registrar-closure/refund-requests", RefundRequests);
            await LoadListAsync(http, "api/v4/doctor-closure/critical-notices", CriticalNotices);
            await LoadListAsync(http, "api/v4/nurse-closure/skin-tests", SkinTests);
            await LoadListAsync(http, "api/v4/portal/copy-requests", CopyRequests);

            var srv = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("api/v4/service/summary", JsonOpts);
            if (srv?.Data.ValueKind == JsonValueKind.Object)
            {
                PatientServiceSummary = $"患者服务汇总（api/v4/service）：{srv.Data.GetRawText()}";
            }

            BannerText = $"三方退费池 {RefundRequests.Count} · 病危通知 {CriticalNotices.Count} · 皮试监测 {SkinTests.Count} · 病历复印 {CopyRequests.Count}";
        }
        catch (Exception ex) { _shell.ShowToast($"刷新四角色闭环数据失败：{ex.Message}", "Error"); }
    }

    private static async Task LoadListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<T>>>(url, JsonOpts);
        target.Clear();
        if (env?.Data != null) foreach (var it in env.Data) target.Add(it);
    }

    private async Task CreateRefundReqAsync()
    {
        if (string.IsNullOrWhiteSpace(RfPatientName)) { _shell.ShowToast("请填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/registrar-closure/refund-requests", new
        {
            PatientId = $"PAT-{DateTime.UtcNow:HHmmss}", PatientName = RfPatientName, VisitId = "VIS-01",
            ChargeLedgerId = "CL-01", ItemName = RfItemName, RefundAmount = 260m, Reason = RfReason
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已发起三方退费申请，待执行科室确认" : (env?.Message ?? "发起失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ConfirmRefundExecAsync()
    {
        if (SelectedRefund == null) { _shell.ShowToast("请先选择退费申请单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/registrar-closure/refund-requests/{SelectedRefund.Id}/exec-confirm", new { ConfirmerName = "影像科值班技师" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已确认未执行，解锁收费窗口退费" : (env?.Message ?? "确认失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ExecuteCashierRefundAsync()
    {
        if (SelectedRefund == null) { _shell.ShowToast("请先选择退费申请单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"api/v4/registrar-closure/refund-requests/{SelectedRefund.Id}/cashier-refund", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "现金退费核销完成" : (env?.Message ?? "退费被拦截"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ReleaseExpiredAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync("api/v4/registrar-closure/appointments/release-expired", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已完成超时未取号源释放与爽约黑名单核算" : (env?.Message ?? "扫描失败"), "Info");
    }

    private async Task IssueCriticalNoticeAsync()
    {
        if (string.IsNullOrWhiteSpace(CritPatientName)) { _shell.ShowToast("请填写住院患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/doctor-closure/critical-notices", new
        {
            EncounterId = $"ENC-{DateTime.UtcNow:HHmmss}", PatientId = "PAT-CRIT-01", PatientName = CritPatientName,
            DeptName = "重症医学科", NoticeLevel = "CRITICALLY_ILL", ClinicalCondition = CritCondition
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "病危通知书已签发并报备医务科" : (env?.Message ?? "签发失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task FamilySignCriticalAsync()
    {
        if (SelectedCritical == null) { _shell.ShowToast("请先选择病危通知书", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/doctor-closure/critical-notices/{SelectedCritical.Id}/family-sign", new
        {
            FamilySignerName = FamilySignerName, FamilyRelation = "配偶", FamilySignSvg = "<svg><path d='M0 0 L40 20'/></svg>"
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "家属电子签名已归档" : (env?.Message ?? "签字失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ReportReturnCheckinAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/doctor-closure/opd/report-return-checkin", new
        {
            VisitId = $"VIS-RET-{DateTime.UtcNow:HHmmss}", OpdDeptId = "DEPT-OPD-01", ClinicRoomId = "ROOM-01"
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "看报告回诊签到成功（已按 2:1 比例插队）" : (env?.Message ?? "回诊签到失败"), env?.Code is 200 or 0 ? "Success" : "Error");
    }

    private async Task InjectSkinTestAsync()
    {
        if (string.IsNullOrWhiteSpace(StPatientName)) { _shell.ShowToast("请填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nurse-closure/skin-tests/inject", new
        {
            EncounterOrVisitId = "ENC-01", PatientId = "PAT-ST-01", PatientName = StPatientName,
            DrugCode = "DRUG-PENICILLIN", DrugName = StDrugName, BatchNo = "B202611", WaitMinutes = 20
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "皮试已注射，20分钟倒计时启动" : (env?.Message ?? "登记失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task JudgeSkinTestPosAsync()
    {
        if (SelectedSkinTest == null) { _shell.ShowToast("请先选择皮试记录", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/nurse-closure/skin-tests/{SelectedSkinTest.Id}/judge", new
        {
            Result = "POSITIVE", ReaderName = "责任护士甲", CheckerName = "复核护士乙", Note = "局部红晕>1.5cm伴伪足"
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "皮试阳性双签完成并写入过敏史" : (env?.Message ?? "判定失败"), env?.Code is 200 or 0 ? "Warning" : "Error");
        await RefreshAllAsync();
    }

    private async Task EmergencyStopInfusionAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nurse-closure/infusion-reactions/emergency-stop", new
        {
            InfusionOrderId = $"INF-{DateTime.UtcNow:HHmmss}", PatientId = "PAT-INF-01", PatientName = string.IsNullOrWhiteSpace(StPatientName) ? "输液患者" : StPatientName,
            DrugName = StDrugName, BatchNo = "B202611", RemainingMl = 85m, Symptoms = "寒战、皮肤荨麻疹", RescueAction = "停液换生理盐水并通知医生", NotifiedDoctor = "值班医生"
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已停液封存并自动上报药学部 ADR" : (env?.Message ?? "处置失败"), env?.Code is 200 or 0 ? "Warning" : "Error");
    }

    private async Task PackUdOralAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nurse-closure/ud-packages/pack", new
        {
            EncounterId = "ENC-01", PatientId = "PAT-01", PatientName = string.IsNullOrWhiteSpace(StPatientName) ? "住院患者" : StPatientName,
            WardName = "心内一病区", BedCode = "08床", TakeDateInt = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd")), TakeSlot = "MORNING", DrugSummary = "阿司匹林 100mg + 美托洛尔 23.75mg"
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "晨间 UD 口服药分包已生成条码" : (env?.Message ?? "生成失败"), env?.Code is 200 or 0 ? "Success" : "Error");
    }

    private async Task SubmitCopyRequestAsync()
    {
        if (string.IsNullOrWhiteSpace(CopyPatientName)) { _shell.ShowToast("请填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/portal/copy-requests", new
        {
            PatientId = "PAT-COPY-01", PatientName = CopyPatientName, EncounterId = "ENC-01", Purpose = "商保理赔",
            CopiesCount = 2, DeliveryMode = "EXPRESS", MailAddress = CopyMailAddress, ContactPhone = "13800138000"
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "病历复印邮寄申请已提交" : (env?.Message ?? "提交失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ShipCopyRequestAsync()
    {
        if (SelectedCopyReq == null) { _shell.ShowToast("请先选择复印申请单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/portal/copy-requests/{SelectedCopyReq.Id}/review", new { Approved = true, ExpressNo = CopyExpressNo });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? $"已审核通过并录入快递单号 {CopyExpressNo}" : (env?.Message ?? "审核失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task PreviewGuideSheetAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("api/v4/portal/guide-sheet/VIS-DEMO?itemKeywords=%E7%94%9F%E5%8C%96,%E8%85%B9%E9%83%A8%E8%B6%85%E5%A3%B0,%E5%8F%96%E8%8D%AF", JsonOpts);
        if (env?.Data.ValueKind == JsonValueKind.Object && env.Data.TryGetProperty("fastingBanner", out var fb))
        {
            _shell.ShowToast(fb.GetString() ?? "指引单已生成", "Info");
        }
    }
}
