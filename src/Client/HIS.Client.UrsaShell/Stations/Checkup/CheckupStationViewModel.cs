using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Checkup;

public sealed class CheckupSummaryDto
{
    public int TotalOrders { get; set; }
    public int InProgressCount { get; set; }
    public int PendingSummaryCount { get; set; }
    public int CompletedCount { get; set; }
    public int TotalAbnormalFindings { get; set; }
}

public sealed class CheckupPackageOption
{
    public string Id { get; set; } = string.Empty;
    public string BundleCode { get; set; } = string.Empty;
    public string BundleName { get; set; } = string.Empty;
    public decimal PackagePrice { get; set; }
    public decimal SumItemPrice { get; set; }
    public int ItemCount { get; set; }
    public override string ToString() => $"{BundleName}（含{ItemCount}项 · ¥{PackagePrice:0.00}）";
}

public sealed class CheckupOrderRow
{
    public string Id { get; set; } = string.Empty;
    public string CheckupNo { get; set; } = string.Empty;
    public string GuideBarcode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string? IdCardMasked { get; set; }
    public string? PhoneMasked { get; set; }
    public string CheckupType { get; set; } = "PERSONAL";
    public string? CompanyName { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = "UNPAID";
    public string Status { get; set; } = "REGISTERED";
    public int TotalItemCount { get; set; }
    public int DoneItemCount { get; set; }
    public int AbnormalCount { get; set; }
    public string? SummaryDoctorName { get; set; }
    public string? OverallConclusion { get; set; }
    public string? HealthAdvice { get; set; }

    public string StatusDisplay => Status switch
    {
        "REGISTERED" => "已登记待检",
        "IN_PROGRESS" => $"体检中 ({DoneItemCount}/{TotalItemCount})",
        "PENDING_SUMMARY" => "待主检总检",
        "COMPLETED" => "已终审出报告",
        _ => Status
    };
    public string PayDisplay => PaymentStatus == "PAID" ? "已缴费" : "未缴费";
    public string TypeDisplay => CheckupType == "GROUP" ? $"团检·{CompanyName}" : "个人体检";
}

public sealed class CheckupOrderItemRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string? ResultValue { get; set; }
    public string? ReferenceRange { get; set; }
    public short IsAbnormal { get; set; }
    public string AbnormalFlag { get; set; } = "NORMAL";
    public string? ExamDoctorName { get; set; }
    public string Status { get; set; } = "PENDING";

    public string StatusText => Status switch
    {
        "DONE" => IsAbnormal == 1 ? $"已检 [异常:{AbnormalFlag}]" : "已检 [正常]",
        "WAIVED" => "已弃检/延期",
        _ => "待检"
    };
}

public sealed class ChkAppointmentRow
{
    public string Id { get; set; } = string.Empty;
    public string ApptNo { get; set; } = string.Empty;
    public int ApptDateInt { get; set; }
    public string TimeSlot { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string ExamCategory { get; set; } = "HEALTH";
    public string PackageName { get; set; } = string.Empty;
    public string Status { get; set; } = "BOOKED";
    public bool CanCheckIn => Status == "BOOKED";
}

public sealed class DeptQueueRow
{
    public string DeptName { get; set; } = string.Empty;
    public int WaitingCount { get; set; }
    public int DoneCount { get; set; }
    public int WaivedCount { get; set; }
    public string RecommendBadge => WaitingCount <= 2 ? "🟢 推荐优先前往" : WaitingCount <= 6 ? "🟡 排队适中" : "🔴 排队较多";
}

public sealed class ChkCompanyRow
{
    public string Id { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public decimal DiscountRate { get; set; }
    public override string ToString() => $"{CompanyName} ({DiscountRate * 10:0.#}折)";
}

public sealed class ChkGroupOrderRow
{
    public string Id { get; set; } = string.Empty;
    public string GroupNo { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public int PlannedCount { get; set; }
    public int RegisteredCount { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public override string ToString() => $"{CompanyName} · {GroupName} ({RegisteredCount}/{PlannedCount}人)";
}

public sealed class ChkFollowupRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string CheckupNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneMasked { get; set; }
    public string PositiveGrade { get; set; } = "B_IMPORTANT";
    public string FindingSummary { get; set; } = string.Empty;
    public string? SuggestDept { get; set; }
    public string FollowupStatus { get; set; } = "PENDING";
    public string? FollowupNote { get; set; }
    public string GradeDisplay => PositiveGrade == "A_CRITICAL" ? "🔴 A类重大阳性" : "🟠 B类重要异常";
}

public sealed class ChkOccRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string CheckupNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string OccCategory { get; set; } = "IN_JOB";
    public string HazardFactors { get; set; } = string.Empty;
    public int WorkYears { get; set; }
    public string OccConclusion { get; set; } = "NO_ABNORMAL";
    public string? HealthCertNo { get; set; }
    public int? HealthCertValidUntil { get; set; }
    public string ReportUploadStatus { get; set; } = "PENDING";
}

public sealed class HistoryCompareRow
{
    public string CheckupNo { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ResultValue { get; set; }
    public string? ReferenceRange { get; set; }
    public string AbnormalFlag { get; set; } = "NORMAL";
}

public sealed class CheckupStationViewModel : ClientModelBase, IPrintContextProvider
{
    private readonly ShellViewModel? _shell;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private int _selectedTabIndex;

