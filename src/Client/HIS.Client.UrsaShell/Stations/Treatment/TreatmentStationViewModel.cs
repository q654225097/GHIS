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

namespace HIS.Client.UrsaShell.Stations.Treatment;

public class TreatmentCatalogItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string CatalogCode { get; set; } = string.Empty;
    public string CatalogName { get; set; } = string.Empty;
    public string Category { get; set; } = "REHAB";
    public string ExecDeptId { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public bool IsHighRisk { get; set; }

    public string HighRiskBadge => IsHighRisk ? "高风险 (双核对)" : "常规治疗";
    public string DisplayName => $"{CatalogName} [{HighRiskBadge}] (￥{UnitPrice:F2}/次)";
}

public class TreatmentInpatientItem : ClientModelBase
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string BedCode { get; set; } = string.Empty;
    public string AdmissionNo { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;

    public string DisplayName => $"{BedCode}床 · {PatientName} ({Gender} · {Age}岁) [{AdmissionNo}]";
}

public class TreatmentRequestItem : ClientModelBase
{
    private string _id = string.Empty;
    private string _requestNo = string.Empty;
    private string _patientId = string.Empty;
    private string _patientName = string.Empty;
    private string _encounterOrVisitId = string.Empty;
    private string _catalogId = string.Empty;
    private string _treatmentName = string.Empty;
    private string _category = "REHAB";
    private string _diagnosisIcd = string.Empty;
    private int _prescribedSessions = 1;
    private int _completedSessions;
    private string _status = "REQUESTED";
    private bool _isHighRisk;
    private string _remark = string.Empty;
    private long _createdAtMs;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string RequestNo { get => _requestNo; set => SetField(ref _requestNo, value); }
    public string PatientId { get => _patientId; set => SetField(ref _patientId, value); }
    public string PatientName { get => _patientName; set => SetField(ref _patientName, value); }
    public string EncounterOrVisitId { get => _encounterOrVisitId; set => SetField(ref _encounterOrVisitId, value); }
    public string CatalogId { get => _catalogId; set => SetField(ref _catalogId, value); }
    public string TreatmentName { get => _treatmentName; set => SetField(ref _treatmentName, value); }
    public string Category { get => _category; set => SetField(ref _category, value); }
    public string DiagnosisIcd { get => _diagnosisIcd; set => SetField(ref _diagnosisIcd, value); }
    public int PrescribedSessions { get => _prescribedSessions; set => SetField(ref _prescribedSessions, value); }
    public int CompletedSessions { get => _completedSessions; set => SetField(ref _completedSessions, value); }
    public string Status { get => _status; set => SetField(ref _status, value); }
    public bool IsHighRisk { get => _isHighRisk; set => SetField(ref _isHighRisk, value); }
    public string Remark { get => _remark; set => SetField(ref _remark, value); }
    public long CreatedAtMs { get => _createdAtMs; set => SetField(ref _createdAtMs, value); }

    public string StatusText => Status switch
    {
        "REQUESTED" => "待预约",
        "SCHEDULED" => "已预约排班",
        "IN_PROGRESS" => "疗程执行中",
        "COMPLETED" => "✓ 疗程已完成",
        "CANCELLED" => "已撤销",
        _ => Status
    };

    public string ProgressText => $"{CompletedSessions} / {PrescribedSessions} 次";
}

public class TreatmentAppointmentItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string TreatmentRoom { get; set; } = string.Empty;
    public long ScheduledStartMs { get; set; }
    public long ScheduledEndMs { get; set; }
    public string Status { get; set; } = "BOOKED";

    public string ScheduledTimeText => ScheduledStartMs > 0
        ? $"{DateTimeOffset.FromUnixTimeMilliseconds(ScheduledStartMs).ToLocalTime():yyyy-MM-dd HH:mm:ss} ~ {DateTimeOffset.FromUnixTimeMilliseconds(ScheduledEndMs).ToLocalTime():yyyy-MM-dd HH:mm:ss}"
        : "-";
}

public class TreatmentExecutionRecordItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public int SessionNo { get; set; } = 1;
    public long ActualStartMs { get; set; }
    public long ActualEndMs { get; set; }
    public string ActualParamsJson { get; set; } = string.Empty;
    public bool HighRiskCheckPassed { get; set; }
    public string DualCheckOperatorId { get; set; } = string.Empty;
    public string PatientReaction { get; set; } = string.Empty;
    public string CaSignatureId { get; set; } = string.Empty;
    public string Status { get; set; } = "FINISHED";

    public string ActualTimeText => ActualStartMs > 0
        ? $"{DateTimeOffset.FromUnixTimeMilliseconds(ActualStartMs).ToLocalTime():yyyy-MM-dd HH:mm:ss} ~ {DateTimeOffset.FromUnixTimeMilliseconds(ActualEndMs).ToLocalTime():yyyy-MM-dd HH:mm:ss}"
        : "-";
}

public class TreatmentSuggestionItem : ClientModelBase
{
    public string ParameterName { get; set; } = string.Empty;
    public string SuggestedValue { get; set; } = string.Empty;
    public string LabBasis { get; set; } = string.Empty;
    public string WarningNotice { get; set; } = string.Empty;
}

public sealed class TreatmentStationViewModel : ClientModelBase, IStationLifecycle, IShortcutTarget
{
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;
    private readonly HttpClient _http;

    private string _searchQuery = string.Empty;
    private string _selectedCategory = "ALL";
    private TreatmentRequestItem? _selectedRequest;
    private string _suggestedParamsSummary = string.Empty;
    private string _actualParams = string.Empty;
    private string _dualCheckUser = string.Empty;
    private bool _highRiskCheckPassed = true;
    private string _patientReaction = "治疗过程平稳，生命体征稳定，无不良反应";
    private string _statusMessage = string.Empty;

    // New Request Form
    private bool _isNewRequestFormVisible;
    private TreatmentInpatientItem? _selectedInpatientForRequest;
    private TreatmentCatalogItem? _selectedCatalogForRequest;
    private int _newRequestSessions = 3;
    private string _newRequestDiagnosis = "专科临床诊断待查";
    private string _newRequestRemark = string.Empty;

    // Appointment Form
    private bool _isAppointmentFormVisible;
    private string _appointmentRoom = "血液净化中心透析1室";

    public static IReadOnlyList<string> CategoryFilterOptions { get; } = new[]
    {
        "ALL",
        "DIALYSIS",
        "CHEMO",
        "HBO",
        "REHAB",
        "PHYSIO"
    };

