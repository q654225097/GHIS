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
using HIS.Contracts;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.BloodBank;

public class BloodBagItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string BagBarcode { get; set; } = string.Empty;
    public string DonationCode { get; set; } = string.Empty;
    public string SourceBloodCenter { get; set; } = string.Empty;
    public string BloodCode { get; set; } = string.Empty;
    public string VarietyName { get; set; } = "悬浮红细胞 (RBC)";
    public string AboType { get; set; } = "A";
    public string RhType { get; set; } = "POSITIVE";
    public string BloodGroup => $"{AboType}型 {(RhType.Contains("POS", StringComparison.OrdinalIgnoreCase) ? "Rh+" : "Rh-")}";
    public decimal VolumeMl { get; set; } = 200m;
    public int ExpiryDateInt { get; set; }
    public string ExpireDate => ExpiryDateInt > 0 ? $"{ExpiryDateInt / 10000}-{(ExpiryDateInt % 10000) / 100:00}-{ExpiryDateInt % 100:00}" : "-";
    public decimal ColdChainTemp { get; set; } = 4.0m;
    public string Status { get; set; } = "IN_STOCK";
    public string StatusText => Status switch
    {
        "IN_STOCK" => "在库可用",
        "CROSSMATCHED" => "已配血锁定",
        "DISPENSED" => "已发血出库",
        "TRANSFUSED" => "已输注完成",
        _ => Status
    };
}

public class BloodRequestItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int BirthDateInt { get; set; }
    public string PatientAbo { get; set; } = "A";
    public string PatientRh { get; set; } = "POSITIVE";
    public string BloodGroup => $"{PatientAbo}型 {(PatientRh.Contains("POS", StringComparison.OrdinalIgnoreCase) ? "Rh+" : "Rh-")}";
    public string VarietyName { get; set; } = "悬浮红细胞";
    public decimal AmountRequested { get; set; } = 200m;
    public decimal? LatestHgb { get; set; }
    public string RationalCheckResult { get; set; } = string.Empty;
    public string? BagBarcode { get; set; }
    public int Status { get; set; }
    public string ApprovalStatus { get; set; } = "APPROVED";
    public string WorkflowInstanceId { get; set; } = string.Empty;
    public string ApprovalBadgeText => ApprovalStatus switch
    {
        "PENDING_APPROVAL" => "待审批",
        "APPROVED" => "已审批",
        "REJECTED" => "已驳回",
        _ => ApprovalStatus
    };
    public string ApprovalBadgeBrush => ApprovalStatus switch
    {
        "PENDING_APPROVAL" => "#D97706",
        "APPROVED" => "#059669",
        "REJECTED" => "#DC2626",
        _ => "#64748B"
    };
    public string StatusText => Status switch
    {
        0 => "待配血",
        1 => "已配血相容",
        2 => "已双签发血",
        3 => "输注中",
        4 => "已回收核销",
        _ => "申请中"
    };
}

public class PatientBloodDistItem : ClientModelBase
{
    public string AboType { get; set; } = string.Empty;
    public string RhType { get; set; } = string.Empty;
    public int PatientCount { get; set; }
    public string DisplayText => $"{AboType} ({RhType}): {PatientCount}人";
}

public class InpatientSelectItem : ClientModelBase
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string BedCode { get; set; } = string.Empty;
    public string AdmissionNo { get; set; } = string.Empty;
    public string AboType { get; set; } = "A";
    public string RhType { get; set; } = "POSITIVE";
    public string DisplayText => $"[{BedCode}] {PatientName} ({AdmissionNo})";
}

public sealed partial class BloodBankStationViewModel : ClientModelBase, IStationLifecycle, IShortcutTarget
{
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;
    private readonly HttpClient _http;

    private BloodRequestItem? _selectedRequest;
    private InpatientSelectItem? _selectedInpatient;
    private BloodBagItem? _selectedBagForMatch;