    // KPI
    private int _totalOrders;
    private int _inProgressCount;
    private int _pendingSummaryCount;
    private int _completedCount;
    private int _totalAbnormalFindings;

    // Filter & selection
    private string _searchKeyword = string.Empty;
    private CheckupOrderRow? _selectedOrder;
    private CheckupOrderItemRow? _selectedOrderItem;

    // Tab 1: Appointment & Triage
    private string _apptName = string.Empty;
    private string _apptGender = "男";
    private int _apptAge = 32;
    private string _apptPhone = string.Empty;
    private string _apptExamCategory = "HEALTH";
    private string _apptTimeSlot = "08:00-10:00";
    private CheckupPackageOption? _apptPackage;

    // Tab 2: Personal Register + Group Management + Batch Import + Add-on
    private string _regFullName = string.Empty;
    private string _regGender = "男";
    private int _regAge = 32;
    private string _regIdCardNo = string.Empty;
    private string _regPhone = string.Empty;
    private string _regCheckupType = "PERSONAL";
    private string _regCompanyName = string.Empty;
    private CheckupPackageOption? _regSelectedPackage;
    private bool _regAutoPay = true;

    private string _newCompanyName = string.Empty;
    private string _newCompanyContact = string.Empty;
    private decimal _newCompanyDiscount = 0.85m;
    private ChkCompanyRow? _selectedCompany;
    private string _newGroupName = "全体员工标准组";
    private int _newGroupPlanned = 50;
    private ChkGroupOrderRow? _selectedGroup;
    private string _batchImportText = "张伟,男,34,13800138001\n李娜,女,29,13800138002";
    private string _groupReportSummary = string.Empty;

    private string _addonDept = "超声科";
    private string _addonItemName = string.Empty;
    private decimal _addonPrice = 120m;

    // Tab 3: Dept result entry + Waive + Questionnaire
    private string _entryResultValue = string.Empty;
    private string _entryRefRange = string.Empty;
    private string _entryAbnormalFlag = "NORMAL";
    private string _waiveReason = "孕期/个人自愿放弃";
    private string _qPastHistory = "否认高血压、糖尿病、冠心病史";
    private string _qFamilyHistory = "无特殊家族遗传病史";
    private string _qLifestyle = "不吸烟，偶饮酒";

    // Tab 4: Summary KB + Dual Review + History Compare
    private string _summaryConclusion = string.Empty;
    private string _summaryAdvice = string.Empty;

    // Tab 5: Followup & Refer to Pre-Admission
    private ChkFollowupRow? _selectedFollowup;
    private string _followupNote = "已电话告知受检者尽快携带报告至专科门诊复查";
    private string _referDeptName = "胸外科";

    // Tab 6: Occupational & Health Cert
    private string _occCategory = "IN_JOB";
    private string _occHazards = "生产性噪声、粉尘";
    private int _occWorkYears = 5;
    private string _occConclusion = "NO_ABNORMAL";
    private string _occDetail = "目前未见职业病及职业禁忌证异常。";
    private bool _occIssueHealthCert;
    private string _occNetReportPreview = string.Empty;

    public CheckupStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new RelayCommand(() => _ = LoadAllAsync());
        SearchCommand = new RelayCommand(() => _ = LoadOrdersAsync());

        CreateApptCommand = new RelayCommand(() => _ = CreateAppointmentAsync());
        CheckInApptCommand = new RelayCommand(p => _ = CheckInAppointmentAsync(p as ChkAppointmentRow));

        RegisterCommand = new RelayCommand(() => _ = RegisterAsync());
        ConfirmPayCommand = new RelayCommand(() => _ = ConfirmPayAsync());
        SaveCompanyCommand = new RelayCommand(() => _ = SaveCompanyAsync());
        CreateGroupCommand = new RelayCommand(() => _ = CreateGroupAsync());
        BatchImportCommand = new RelayCommand(() => _ = BatchImportAsync());
        LoadGroupReportCommand = new RelayCommand(() => _ = LoadGroupReportAsync());
        AddAddonItemCommand = new RelayCommand(() => _ = AddAddonItemAsync());

        SaveItemResultCommand = new RelayCommand(() => _ = SaveItemResultAsync());
        WaiveItemCommand = new RelayCommand(() => _ = WaiveItemAsync());
        SaveQuestionnaireCommand = new RelayCommand(() => _ = SaveQuestionnaireAsync());