    public ObservableCollection<TreatmentInpatientItem> Inpatients { get; } = new();
    public ObservableCollection<TreatmentCatalogItem> Catalogs { get; } = new();
    public ObservableCollection<TreatmentRequestItem> Requests { get; } = new();
    public ObservableCollection<TreatmentAppointmentItem> Appointments { get; } = new();
    public ObservableCollection<TreatmentExecutionRecordItem> ExecutionRecords { get; } = new();
    public ObservableCollection<TreatmentSuggestionItem> Suggestions { get; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value))
            {
                FilterRequests();
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
                FilterRequests();
            }
        }
    }

    public TreatmentRequestItem? SelectedRequest
    {
        get => _selectedRequest;
        set
        {
            if (SetField(ref _selectedRequest, value) && value != null)
            {
                _ = OnSelectedRequestChangedAsync(value);
            }
        }
    }

    public string SuggestedParamsSummary
    {
        get => _suggestedParamsSummary;
        set => SetField(ref _suggestedParamsSummary, value);
    }

    public string ActualParams
    {
        get => _actualParams;
        set => SetField(ref _actualParams, value);
    }

    public string DualCheckUser
    {
        get => _dualCheckUser;
        set => SetField(ref _dualCheckUser, value);
    }

    public bool HighRiskCheckPassed
    {
        get => _highRiskCheckPassed;
        set => SetField(ref _highRiskCheckPassed, value);
    }

    public string PatientReaction
    {
        get => _patientReaction;
        set => SetField(ref _patientReaction, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsNewRequestFormVisible
    {
        get => _isNewRequestFormVisible;
        set => SetField(ref _isNewRequestFormVisible, value);
    }

    public TreatmentInpatientItem? SelectedInpatientForRequest
    {
        get => _selectedInpatientForRequest;
        set => SetField(ref _selectedInpatientForRequest, value);
    }

    public TreatmentCatalogItem? SelectedCatalogForRequest
    {
        get => _selectedCatalogForRequest;
        set => SetField(ref _selectedCatalogForRequest, value);
    }

    public int NewRequestSessions
    {
        get => _newRequestSessions;
        set => SetField(ref _newRequestSessions, value);
    }

    public string NewRequestDiagnosis
    {
        get => _newRequestDiagnosis;
        set => SetField(ref _newRequestDiagnosis, value);
    }

    public string NewRequestRemark
    {
        get => _newRequestRemark;
        set => SetField(ref _newRequestRemark, value);
    }

    public bool IsAppointmentFormVisible
    {
        get => _isAppointmentFormVisible;
        set => SetField(ref _isAppointmentFormVisible, value);
    }

    public string AppointmentRoom
    {
        get => _appointmentRoom;
        set => SetField(ref _appointmentRoom, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SelectRequestCommand { get; }
    public ICommand ToggleNewRequestFormCommand { get; }
    public ICommand SubmitNewRequestCommand { get; }
    public ICommand ToggleAppointmentFormCommand { get; }
    public ICommand SubmitAppointmentCommand { get; }
    public ICommand ExecuteTreatmentCommand { get; }

    public TreatmentStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        _http = new HttpClient(new AuthenticatedHttpHandler(SessionContext.Current))
        {
            BaseAddress = new Uri(ApiEndpoints.BaseAddress),
            Timeout = TimeSpan.FromSeconds(10)
        };

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SelectRequestCommand = new RelayCommand(p => SelectedRequest = p as TreatmentRequestItem);
        ToggleNewRequestFormCommand = new RelayCommand(_ => IsNewRequestFormVisible = !IsNewRequestFormVisible);
        SubmitNewRequestCommand = new AsyncRelayCommand(ExecuteSubmitNewRequestAsync);
        ToggleAppointmentFormCommand = new RelayCommand(_ => IsAppointmentFormVisible = !IsAppointmentFormVisible);
        SubmitAppointmentCommand = new AsyncRelayCommand(ExecuteSubmitAppointmentAsync);
        ExecuteTreatmentCommand = new AsyncRelayCommand(ExecuteTreatmentAsync);

        DualCheckUser = SessionContext.Current.DisplayName ?? "复核护士/医师";
    }

    public async Task LoadDataAsync()
    {
        try
        {
            StatusMessage = "正在同步治疗项目目录、在院患者与申请队列...";

            // 1. Fetch Treatment Catalog
            var catRes = await _http.GetFromJsonAsync<ApiResponse<List<CatalogApiDto>>>("/api/v4/treatment/catalog");
            Catalogs.Clear();
            if (catRes?.Data != null)
            {
                foreach (var c in catRes.Data)
                {
                    Catalogs.Add(new TreatmentCatalogItem
                    {
                        Id = c.Id,
                        CatalogCode = c.CatalogCode,
                        CatalogName = c.CatalogName,
                        Category = c.Category,
                        ExecDeptId = c.ExecDeptId,
                        UnitPrice = c.UnitPrice,
                        IsHighRisk = c.IsHighRisk == 1
                    });
                }
            }

            // 2. Fetch Active Inpatients for Request Creation
            var inpatRes = await _http.GetFromJsonAsync<ApiResponse<List<ActiveInpatientEncounterDto>>>("/api/v4/ipd/encounters/active");
            Inpatients.Clear();
            if (inpatRes?.Data != null)
            {
                foreach (var enc in inpatRes.Data)
                {
                    Inpatients.Add(new TreatmentInpatientItem
                    {
                        EncounterId = enc.Id,
                        PatientId = enc.PatientId,
                        PatientName = enc.PatientName ?? "患者",
                        Gender = enc.Gender ?? "男",
                        Age = enc.Age > 0 ? enc.Age : 45,
                        BedCode = enc.BedCode ?? "未分床",
                        AdmissionNo = enc.AdmissionNo ?? enc.Id,
                        DeptName = enc.DeptName ?? "内科"
                    });
                }
            }
            if (Inpatients.Count > 0 && SelectedInpatientForRequest == null)
            {
                SelectedInpatientForRequest = Inpatients[0];
            }
            if (Catalogs.Count > 0 && SelectedCatalogForRequest == null)
            {
                SelectedCatalogForRequest = Catalogs[0];
            }

            // 3. Fetch Treatment Requests
            var reqRes = await _http.GetFromJsonAsync<ApiResponse<List<RequestApiDto>>>("/api/v4/treatment/requests");
            Requests.Clear();
            if (reqRes?.Data != null)
            {
                foreach (var r in reqRes.Data)
                {
                    var cat = Catalogs.FirstOrDefault(c => c.Id == r.CatalogId);
                    var pat = Inpatients.FirstOrDefault(p => p.PatientId == r.PatientId);

                    Requests.Add(new TreatmentRequestItem
                    {
                        Id = r.Id,
                        RequestNo = r.RequestNo,
                        PatientId = r.PatientId,
                        PatientName = pat?.PatientName ?? $"患者 ({r.PatientId[..Math.Min(6, r.PatientId.Length)]})",
                        EncounterOrVisitId = r.EncounterOrVisitId,
                        CatalogId = r.CatalogId,
                        TreatmentName = cat?.CatalogName ?? "一般临床治疗项目",
                        Category = cat?.Category ?? "REHAB",
                        DiagnosisIcd = string.IsNullOrWhiteSpace(r.DiagnosisIcd) ? "临床诊断" : r.DiagnosisIcd,
                        PrescribedSessions = r.PrescribedSessions,
                        CompletedSessions = r.CompletedSessions,
                        Status = r.Status,
                        IsHighRisk = cat?.IsHighRisk ?? false,
                        Remark = r.Remark ?? string.Empty,
                        CreatedAtMs = r.CreatedAtMs
                    });
                }
            }

            // 4. Fetch Appointments
            var apptRes = await _http.GetFromJsonAsync<ApiResponse<List<AppointmentApiDto>>>("/api/v4/treatment/appointments");
            Appointments.Clear();
            if (apptRes?.Data != null)
            {
                foreach (var a in apptRes.Data)
                {
                    Appointments.Add(new TreatmentAppointmentItem
                    {
                        Id = a.Id,
                        RequestId = a.RequestId,
                        PatientId = a.PatientId,
                        TreatmentRoom = a.TreatmentRoom,
                        ScheduledStartMs = a.ScheduledStartMs,
                        ScheduledEndMs = a.ScheduledEndMs,
                        Status = a.Status
                    });
                }
            }

            if (Requests.Count > 0 && SelectedRequest == null)
            {
                SelectedRequest = Requests[0];
            }

            StatusMessage = $"✓ 治疗中心数据已刷新：目录 {Catalogs.Count} 项，申请 {Requests.Count} 份，预约 {Appointments.Count} 条。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }

    private async Task OnSelectedRequestChangedAsync(TreatmentRequestItem req)
    {
        try
        {
            // 1. Fetch Parameter Suggestions
            var sugRes = await _http.GetFromJsonAsync<ApiResponse<List<TreatmentParamSuggestionDto>>>($"/api/v4/treatment/requests/{req.Id}/suggest-parameters");
            Suggestions.Clear();
            if (sugRes?.Data != null && sugRes.Data.Count > 0)
            {
                foreach (var s in sugRes.Data)
                {
                    Suggestions.Add(new TreatmentSuggestionItem
                    {
                        ParameterName = s.ParameterName,
                        SuggestedValue = s.SuggestedValue,
                        LabBasis = s.LabBasis,
                        WarningNotice = s.WarningNotice
                    });
                }

                var firstSug = sugRes.Data[0];
                SuggestedParamsSummary = string.Join("；\n", sugRes.Data.Select(s => $"【{s.ParameterName}】依据 [{s.LabBasis}] ➔ 推荐: {s.SuggestedValue} ({s.WarningNotice})"));
                ActualParams = firstSug.SuggestedValue;
            }
            else
            {
                SuggestedParamsSummary = "已调阅临床指南推荐参数与患者生化基准。";
                ActualParams = req.Category switch
                {
                    "DIALYSIS" => "超滤量: 2200ml, 血液流速: 220ml/min, 透析液温度: 36.5℃, 肝素首剂: 2500IU",
                    "CHEMO" => "全剂量输注, 滴速: 80ml/h, 预防水化及止吐",
                    "HBO" => "治疗压力: 0.20MPa, 稳压吸氧: 60min, 升降压各: 20min",
                    _ => "按专科医嘱标准方案执行"
                };
            }

            // 2. Fetch Patient Execution History
            var execRes = await _http.GetFromJsonAsync<ApiResponse<List<ExecutionRecordApiDto>>>($"/api/v4/treatment/records/by-patient/{req.PatientId}");
            ExecutionRecords.Clear();
            if (execRes?.Data != null)
            {
                foreach (var ex in execRes.Data)
                {
                    ExecutionRecords.Add(new TreatmentExecutionRecordItem
                    {
                        Id = ex.Id,
                        RequestId = ex.RequestId,
                        PatientId = ex.PatientId,
                        SessionNo = ex.SessionNo,
                        ActualStartMs = ex.ActualStartMs,
                        ActualEndMs = ex.ActualEndMs,
                        ActualParamsJson = ex.ActualParamsJson ?? string.Empty,
                        HighRiskCheckPassed = ex.HighRiskCheckPassed == 1,
                        DualCheckOperatorId = ex.DualCheckOperatorId ?? string.Empty,
                        PatientReaction = ex.PatientReaction ?? string.Empty,
                        CaSignatureId = ex.CaSignatureId ?? string.Empty,
                        Status = ex.Status
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载治疗详情失败: {ex.Message}";
        }
    }

    private async Task ExecuteSubmitNewRequestAsync()
    {
        if (SelectedInpatientForRequest == null || SelectedCatalogForRequest == null)
        {
            _shell?.ShowToast("请选择在院患者与治疗项目", "Warning");
            return;
        }

        try
        {
            var payload = new
            {
                PatientId = SelectedInpatientForRequest.PatientId,
                EncounterOrVisitId = SelectedInpatientForRequest.EncounterId,
                CatalogId = SelectedCatalogForRequest.Id,
                SourceType = "IPD",
                SourceOrgCode = (string?)null,
                ExternalPatientId = (string?)null,
                DiagnosisIcd = string.IsNullOrWhiteSpace(NewRequestDiagnosis) ? "临床诊断" : NewRequestDiagnosis.Trim(),
                PrescribedSessions = NewRequestSessions > 0 ? NewRequestSessions : 1,
                Remark = NewRequestRemark.Trim()
            };

            var res = await _http.PostAsJsonAsync("/api/v4/treatment/requests", payload);
            if (res.IsSuccessStatusCode)
            {
                IsNewRequestFormVisible = false;
                await LoadDataAsync();
                StatusMessage = "✓ 治疗申请单开立成功，已自动关联数字签名与诊疗流程。";
                _shell?.ShowToast("治疗申请开立成功", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"开立失败: {err}";
                _shell?.ShowToast("开立治疗申请失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"开立异常: {ex.Message}";
            _shell?.ShowToast($"开立异常: {ex.Message}", "Error");
        }
    }

    private async Task ExecuteSubmitAppointmentAsync()
    {
        if (SelectedRequest == null)
        {
            _shell?.ShowToast("请先选择待预约的治疗申请单", "Warning");
            return;
        }

        try
        {
            long now = SystemTime.UtcNowMs;
            var payload = new
            {
                RequestId = SelectedRequest.Id,
                TreatmentRoom = AppointmentRoom.Trim(),
                ScheduledStartMs = now + 3600 * 1000,
                ScheduledEndMs = now + 7200 * 1000
            };

            var res = await _http.PostAsJsonAsync("/api/v4/treatment/appointments", payload);
            if (res.IsSuccessStatusCode)
            {
                IsAppointmentFormVisible = false;
                SelectedRequest.Status = "SCHEDULED";
                await LoadDataAsync();
                StatusMessage = "✓ 治疗预约排班成功，已向患者端推送预约提醒。";
                _shell?.ShowToast("预约排班成功", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"预约失败: {err}";
                _shell?.ShowToast("预约排班失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"预约异常: {ex.Message}";
            _shell?.ShowToast($"预约异常: {ex.Message}", "Error");
        }
    }

    public async Task ExecuteTreatmentAsync()
    {
        if (SelectedRequest == null)
        {
            _shell?.ShowToast("请先选择治疗申请单", "Warning");
            return;
        }

        if (SelectedRequest.IsHighRisk)
        {
            if (!HighRiskCheckPassed)
            {
                _shell?.ShowToast("高风险治疗项目必须确认高风险核对通过", "Warning");
                return;
            }
            if (string.IsNullOrWhiteSpace(DualCheckUser))
            {
                _shell?.ShowToast("高风险治疗项目必须录入双人复核工牌/姓名", "Warning");
                return;
            }
        }

        try
        {
            long now = SystemTime.UtcNowMs;
            var payload = new
            {
                RequestId = SelectedRequest.Id,
                AppointmentId = (string?)null,
                ActualStartMs = now - 1800 * 1000,
                ActualEndMs = now,
                ActualParams = new Dictionary<string, string> { ["executed_params"] = ActualParams },
                HighRiskCheckPassed = HighRiskCheckPassed,
                DualCheckOperatorId = string.IsNullOrWhiteSpace(DualCheckUser) ? SessionContext.Current.DisplayName : DualCheckUser.Trim(),
                PatientReaction = PatientReaction.Trim()
            };

            var res = await _http.PostAsJsonAsync("/api/v4/treatment/executions", payload);
            if (res.IsSuccessStatusCode)
            {
                SelectedRequest.CompletedSessions += 1;
                if (SelectedRequest.CompletedSessions >= SelectedRequest.PrescribedSessions)
                {
                    SelectedRequest.Status = "COMPLETED";
                }
                else
                {
                    SelectedRequest.Status = "IN_PROGRESS";
                }

                await OnSelectedRequestChangedAsync(SelectedRequest);
                StatusMessage = "✓ 治疗执行记录已归档，双人核验与国密可靠电子签名已完成，费用已自动联动。";
                _shell?.ShowToast("治疗记录已签署并归档 (完成费用与签名联动)", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"执行记录失败: {err}";
                _shell?.ShowToast("执行失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"执行错误: {ex.Message}";
            _shell?.ShowToast($"执行错误: {ex.Message}", "Error");
        }
    }

    private void FilterRequests()
    {
        // Requests filtering helper
    }

    public void Activate() => _ = LoadDataAsync();
    public void Deactivate() { }
    public void Dispose() => _http.Dispose();
    public bool OnShortcut(string gesture) => false;

    // API DTO records
    public record CatalogApiDto(
        string Id,
        string CatalogCode,
        string CatalogName,
        string Category,
        string ExecDeptId,
        decimal UnitPrice,
        short IsHighRisk
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

    public record RequestApiDto(
        string Id,
        string RequestNo,
        string SourceType,
        string? SourceOrgCode,
        string? ExternalPatientId,
        string PatientId,
        string EncounterOrVisitId,
        string CatalogId,
        string? DiagnosisIcd,
        int PrescribedSessions,
        int CompletedSessions,
        string Status,
        string? Remark,
        long CreatedAtMs
    );

    public record AppointmentApiDto(
        string Id,
        string RequestId,
        string PatientId,
        string TreatmentRoom,
        long ScheduledStartMs,
        long ScheduledEndMs,
        string Status
    );

    public record ExecutionRecordApiDto(
        string Id,
        string RequestId,
        string? AppointmentId,
        string PatientId,
        int SessionNo,
        long ActualStartMs,
        long ActualEndMs,
        string? ActualParamsJson,
        short HighRiskCheckPassed,
        string? DualCheckOperatorId,
        string? PatientReaction,
        string? CaSignatureId,
        string Status
    );
}