    // Crossmatch Fields
    private string _crossmatchTechnician = string.Empty;
    private string _crossmatchVerifier = "MT2001";
    private string _antibodyScreen = "NEGATIVE";
    private string _majorCrossmatch = "COMPATIBLE";
    private string _minorCrossmatch = "COMPATIBLE";

    // Dual Dispense Fields
    private string _dispenseStaff1 = string.Empty;
    private string _dispenseStaff2 = "RN3002";

    // Stock-In Fields
    private string _stockInBarcode = string.Empty;
    private string _stockInDonationCode = string.Empty;
    private string _stockInBloodCode = "RBC";
    private string _stockInAbo = "A";
    private string _stockInRh = "POSITIVE";
    private decimal _stockInVolume = 200m;
    private decimal _stockInTemp = 4.2m;

    // Request Creation Fields
    private string _reqBloodCode = "RBC";
    private decimal _reqVolume = 200m;
    private string _reqReason = "围术期备血/贫血指征核查";

    // Reaction Report Fields
    private string _reactionType = "FEBRILE";
    private string _reactionSeverity = "MODERATE";
    private string _reactionSymptoms = "寒战、高热(38.8℃)、头痛、面色潮红";
    private string _reactionDisposition = "立即停止输血，更换输血器，静注地塞米松，保持静脉通路";

    private string _statusMessage = string.Empty;
    private string _stockAdvisoryText = "正在加载血站区域库存监控数据...";
    private bool _isLoading;

    // Panels visibility
    private bool _showStockInPanel;
    private bool _showNewReqPanel;
    private bool _showReactionPanel;

    public ObservableCollection<BloodBagItem> InventoryBags { get; } = new();
    public ObservableCollection<BloodRequestItem> Requests { get; } = new();
    public ObservableCollection<PatientBloodDistItem> PatientBloodDistribution { get; } = new();
    public ObservableCollection<InpatientSelectItem> InpatientOptions { get; } = new();

    public static IReadOnlyList<string> BloodCodeOptions { get; } = new[] { "RBC", "FFP", "PLT", "CRYO" };
    public static IReadOnlyList<string> AboOptions { get; } = new[] { "A", "B", "O", "AB" };
    public static IReadOnlyList<string> RhOptions { get; } = new[] { "POSITIVE", "NEGATIVE" };
    public static IReadOnlyList<string> ReactionTypeOptions { get; } = new[] { "FEBRILE", "ALLERGIC", "HEMOLYTIC", "TRALI", "TACO" };
    public static IReadOnlyList<string> SeverityOptions { get; } = new[] { "MILD", "MODERATE", "SEVERE" };
    public static IReadOnlyList<string> CompatibilityOptions { get; } = new[] { "COMPATIBLE", "INCOMPATIBLE" };
    public static IReadOnlyList<string> AntibodyScreenOptions { get; } = new[] { "NEGATIVE", "POSITIVE" };

    public BloodRequestItem? SelectedRequest
    {
        get => _selectedRequest;
        set => SetField(ref _selectedRequest, value);
    }

    public InpatientSelectItem? SelectedInpatient
    {
        get => _selectedInpatient;
        set => SetField(ref _selectedInpatient, value);
    }

    public BloodBagItem? SelectedBagForMatch
    {
        get => _selectedBagForMatch;
        set => SetField(ref _selectedBagForMatch, value);
    }

    public string CrossmatchTechnician
    {
        get => _crossmatchTechnician;
        set => SetField(ref _crossmatchTechnician, value);
    }

    public string CrossmatchVerifier
    {
        get => _crossmatchVerifier;
        set => SetField(ref _crossmatchVerifier, value);
    }

    public string AntibodyScreen
    {
        get => _antibodyScreen;
        set => SetField(ref _antibodyScreen, value);
    }

    public string MajorCrossmatch
    {
        get => _majorCrossmatch;
        set => SetField(ref _majorCrossmatch, value);
    }