        AutoMatchKbCommand = new RelayCommand(() => _ = AutoMatchKbAsync());
        CompleteSummaryCommand = new RelayCommand(() => _ = CompleteSummaryAsync());
        FinalReviewApproveCommand = new RelayCommand(() => _ = FinalReviewAsync(true));
        PrintPersonalReportCommand = new RelayCommand(() => _ = PrintFullPersonalReportAsync());

        MarkFollowupNotifiedCommand = new RelayCommand(() => _ = UpdateFollowupAsync("NOTIFIED"));
        ReferToPreAdmissionCommand = new RelayCommand(() => _ = ReferToPreAdmissionAsync());

        SaveOccupationalCommand = new RelayCommand(() => _ = SaveOccupationalAsync());
        ExportOccNetReportCommand = new RelayCommand(p => _ = ExportOccNetReportAsync(p as ChkOccRow));

        _ = LoadAllAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<CheckupPackageOption> Packages { get; } = new();
    public ObservableCollection<CheckupOrderRow> Orders { get; } = new();
    public ObservableCollection<CheckupOrderItemRow> SelectedOrderItems { get; } = new();
    public ObservableCollection<ChkAppointmentRow> Appointments { get; } = new();
    public ObservableCollection<DeptQueueRow> DeptQueues { get; } = new();
    public ObservableCollection<ChkCompanyRow> Companies { get; } = new();
    public ObservableCollection<ChkGroupOrderRow> GroupOrders { get; } = new();
    public ObservableCollection<HistoryCompareRow> HistoryComparisons { get; } = new();
    public ObservableCollection<ChkFollowupRow> Followups { get; } = new();
    public ObservableCollection<ChkOccRow> OccupationalRecords { get; } = new();

    public IReadOnlyList<string> GenderOptions { get; } = new[] { "男", "女" };
    public IReadOnlyList<string> CheckupTypeOptions { get; } = new[] { "PERSONAL", "GROUP" };
    public IReadOnlyList<string> ExamCategoryOptions { get; } = new[] { "HEALTH", "OCCUPATIONAL", "FOOD_HEALTH" };
    public IReadOnlyList<string> TimeSlotOptions { get; } = new[] { "07:30-09:00", "08:00-10:00", "09:30-11:30" };
    public IReadOnlyList<string> AbnormalFlagOptions { get; } = new[] { "NORMAL", "HIGH", "LOW", "POSITIVE" };
    public IReadOnlyList<string> OccCategoryOptions { get; } = new[] { "PRE_JOB", "IN_JOB", "POST_JOB", "FOOD_HEALTH" };
    public IReadOnlyList<string> OccConclusionOptions { get; } = new[] { "NO_ABNORMAL", "RECHECK", "SUSPECTED_OCC", "CONTRAINDICATION", "QUALIFIED_CERT" };

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand CreateApptCommand { get; }
    public ICommand CheckInApptCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand ConfirmPayCommand { get; }
    public ICommand SaveCompanyCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand BatchImportCommand { get; }
    public ICommand LoadGroupReportCommand { get; }
    public ICommand AddAddonItemCommand { get; }
    public ICommand SaveItemResultCommand { get; }
    public ICommand WaiveItemCommand { get; }
    public ICommand SaveQuestionnaireCommand { get; }
    public ICommand AutoMatchKbCommand { get; }
    public ICommand CompleteSummaryCommand { get; }
    public ICommand FinalReviewApproveCommand { get; }
    public ICommand PrintPersonalReportCommand { get; }
    public ICommand MarkFollowupNotifiedCommand { get; }
    public ICommand ReferToPreAdmissionCommand { get; }
    public ICommand SaveOccupationalCommand { get; }
    public ICommand ExportOccNetReportCommand { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetField(ref _selectedTabIndex, value); }

    public int TotalOrders { get => _totalOrders; set => SetField(ref _totalOrders, value); }
    public int InProgressCount { get => _inProgressCount; set => SetField(ref _inProgressCount, value); }
    public int PendingSummaryCount { get => _pendingSummaryCount; set => SetField(ref _pendingSummaryCount, value); }
    public int CompletedCount { get => _completedCount; set => SetField(ref _completedCount, value); }
    public int TotalAbnormalFindings { get => _totalAbnormalFindings; set => SetField(ref _totalAbnormalFindings, value); }
    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value); }

    public CheckupOrderRow? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetField(ref _selectedOrder, value))
            {
                OnPropertyChanged(nameof(HasSelectedOrder));
                SummaryConclusion = value?.OverallConclusion ?? string.Empty;
                SummaryAdvice = value?.HealthAdvice ?? string.Empty;
                if (value != null)
                {
                    _ = LoadOrderItemsAsync(value.Id);
                    _ = LoadHistoryCompareAsync(value.Id);
                }
            }
        }
    }
    public bool HasSelectedOrder => _selectedOrder != null;

    public CheckupOrderItemRow? SelectedOrderItem
    {
        get => _selectedOrderItem;
        set
        {
            if (SetField(ref _selectedOrderItem, value) && value != null)
            {
                EntryResultValue = value.ResultValue ?? string.Empty;
                EntryRefRange = value.ReferenceRange ?? string.Empty;
                EntryAbnormalFlag = string.IsNullOrWhiteSpace(value.AbnormalFlag) ? "NORMAL" : value.AbnormalFlag;
            }
        }
    }

    public string ApptName { get => _apptName; set => SetField(ref _apptName, value); }
    public string ApptGender { get => _apptGender; set => SetField(ref _apptGender, value); }
    public int ApptAge { get => _apptAge; set => SetField(ref _apptAge, value); }
    public string ApptPhone { get => _apptPhone; set => SetField(ref _apptPhone, value); }
    public string ApptExamCategory { get => _apptExamCategory; set => SetField(ref _apptExamCategory, value); }
    public string ApptTimeSlot { get => _apptTimeSlot; set => SetField(ref _apptTimeSlot, value); }
    public CheckupPackageOption? ApptPackage { get => _apptPackage; set => SetField(ref _apptPackage, value); }

    public string RegFullName { get => _regFullName; set => SetField(ref _regFullName, value); }
    public string RegGender { get => _regGender; set => SetField(ref _regGender, value); }
    public int RegAge { get => _regAge; set => SetField(ref _regAge, value); }
    public string RegIdCardNo { get => _regIdCardNo; set => SetField(ref _regIdCardNo, value); }
    public string RegPhone { get => _regPhone; set => SetField(ref _regPhone, value); }
    public string RegCheckupType { get => _regCheckupType; set => SetField(ref _regCheckupType, value); }
    public string RegCompanyName { get => _regCompanyName; set => SetField(ref _regCompanyName, value); }
    public CheckupPackageOption? RegSelectedPackage { get => _regSelectedPackage; set => SetField(ref _regSelectedPackage, value); }
    public bool RegAutoPay { get => _regAutoPay; set => SetField(ref _regAutoPay, value); }

    public string NewCompanyName { get => _newCompanyName; set => SetField(ref _newCompanyName, value); }
    public string NewCompanyContact { get => _newCompanyContact; set => SetField(ref _newCompanyContact, value); }
    public decimal NewCompanyDiscount { get => _newCompanyDiscount; set => SetField(ref _newCompanyDiscount, value); }
    public ChkCompanyRow? SelectedCompany { get => _selectedCompany; set => SetField(ref _selectedCompany, value); }
    public string NewGroupName { get => _newGroupName; set => SetField(ref _newGroupName, value); }
    public int NewGroupPlanned { get => _newGroupPlanned; set => SetField(ref _newGroupPlanned, value); }
    public ChkGroupOrderRow? SelectedGroup { get => _selectedGroup; set => SetField(ref _selectedGroup, value); }
    public string BatchImportText { get => _batchImportText; set => SetField(ref _batchImportText, value); }
    public string GroupReportSummary { get => _groupReportSummary; set => SetField(ref _groupReportSummary, value); }
    public string AddonDept { get => _addonDept; set => SetField(ref _addonDept, value); }
    public string AddonItemName { get => _addonItemName; set => SetField(ref _addonItemName, value); }
    public decimal AddonPrice { get => _addonPrice; set => SetField(ref _addonPrice, value); }

    public string EntryResultValue { get => _entryResultValue; set => SetField(ref _entryResultValue, value); }
    public string EntryRefRange { get => _entryRefRange; set => SetField(ref _entryRefRange, value); }
    public string EntryAbnormalFlag { get => _entryAbnormalFlag; set => SetField(ref _entryAbnormalFlag, value); }
    public string WaiveReason { get => _waiveReason; set => SetField(ref _waiveReason, value); }
    public string QPastHistory { get => _qPastHistory; set => SetField(ref _qPastHistory, value); }
    public string QFamilyHistory { get => _qFamilyHistory; set => SetField(ref _qFamilyHistory, value); }
    public string QLifestyle { get => _qLifestyle; set => SetField(ref _qLifestyle, value); }

    public string SummaryConclusion { get => _summaryConclusion; set => SetField(ref _summaryConclusion, value); }
    public string SummaryAdvice { get => _summaryAdvice; set => SetField(ref _summaryAdvice, value); }

    public ChkFollowupRow? SelectedFollowup
    {
        get => _selectedFollowup;
        set
        {
            if (SetField(ref _selectedFollowup, value) && value != null)
            {
                if (!string.IsNullOrWhiteSpace(value.SuggestDept)) ReferDeptName = value.SuggestDept;
            }
        }
    }
    public string FollowupNote { get => _followupNote; set => SetField(ref _followupNote, value); }
    public string ReferDeptName { get => _referDeptName; set => SetField(ref _referDeptName, value); }

    public string OccCategory { get => _occCategory; set => SetField(ref _occCategory, value); }
    public string OccHazards { get => _occHazards; set => SetField(ref _occHazards, value); }
    public int OccWorkYears { get => _occWorkYears; set => SetField(ref _occWorkYears, value); }
    public string OccConclusion { get => _occConclusion; set => SetField(ref _occConclusion, value); }
    public string OccDetail { get => _occDetail; set => SetField(ref _occDetail, value); }
    public bool OccIssueHealthCert { get => _occIssueHealthCert; set => SetField(ref _occIssueHealthCert, value); }
    public string OccNetReportPreview { get => _occNetReportPreview; set => SetField(ref _occNetReportPreview, value); }

    public Dictionary<string, string> BuildPrintContext(object? rowContext = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = rowContext as CheckupOrderRow ?? SelectedOrder;
        if (order != null)
        {
            ctx["checkup.checkupNo"] = order.CheckupNo;
            ctx["checkup.guideBarcode"] = order.GuideBarcode;
            ctx["checkup.fullName"] = order.FullName;
            ctx["checkup.gender"] = order.Gender;
            ctx["checkup.age"] = order.Age.ToString();
            ctx["checkup.companyName"] = string.IsNullOrWhiteSpace(order.CompanyName) ? "个人体检" : order.CompanyName;
            ctx["checkup.packageName"] = order.PackageName;
            ctx["checkup.totalAmount"] = order.TotalAmount.ToString("0.00");
            ctx["checkup.overallConclusion"] = order.OverallConclusion ?? string.Empty;
            ctx["patient.fullName"] = order.FullName;
        }
        return ctx;
    }

    public async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var sumRes = await http.GetFromJsonAsync<ClientApiEnvelope<CheckupSummaryDto>>("/api/v4/checkup/summary", JsonOpts);
            if (sumRes?.Data != null)
            {
                TotalOrders = sumRes.Data.TotalOrders;
                InProgressCount = sumRes.Data.InProgressCount;
                PendingSummaryCount = sumRes.Data.PendingSummaryCount;
                CompletedCount = sumRes.Data.CompletedCount;
                TotalAbnormalFindings = sumRes.Data.TotalAbnormalFindings;
            }

            var pkgRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<CheckupPackageOption>>>("/api/v4/checkup/packages", JsonOpts);
            Packages.Clear();
            if (pkgRes?.Data != null) foreach (var p in pkgRes.Data) Packages.Add(p);
            RegSelectedPackage ??= Packages.FirstOrDefault();
            ApptPackage ??= Packages.FirstOrDefault();

            var apptRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<ChkAppointmentRow>>>("/api/v4/checkup-pro/appointments", JsonOpts);
            Appointments.Clear();
            if (apptRes?.Data != null) foreach (var a in apptRes.Data) Appointments.Add(a);

            var qRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<DeptQueueRow>>>("/api/v4/checkup-pro/triage/queue", JsonOpts);
            DeptQueues.Clear();
            if (qRes?.Data != null) foreach (var q in qRes.Data) DeptQueues.Add(q);

            var cmpRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<ChkCompanyRow>>>("/api/v4/checkup-pro/companies", JsonOpts);
            Companies.Clear();
            if (cmpRes?.Data != null) foreach (var c in cmpRes.Data) Companies.Add(c);
            SelectedCompany ??= Companies.FirstOrDefault();

            var grpRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<ChkGroupOrderRow>>>("/api/v4/checkup-pro/groups", JsonOpts);
            GroupOrders.Clear();
            if (grpRes?.Data != null) foreach (var g in grpRes.Data) GroupOrders.Add(g);
            SelectedGroup ??= GroupOrders.FirstOrDefault();

            var fuRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<ChkFollowupRow>>>("/api/v4/checkup-pro/followups", JsonOpts);
            Followups.Clear();
            if (fuRes?.Data != null) foreach (var f in fuRes.Data) Followups.Add(f);
            SelectedFollowup ??= Followups.FirstOrDefault();

            var occRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<ChkOccRow>>>("/api/v4/checkup-pro/occupational", JsonOpts);
            OccupationalRecords.Clear();
            if (occRes?.Data != null) foreach (var o in occRes.Data) OccupationalRecords.Add(o);

            await LoadOrdersAsync();
            StatusMessage = $"已同步 {Orders.Count} 份体检单 · {Appointments.Count} 条预约 · {Followups.Count} 条重大阳性随访";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadOrdersAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        string url = "/api/v4/checkup/orders" + (string.IsNullOrWhiteSpace(SearchKeyword) ? "" : "?keyword=" + Uri.EscapeDataString(SearchKeyword.Trim()));
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<CheckupOrderRow>>>(url, JsonOpts);
        string? prevId = SelectedOrder?.Id;
        Orders.Clear();
        if (res?.Data != null) foreach (var o in res.Data) Orders.Add(o);
        SelectedOrder = (prevId != null ? Orders.FirstOrDefault(o => o.Id == prevId) : null) ?? Orders.FirstOrDefault();
    }

    private async Task LoadOrderItemsAsync(string orderId)
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<CheckupOrderItemRow>>>($"/api/v4/checkup/orders/{orderId}/items", JsonOpts);
        SelectedOrderItems.Clear();
        if (res?.Data != null) foreach (var it in res.Data) SelectedOrderItems.Add(it);
        SelectedOrderItem = SelectedOrderItems.FirstOrDefault();
    }

    private async Task LoadHistoryCompareAsync(string orderId)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<HistoryCompareRow>>>($"/api/v4/checkup-pro/orders/{orderId}/history-compare", JsonOpts);
            HistoryComparisons.Clear();
            if (res?.Data != null) foreach (var h in res.Data) HistoryComparisons.Add(h);
        }
        catch { }
    }

    private async Task CreateAppointmentAsync()
    {
        if (string.IsNullOrWhiteSpace(ApptName) || ApptPackage == null) { _shell?.ShowToast("请填写预约人姓名与套餐。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/checkup-pro/appointments", new
        {
            ApptDateInt = int.Parse(DateTime.Now.ToString("yyyyMMdd")),
            TimeSlot = ApptTimeSlot,
            FullName = ApptName.Trim(),
            Gender = ApptGender,
            Age = ApptAge,
            Phone = ApptPhone,
            ExamCategory = ApptExamCategory,
            PackageId = ApptPackage.Id,
            CompanyName = (string?)null,
            Remark = (string?)null
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast("体检预约创建成功。", "Success"); ApptName = string.Empty; await LoadAllAsync(); }
        else _shell?.ShowToast(env?.Message ?? "预约失败", "Error");
    }

    private async Task CheckInAppointmentAsync(ChkAppointmentRow? appt)
    {
        if (appt == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"/api/v4/checkup-pro/appointments/{appt.Id}/checkin", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell?.ShowToast(env?.Message ?? "签到完成", (env?.Code is 200 or 0) ? "Success" : "Error");
        await LoadAllAsync();
    }

    private async Task RegisterAsync()
    {
        if (string.IsNullOrWhiteSpace(RegFullName) || RegSelectedPackage == null) { _shell?.ShowToast("请填写体检人姓名并选择体检套餐。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/checkup/orders/register", new
        {
            FullName = RegFullName.Trim(),
            Gender = RegGender,
            Age = RegAge,
            IdCardNo = string.IsNullOrWhiteSpace(RegIdCardNo) ? null : RegIdCardNo.Trim(),
            Phone = string.IsNullOrWhiteSpace(RegPhone) ? null : RegPhone.Trim(),
            CheckupType = RegCheckupType,
            CompanyName = string.IsNullOrWhiteSpace(RegCompanyName) ? null : RegCompanyName.Trim(),
            PackageId = RegSelectedPackage.Id,
            AutoPay = RegAutoPay
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast(env.Message ?? "体检登记成功", "Success"); RegFullName = string.Empty; await LoadAllAsync(); }
        else _shell?.ShowToast(env?.Message ?? "登记失败", "Error");
    }

    private async Task ConfirmPayAsync()
    {
        if (SelectedOrder == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"/api/v4/checkup/orders/{SelectedOrder.Id}/pay", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast("体检缴费已确认。", "Success"); await LoadAllAsync(); }
    }

    private async Task SaveCompanyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCompanyName)) { _shell?.ShowToast("请输入体检单位名称。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/checkup-pro/companies", new
        {
            Id = (string?)null, CompanyCode = "", CompanyName = NewCompanyName.Trim(), CreditCode = "",
            ContactName = NewCompanyContact, ContactPhone = "", DiscountRate = NewCompanyDiscount, SettlementMode = "POST_SETTLE", Remark = ""
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast("体检单位档案已创建。", "Success"); NewCompanyName = string.Empty; await LoadAllAsync(); }
    }

    private async Task CreateGroupAsync()
    {
        if (SelectedCompany == null || RegSelectedPackage == null) { _shell?.ShowToast("请先选择体检单位和套餐。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/checkup-pro/groups", new
        {
            CompanyId = SelectedCompany.Id, GroupName = NewGroupName, GenderLimit = "ALL",
            PackageId = RegSelectedPackage.Id, PlannedCount = NewGroupPlanned, Remark = ""
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast("团检分组订单已创建。", "Success"); await LoadAllAsync(); }
    }

    private async Task BatchImportAsync()
    {
        if (SelectedGroup == null || string.IsNullOrWhiteSpace(BatchImportText)) { _shell?.ShowToast("请先选择团检分组并粘贴人员名单。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/checkup-pro/groups/{SelectedGroup.Id}/batch-import", new { MembersLines = BatchImportText });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell?.ShowToast(env?.Message ?? "批量导入完成", (env?.Code is 200 or 0) ? "Success" : "Error");
        await LoadAllAsync();
    }

    private async Task LoadGroupReportAsync()
    {
        if (SelectedGroup == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>($"/api/v4/checkup-pro/groups/{SelectedGroup.Id}/report", JsonOpts);
        if ((res?.Code is 200 or 0))
        {
            var d = res.Data;
            int reg = d.GetProperty("registeredCount").GetInt32();
            int done = d.GetProperty("completedCount").GetInt32();
            int abn = d.GetProperty("abnormalPeopleCount").GetInt32();
            double rate = d.GetProperty("abnormalRatePct").GetDouble();
            GroupReportSummary = $"【{SelectedGroup.CompanyName} · {SelectedGroup.GroupName} 团检健康汇总】已登记 {reg} 人，已出报告 {done} 人，检出异常 {abn} 人（异常检出率 {rate:0.0}%），团检应收总额 ¥{SelectedGroup.TotalAmount:0.00}。";
            _shell?.ShowToast("已生成单位团体健康汇总分析报告。", "Success");
        }
    }

    private async Task AddAddonItemAsync()
    {
        if (SelectedOrder == null || string.IsNullOrWhiteSpace(AddonItemName)) { _shell?.ShowToast("请先选择体检单并填写加项名称。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/checkup-pro/orders/{SelectedOrder.Id}/add-item", new
        {
            DeptName = AddonDept, ItemCode = "", ItemName = AddonItemName.Trim(), UnitPrice = AddonPrice
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast(env.Message ?? "加项成功", "Success"); AddonItemName = string.Empty; await LoadAllAsync(); }
    }

    private async Task SaveItemResultAsync()
    {
        if (SelectedOrderItem == null) { _shell?.ShowToast("请先选择分科项目。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"/api/v4/checkup/items/{SelectedOrderItem.Id}/result", new
        {
            ResultValue = EntryResultValue, ReferenceRange = EntryRefRange, AbnormalFlag = EntryAbnormalFlag
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast($"[{SelectedOrderItem.ItemName}] 结果已保存。", "Success"); await LoadAllAsync(); }
    }

    private async Task WaiveItemAsync()
    {
        if (SelectedOrderItem == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"/api/v4/checkup-pro/items/{SelectedOrderItem.Id}/waive", new { Reason = WaiveReason });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast("已标记弃检/延期。", "Warning"); await LoadAllAsync(); }
    }

    private async Task SaveQuestionnaireAsync()
    {
        if (SelectedOrder == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"/api/v4/checkup-pro/orders/{SelectedOrder.Id}/questionnaire", new
        {
            PastHistory = QPastHistory, FamilyHistory = QFamilyHistory, SmokingDrinking = QLifestyle, AllergyHistory = "", ChiefComplaint = ""
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) _shell?.ShowToast("医生问诊健康问卷已保存。", "Success");
    }

    private async Task AutoMatchKbAsync()
    {
        if (SelectedOrder == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"/api/v4/checkup-pro/orders/{SelectedOrder.Id}/auto-match-kb", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            SummaryConclusion = env.Data.GetProperty("conclusion").GetString() ?? string.Empty;
            SummaryAdvice = env.Data.GetProperty("advice").GetString() ?? string.Empty;
            _shell?.ShowToast(env.Message ?? "已完成知识库智能匹配", "Success");
            await LoadAllAsync();
        }
    }

    private async Task CompleteSummaryAsync()
    {
        if (SelectedOrder == null || string.IsNullOrWhiteSpace(SummaryConclusion)) { _shell?.ShowToast("请先填写总检结论。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/checkup/orders/{SelectedOrder.Id}/complete-summary", new
        {
            OverallConclusion = SummaryConclusion, HealthAdvice = SummaryAdvice
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast("主检初审结论已提交保存。", "Success"); await LoadAllAsync(); }
    }

    private async Task FinalReviewAsync(bool approved)
    {
        if (SelectedOrder == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/checkup-pro/orders/{SelectedOrder.Id}/final-review", new { Approved = approved, ReviewNote = "" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast(env.Message ?? "终审完成", "Success"); await LoadAllAsync(); }
    }

    private async Task PrintFullPersonalReportAsync()
    {
        if (SelectedOrder == null) return;
        var o = SelectedOrder;
        string bc = Code128BarcodeRenderer.RenderSvg(o.GuideBarcode, 52, 12, true);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/><title>健康体检报告 - " + WebUtility.HtmlEncode(o.FullName) + "</title>");
        sb.AppendLine("<style>@page{size:A4;margin:12mm;}body{font-family:'Microsoft YaHei',sans-serif;color:#111;line-height:1.5;}h1{margin:0;font-size:18pt;}.hdr{display:flex;justify-content:space-between;border-bottom:2px solid #165DFF;padding-bottom:8px;margin-bottom:12px;}table{width:100%;border-collapse:collapse;margin:10px 0;font-size:9.5pt;}th,td{border:1px solid #cbd5e1;padding:6px 8px;text-align:left;}th{background:#f1f5f9;}.abn{color:#dc2626;font-weight:bold;}.box{border:1px solid #94a3b8;padding:10px;border-radius:4px;margin:10px 0;white-space:pre-wrap;}</style></head><body>");
        sb.AppendLine($"<div class=\"hdr\"><div><h1>GHIS 示范医院 健康体检完整报告</h1><div>体检号：{WebUtility.HtmlEncode(o.CheckupNo)} · 套餐：{WebUtility.HtmlEncode(o.PackageName)}</div></div><div>{bc}</div></div>");
        sb.AppendLine($"<table><tr><th>姓名</th><td>{WebUtility.HtmlEncode(o.FullName)}</td><th>性别/年龄</th><td>{WebUtility.HtmlEncode(o.Gender)} / {o.Age}岁</td><th>体检类型</th><td>{WebUtility.HtmlEncode(o.TypeDisplay)}</td></tr></table>");
        sb.AppendLine("<h3>一、主检总检结论</h3><div class=\"box\">" + WebUtility.HtmlEncode(SummaryConclusion) + "</div>");
        sb.AppendLine("<h3>二、健康干预与随访建议</h3><div class=\"box\">" + WebUtility.HtmlEncode(SummaryAdvice) + "</div>");
        sb.AppendLine("<h3>三、各分科检查结果明细</h3><table><tr><th>科室</th><th>检查项目</th><th>检查结果</th><th>参考范围</th><th>状态</th></tr>");
        foreach (var it in SelectedOrderItems)
        {
            string cls = it.IsAbnormal == 1 ? " class=\"abn\"" : "";
            sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(it.DeptName)}</td><td>{WebUtility.HtmlEncode(it.ItemName)}</td><td{cls}>{WebUtility.HtmlEncode(it.ResultValue ?? "-")}</td><td>{WebUtility.HtmlEncode(it.ReferenceRange ?? "-")}</td><td{cls}>{WebUtility.HtmlEncode(it.StatusText)}</td></tr>");
        }
        sb.AppendLine($"</table><p style=\"margin-top:20px;font-size:9pt;color:#475569;\">主检医师：{WebUtility.HtmlEncode(o.SummaryDoctorName ?? "-")} · 打印日期：{DateTime.Now:yyyy-MM-dd HH:mm}</p><script>window.onload=function(){{setTimeout(function(){{window.print();}},200);}};</script></body></html>");

        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GHIS_PrintSpool");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"PEIS_RPT_{o.CheckupNo}.html");
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        _shell?.ShowToast("已生成完整版个人体检报告并调起打印。", "Success");
    }

    private async Task UpdateFollowupAsync(string status)
    {
        if (SelectedFollowup == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"/api/v4/checkup-pro/followups/{SelectedFollowup.Id}", new { FollowupStatus = status, FollowupNote = FollowupNote });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast("随访状态已更新。", "Success"); await LoadAllAsync(); }
    }

    private async Task ReferToPreAdmissionAsync()
    {
        if (SelectedFollowup == null) { _shell?.ShowToast("请先选择一条重大阳性随访记录。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/checkup-pro/followups/{SelectedFollowup.Id}/refer-pre-admission", new
        {
            TargetDeptName = ReferDeptName, PrimaryDiagName = SelectedFollowup.FindingSummary, PlannedSurgeryName = ""
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast(env.Message ?? "已一键转入预住院", "Success"); await LoadAllAsync(); }
        else _shell?.ShowToast(env?.Message ?? "转诊失败", "Error");
    }

    private async Task SaveOccupationalAsync()
    {
        if (SelectedOrder == null) { _shell?.ShowToast("请先在左侧选择一份体检单。", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/checkup-pro/occupational", new
        {
            OrderId = SelectedOrder.Id, OccCategory = OccCategory, HazardFactors = OccHazards, WorkYears = OccWorkYears,
            OccConclusion = OccConclusion, ConclusionDetail = OccDetail, IssueHealthCert = OccIssueHealthCert
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0)) { _shell?.ShowToast(env.Message ?? "职业/从业体检已评定", "Success"); await LoadAllAsync(); }
        else _shell?.ShowToast(env?.Message ?? "保存失败", "Error");
    }

    private async Task ExportOccNetReportAsync(ChkOccRow? row)
    {
        if (row == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"/api/v4/checkup-pro/occupational/{row.Id}/export-report", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            OccNetReportPreview = env.Data.GetProperty("netReportJson").GetString() ?? string.Empty;
            _shell?.ShowToast("已生成省职业病监测平台网报数据包并标记上报完成。", "Success");
            await LoadAllAsync();
        }
    }
}