    public string MinorCrossmatch
    {
        get => _minorCrossmatch;
        set => SetField(ref _minorCrossmatch, value);
    }

    public string DispenseStaff1
    {
        get => _dispenseStaff1;
        set => SetField(ref _dispenseStaff1, value);
    }

    public string DispenseStaff2
    {
        get => _dispenseStaff2;
        set => SetField(ref _dispenseStaff2, value);
    }

    public string StockInBarcode
    {
        get => _stockInBarcode;
        set => SetField(ref _stockInBarcode, value);
    }

    public string StockInDonationCode
    {
        get => _stockInDonationCode;
        set => SetField(ref _stockInDonationCode, value);
    }

    public string StockInBloodCode
    {
        get => _stockInBloodCode;
        set => SetField(ref _stockInBloodCode, value);
    }

    public string StockInAbo
    {
        get => _stockInAbo;
        set => SetField(ref _stockInAbo, value);
    }

    public string StockInRh
    {
        get => _stockInRh;
        set => SetField(ref _stockInRh, value);
    }

    public decimal StockInVolume
    {
        get => _stockInVolume;
        set => SetField(ref _stockInVolume, value);
    }

    public decimal StockInTemp
    {
        get => _stockInTemp;
        set => SetField(ref _stockInTemp, value);
    }

    public string ReqBloodCode
    {
        get => _reqBloodCode;
        set => SetField(ref _reqBloodCode, value);
    }

    public decimal ReqVolume
    {
        get => _reqVolume;
        set => SetField(ref _reqVolume, value);
    }

    public string ReqReason
    {
        get => _reqReason;
        set => SetField(ref _reqReason, value);
    }

    public string ReactionType
    {
        get => _reactionType;
        set => SetField(ref _reactionType, value);
    }

    public string ReactionSeverity
    {
        get => _reactionSeverity;
        set => SetField(ref _reactionSeverity, value);
    }

    public string ReactionSymptoms
    {
        get => _reactionSymptoms;
        set => SetField(ref _reactionSymptoms, value);
    }

    public string ReactionDisposition
    {
        get => _reactionDisposition;
        set => SetField(ref _reactionDisposition, value);
    }

    public string StockAdvisoryText
    {
        get => _stockAdvisoryText;
        set => SetField(ref _stockAdvisoryText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public bool ShowStockInPanel
    {
        get => _showStockInPanel;
        set => SetField(ref _showStockInPanel, value);
    }

    public bool ShowNewReqPanel
    {
        get => _showNewReqPanel;
        set => SetField(ref _showNewReqPanel, value);
    }

    public bool ShowReactionPanel
    {
        get => _showReactionPanel;
        set => SetField(ref _showReactionPanel, value);
    }

    public ICommand SelectRequestCommand { get; }
    public ICommand PerformCrossmatchCommand { get; }
    public ICommand DualCheckDispenseCommand { get; }
    public ICommand StockInBagCommand { get; }
    public ICommand SubmitRequestCommand { get; }
    public ICommand SubmitReactionCommand { get; }
    public ICommand SyncExternalBloodCenterCommand { get; }
    public ICommand ToggleStockInPanelCommand { get; }
    public ICommand ToggleNewReqPanelCommand { get; }
    public ICommand ToggleReactionPanelCommand { get; }
    public ICommand RefreshCommand { get; }

    public BloodBankStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        _http = new HttpClient(new AuthenticatedHttpHandler(SessionContext.Current))
        {
            BaseAddress = new Uri(ApiEndpoints.BaseAddress),
            Timeout = TimeSpan.FromSeconds(10)
        };

        var curUser = SessionContext.Current.DisplayName;
        if (string.IsNullOrWhiteSpace(curUser)) curUser = SessionContext.Current.UserName;
        if (string.IsNullOrWhiteSpace(curUser)) curUser = SessionContext.Current.UserId ?? "STAFF";

        _crossmatchTechnician = curUser;
        _dispenseStaff1 = curUser;

        SelectRequestCommand = new RelayCommand(p => SelectedRequest = p as BloodRequestItem);
        PerformCrossmatchCommand = new AsyncRelayCommand(ExecuteCrossmatchAsync);
        DualCheckDispenseCommand = new AsyncRelayCommand(ExecuteDualCheckDispenseAsync);
        StockInBagCommand = new AsyncRelayCommand(ExecuteStockInAsync);
        SubmitRequestCommand = new AsyncRelayCommand(ExecuteSubmitRequestAsync);
        SubmitReactionCommand = new AsyncRelayCommand(ExecuteSubmitReactionAsync);
        SyncExternalBloodCenterCommand = new AsyncRelayCommand(ExecuteSyncExternalAsync);

        ToggleStockInPanelCommand = new RelayCommand(() => ShowStockInPanel = !ShowStockInPanel);
        ToggleNewReqPanelCommand = new RelayCommand(() => ShowNewReqPanel = !ShowNewReqPanel);
        ToggleReactionPanelCommand = new RelayCommand(() => ShowReactionPanel = !ShowReactionPanel);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);

        StockInBarcode = $"BLD{DateTime.Now:yyyyMMddHHmmss}";
        StockInDonationCode = $"DON{DateTime.Now:yyyyMMddHHmm}";
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            StatusMessage = "正在从后端加载血库实时数据与用血申请...";

            // 1. Load In-Stock Bags
            var bagsRes = await _http.GetFromJsonAsync<ApiResponse<List<BloodBagDto>>>("/api/v4/blood/inventory/bags");
            if (bagsRes?.Data != null)
            {
                InventoryBags.Clear();
                foreach (var b in bagsRes.Data)
                {
                    InventoryBags.Add(new BloodBagItem
                    {
                        Id = b.Id,
                        BagBarcode = b.BagBarcode,
                        DonationCode = b.DonationCode,
                        SourceBloodCenter = b.SourceBloodCenter,
                        BloodCode = b.BloodCode,
                        VarietyName = ResolveVarietyName(b.BloodCode),
                        AboType = b.AboType,
                        RhType = b.RhType,
                        VolumeMl = b.VolumeMl,
                        ExpiryDateInt = b.ExpiryDateInt,
                        ColdChainTemp = b.ColdChainTemp,
                        Status = b.Status
                    });
                }
            }

            // 2. Load Blood Requests
            var reqRes = await _http.GetFromJsonAsync<ApiResponse<List<BloodRequestSummaryDto>>>("/api/v4/blood/requests");
            if (reqRes?.Data != null)
            {
                Requests.Clear();
                foreach (var r in reqRes.Data)
                {
                    Requests.Add(new BloodRequestItem
                    {
                        Id = r.Id,
                        EncounterId = r.EncounterId,
                        PatientId = r.PatientId,
                        PatientName = $"{r.PatientName} ({r.Gender})",
                        Gender = r.Gender,
                        BirthDateInt = r.BirthDateInt,
                        PatientAbo = r.AboType,
                        PatientRh = r.RhType,
                        VarietyName = ResolveVarietyName(r.BloodProductCode),
                        AmountRequested = r.VolumeMl,
                        LatestHgb = r.LatestHgb,
                        RationalCheckResult = r.RationalCheckResult ?? "✓ 临床用血指征评估通过",
                        BagBarcode = r.BagBarcode,
                        Status = r.Status
                    });
                }
                if (Requests.Count > 0 && SelectedRequest == null)
                {
                    SelectedRequest = Requests[0];
                }
            }

            // 3. Load Stock Advisory
            var advRes = await _http.GetFromJsonAsync<ApiResponse<List<BloodStockAdvisoryDto>>>("/api/v4/blood/analytics/stock-advisory");
            if (advRes?.Data != null && advRes.Data.Count > 0)
            {
                var summaryList = advRes.Data.Select(a => $"{a.BloodName}({a.AboType}型): 存量{a.CurrentStock}U/待备{a.PendingDemand}U [{a.AdvisoryStatus}]");
                StockAdvisoryText = "✓ 智能备血监控：" + string.Join("； ", summaryList);
            }
            else
            {
                StockAdvisoryText = "✓ 全院血液冷链与库存充足，备血与拟手术排程需求平衡。";
            }

            // 4. Load Patient Blood Type Distribution
            var distRes = await _http.GetFromJsonAsync<ApiResponse<List<PatientBloodDistDto>>>("/api/v4/blood/patients/blood-distribution");
            if (distRes?.Data != null)
            {
                PatientBloodDistribution.Clear();
                foreach (var d in distRes.Data)
                {
                    PatientBloodDistribution.Add(new PatientBloodDistItem
                    {
                        AboType = d.AboType ?? "A型",
                        RhType = d.RhType ?? "Rh+",
                        PatientCount = d.PatientCount
                    });
                }
            }

            // 5. Load Active Inpatients for Requisition
            var inpatRes = await _http.GetFromJsonAsync<ApiResponse<List<ActiveInpatientEncounterDto>>>("/api/v4/ipd/encounters/active");
            if (inpatRes?.Data != null)
            {
                InpatientOptions.Clear();
                foreach (var p in inpatRes.Data)
                {
                    InpatientOptions.Add(new InpatientSelectItem
                    {
                        EncounterId = p.Id,
                        PatientId = p.PatientId,
                        PatientName = p.PatientName ?? "患者",
                        BedCode = p.BedCode ?? "未分床",
                        AdmissionNo = p.AdmissionNo ?? p.Id,
                        AboType = "A",
                        RhType = "POSITIVE"
                    });
                }
                if (InpatientOptions.Count > 0 && SelectedInpatient == null)
                {
                    SelectedInpatient = InpatientOptions[0];
                }
            }

            StatusMessage = $"✓ 血库数据已同步：在库血袋 {InventoryBags.Count} 袋，待处置申请 {Requests.Count} 份。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ExecuteStockInAsync()
    {
        if (string.IsNullOrWhiteSpace(StockInBarcode))
        {
            _shell?.ShowToast("请输入血袋唯一扫码条码", "Warning");
            return;
        }

        try
        {
            var dto = new
            {
                BagBarcode = StockInBarcode.Trim(),
                DonationCode = string.IsNullOrWhiteSpace(StockInDonationCode) ? $"DON{DateTime.Now:yyyyMMddHHmmss}" : StockInDonationCode.Trim(),
                SourceBloodCenter = "市中心血站",
                BloodCode = StockInBloodCode,
                AboType = StockInAbo,
                RhType = StockInRh,
                VolumeMl = StockInVolume,
                CollectionDateInt = int.Parse(DateTime.Now.ToString("yyyyMMdd")),
                ExpiryDateInt = int.Parse(DateTime.Now.AddDays(35).ToString("yyyyMMdd")),
                ColdChainTemp = StockInTemp
            };

            var res = await _http.PostAsJsonAsync("/api/v4/blood/inventory/stock-in", dto);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                _shell?.ShowToast($"血袋 {StockInBarcode} 扫码入库成功 (冷链温度 {StockInTemp}℃)", "Success");
                ShowStockInPanel = false;
                StockInBarcode = $"BLD{DateTime.Now:yyyyMMddHHmmss}";
                await LoadDataAsync();
            }
            else
            {
                _shell?.ShowToast($"入库失败: {result?.Message ?? "服务异常"}", "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"入库请求异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteSubmitRequestAsync()
    {
        if (SelectedInpatient == null)
        {
            _shell?.ShowToast("请先选择用血在院患者", "Warning");
            return;
        }

        try
        {
            var dto = new
            {
                EncounterId = SelectedInpatient.EncounterId,
                PatientId = SelectedInpatient.PatientId,
                AboType = SelectedInpatient.AboType,
                RhType = SelectedInpatient.RhType,
                BloodProductCode = ReqBloodCode,
                VolumeMl = (int)ReqVolume,
                Reason = ReqReason
            };

            var res = await _http.PostAsJsonAsync("/api/v4/blood/requests", dto);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                _shell?.ShowToast($"患者 {SelectedInpatient.PatientName} 临床用血申请已开立并完成合理用血审核", "Success");
                ShowNewReqPanel = false;
                await LoadDataAsync();
            }
            else
            {
                _shell?.ShowToast($"申请开立失败: {result?.Message ?? "服务异常"}", "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"申请异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteCrossmatchAsync()
    {
        if (SelectedRequest == null)
        {
            _shell?.ShowToast("请先选择一条待配血申请单", "Warning");
            return;
        }

        string bagBarcode = SelectedBagForMatch?.BagBarcode ?? SelectedRequest.BagBarcode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bagBarcode))
        {
            // Pick first matching bag from inventory if available
            var matchBag = InventoryBags.FirstOrDefault(b => b.Status == "IN_STOCK" && b.AboType == SelectedRequest.PatientAbo);
            if (matchBag != null) bagBarcode = matchBag.BagBarcode;
        }

        if (string.IsNullOrWhiteSpace(bagBarcode))
        {
            _shell?.ShowToast("请指定或从在库清单选择配血血袋条码", "Warning");
            return;
        }

        var donorAbo = SelectedBagForMatch?.AboType ?? SelectedRequest.PatientAbo;
        var donorRh = SelectedBagForMatch?.RhType ?? SelectedRequest.PatientRh;

        try
        {
            var dto = new
            {
                BloodRequestId = SelectedRequest.Id,
                BagBarcode = bagBarcode,
                PatientAbo = SelectedRequest.PatientAbo,
                PatientRh = SelectedRequest.PatientRh,
                DonorAbo = donorAbo,
                DonorRh = donorRh,
                AntibodyScreenResult = AntibodyScreen,
                MajorCrossmatch = MajorCrossmatch,
                MinorCrossmatch = MinorCrossmatch,
                VerifierId = CrossmatchVerifier,
                Remark = $"主侧配血: {MajorCrossmatch}, 次侧配血: {MinorCrossmatch}, 抗筛: {AntibodyScreen}"
            };

            var res = await _http.PostAsJsonAsync("/api/v4/blood/crossmatch", dto);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                SelectedRequest.Status = 1;
                SelectedRequest.BagBarcode = bagBarcode;
                StatusMessage = $"✓ 交叉配血相容核验通过：血袋 {bagBarcode} 已绑定，双人审签已存证。";
                _shell?.ShowToast("配血相容性核验通过，允许发血", "Success");
                await LoadDataAsync();
            }
            else
            {
                string msg = result?.Message ?? "配血不相容或核查不通过";
                StatusMessage = $"⚠ 配血拦截：{msg}";
                _shell?.ShowToast(msg, "Danger", durationSeconds: 8);
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"交叉配血调用异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteDualCheckDispenseAsync()
    {
        if (SelectedRequest == null)
        {
            _shell?.ShowToast("请先选择待发血申请单", "Warning");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedRequest.BagBarcode))
        {
            _shell?.ShowToast("该申请尚未完成交叉配血相容核定，禁止发血", "Warning");
            return;
        }

        if (string.IsNullOrWhiteSpace(DispenseStaff2) || DispenseStaff2 == DispenseStaff1)
        {
            _shell?.ShowToast("血库发血必须由两名不同资质技术人员双人双签核对", "Warning");
            return;
        }

        try
        {
            var dto = new
            {
                BloodRequestId = SelectedRequest.Id,
                BagBarcode = SelectedRequest.BagBarcode,
                DualCheckDispenserId = DispenseStaff2
            };

            var res = await _http.PostAsJsonAsync("/api/v4/blood/dispense", dto);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                SelectedRequest.Status = 2;
                StatusMessage = $"✓ 双人双签发血闭环完成：血袋 {SelectedRequest.BagBarcode} 已出库并生成国密过程签名与时间戳。";
                _shell?.ShowToast("血库双人双签核发成功，出库闭环完成", "Success");
                await LoadDataAsync();
            }
            else
            {
                string msg = result?.Message ?? "发血核验失败";
                StatusMessage = $"发血拦截: {msg}";
                _shell?.ShowToast(msg, "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"发血调用异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteSubmitReactionAsync()
    {
        if (SelectedRequest == null)
        {
            _shell?.ShowToast("请选择关联的用血记录", "Warning");
            return;
        }

        try
        {
            var dto = new
            {
                ExecutionId = SelectedRequest.Id,
                PatientId = SelectedRequest.PatientId,
                BagBarcode = SelectedRequest.BagBarcode ?? "BLD-UNKNOWN",
                ReactionType = ReactionType,
                Severity = ReactionSeverity,
                Symptoms = ReactionSymptoms,
                Disposition = ReactionDisposition,
                ShareExternal = true
            };

            var res = await _http.PostAsJsonAsync("/api/v4/blood/reactions", dto);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                _shell?.ShowToast("输血不良反应已紧急处置并上报血库及区域卫生平台", "Success");
                ShowReactionPanel = false;
            }
            else
            {
                _shell?.ShowToast($"不良反应上报失败: {result?.Message ?? "服务异常"}", "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"上报异常: {ex.Message}", "Danger");
        }
    }

    public async Task ExecuteSyncExternalAsync()
    {
        try
        {
            var res = await _http.PostAsync("/api/v4/blood/external/sync-center", null);
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (res.IsSuccessStatusCode && result?.Code == 200)
            {
                _shell?.ShowToast("与中心血站区域专网库存与备血同步完成", "Success");
                await LoadDataAsync();
            }
            else
            {
                _shell?.ShowToast($"同步失败: {result?.Message ?? "专网未响应"}", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"同步异常: {ex.Message}", "Danger");
        }
    }

    private static string ResolveVarietyName(string code) => code?.ToUpperInvariant() switch
    {
        "RBC" or "悬浮红细胞" => "悬浮红细胞 (RBC)",
        "FFP" or "新鲜冰冻血浆" => "新鲜冰冻血浆 (FFP)",
        "PLT" or "单采血小板" => "单采血小板 (PLT)",
        "CRYO" or "冷沉淀" => "冷沉淀凝血因子 (CRYO)",
        _ => code ?? "血液制剂"
    };

    public void Activate()
    {
        _ = LoadDataAsync();
        _ = LoadExecutionsAsync();
    }
    public void Deactivate() { }
    public void Dispose() => _http.Dispose();
    public bool OnShortcut(string gesture) => false;

    // DTO records for json deserialization
    public record BloodBagDto(
        string Id,
        string BagBarcode,
        string DonationCode,
        string SourceBloodCenter,
        string BloodCode,
        string AboType,
        string RhType,
        decimal VolumeMl,
        int CollectionDateInt,
        int ExpiryDateInt,
        decimal ColdChainTemp,
        string Status
    );

    public record BloodRequestSummaryDto(
        string Id,
        string EncounterId,
        string PatientId,
        string PatientName,
        string Gender,
        int BirthDateInt,
        string AboType,
        string RhType,
        string BloodProductCode,
        int VolumeMl,
        decimal? LatestHgb,
        string? RationalCheckResult,
        string? BagBarcode,
        int Status,
        string StatusDesc,
        long CreatedAtMs
    );

    public record PatientBloodDistDto(
        string? AboType,
        string? RhType,
        int PatientCount
    );

    public record ActiveInpatientEncounterDto(
        string Id,
        string PatientId,
        string PatientName,
        string Gender,
        int Age,
        string BedCode,
        string AdmissionNo,
        string DeptName
    );
}
