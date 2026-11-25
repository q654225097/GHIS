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

namespace HIS.Client.UrsaShell.Stations.EmrQc;

public class QcDefectItem : ClientModelBase
{
    private string _id = string.Empty;
    private string _taskId = string.Empty;
    private string _qcDimension = "TIMELINESS";
    private string _description = string.Empty;
    private decimal _deductPoints = 5m;
    private string _doctorId = string.Empty;
    private string _status = "OPEN";
    private string _rectificationNote = string.Empty;
    private string _verifiedBy = string.Empty;
    private long _deadlineMs;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string TaskId { get => _taskId; set => SetField(ref _taskId, value); }
    public string QcDimension { get => _qcDimension; set => SetField(ref _qcDimension, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }
    public decimal DeductPoints { get => _deductPoints; set => SetField(ref _deductPoints, value); }
    public string DoctorId { get => _doctorId; set => SetField(ref _doctorId, value); }
    public string Status { get => _status; set => SetField(ref _status, value); }
    public string RectificationNote { get => _rectificationNote; set => SetField(ref _rectificationNote, value); }
    public string VerifiedBy { get => _verifiedBy; set => SetField(ref _verifiedBy, value); }
    public long DeadlineMs { get => _deadlineMs; set => SetField(ref _deadlineMs, value); }

    public string StatusText => Status switch
    {
        "OPEN" => "待整改 (OPEN)",
        "DOCTOR_FIXED" => "医师已整改待复核",
        "VERIFIED_CLOSED" => "✓ 已复核关闭",
        "REJECTED" => "整改不合格驳回",
        _ => Status
    };

    public string DeadlineText => DeadlineMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(DeadlineMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "-";
}

public class QcTaskItem : ClientModelBase
{
    private string _id = string.Empty;
    private string _encounterId = string.Empty;
    private string _patientId = string.Empty;
    private string _patientName = string.Empty;
    private string _admissionNo = string.Empty;
    private decimal _totalScore = 100m;
    private string _grade = "A";
    private string _taskType = "AUTO_PROCESS";
    private string _status = "ACTIVE";
    private int _defectCount;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string EncounterId { get => _encounterId; set => SetField(ref _encounterId, value); }
    public string PatientId { get => _patientId; set => SetField(ref _patientId, value); }
    public string PatientName { get => _patientName; set => SetField(ref _patientName, value); }
    public string AdmissionNo { get => _admissionNo; set => SetField(ref _admissionNo, value); }
    public decimal TotalScore { get => _totalScore; set => SetField(ref _totalScore, value); }
    public string Grade { get => _grade; set => SetField(ref _grade, value); }
    public string TaskType { get => _taskType; set => SetField(ref _taskType, value); }
    public string Status { get => _status; set => SetField(ref _status, value); }
    public int DefectCount { get => _defectCount; set => SetField(ref _defectCount, value); }

    public string GradeDisplay => Grade switch
    {
        "A" => $"甲级病历 ({TotalScore:F0}分)",
        "B" => $"乙级病历 ({TotalScore:F0}分)",
        "C" => $"丙级病历 ({TotalScore:F0}分)",
        _ => $"{Grade} ({TotalScore:F0}分)"
    };
}

public class QcInpatientItem : ClientModelBase
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string BedCode { get; set; } = string.Empty;
    public string AdmissionNo { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string AttendingDoctorName { get; set; } = string.Empty;

    public string DisplayName => $"{BedCode}床 · {PatientName} ({Gender} · {Age}岁) [{AdmissionNo}]";
}

public class QcCrossOrgTrackItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string SourceOrgCode { get; set; } = string.Empty;
    public string ExternalDocId { get; set; } = string.Empty;
    public string QcReviewerId { get; set; } = string.Empty;
    public string FindingSummary { get; set; } = string.Empty;
    public string TrackStatus { get; set; } = "REVIEWED";
    public long CreatedAtMs { get; set; }

    public string CreatedAtText => CreatedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "-";
}

public sealed class EmrQcStationViewModel : ClientModelBase, IStationLifecycle, IShortcutTarget
{
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;
    private readonly HttpClient _http;

    private QcInpatientItem? _selectedInpatient;
    private QcTaskItem? _selectedTask;
    private QcDefectItem? _selectedDefect;
    private string _doctorFixReason = string.Empty;
    private string _statusMessage = string.Empty;

    // Manual Defect Form Fields
    private bool _isManualDefectFormVisible;
    private string _manualQcDimension = "TIMELINESS";
    private string _manualDefectDescription = string.Empty;
    private decimal _manualDeductPoints = 5m;
    private string _manualResponsibleDoctor = string.Empty;

    // Cross-Org Track Form Fields
    private bool _isCrossOrgFormVisible;
    private string _crossOrgSourceCode = "ORG-REGIONAL-01";
    private string _crossOrgDocId = string.Empty;
    private string _crossOrgSummary = string.Empty;

    public static IReadOnlyList<string> DimensionOptions { get; } = new[]
    {
        "TIMELINESS",
        "FORMAT",
        "CONTENT",
        "SPECIALTY",
        "DRUG_RATIONALITY",
        "INFECTION_CONTROL",
        "COST_CONTROL"
    };

    public ObservableCollection<QcInpatientItem> Inpatients { get; } = new();
    public ObservableCollection<QcTaskItem> Tasks { get; } = new();
    public ObservableCollection<QcDefectItem> Defects { get; } = new();
    public ObservableCollection<QcCrossOrgTrackItem> CrossOrgTracks { get; } = new();

    public QcInpatientItem? SelectedInpatient
    {
        get => _selectedInpatient;
        set
        {
            if (SetField(ref _selectedInpatient, value) && value != null)
            {
                _ = OnInpatientChangedAsync(value);
            }
        }
    }

    public QcTaskItem? SelectedTask
    {
        get => _selectedTask;
        set => SetField(ref _selectedTask, value);
    }

    public QcDefectItem? SelectedDefect
    {
        get => _selectedDefect;
        set
        {
            if (SetField(ref _selectedDefect, value) && value != null)
            {
                DoctorFixReason = value.RectificationNote;
            }
        }
    }

    public string DoctorFixReason
    {
        get => _doctorFixReason;
        set => SetField(ref _doctorFixReason, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsManualDefectFormVisible
    {
        get => _isManualDefectFormVisible;
        set => SetField(ref _isManualDefectFormVisible, value);
    }

    public string ManualQcDimension
    {
        get => _manualQcDimension;
        set => SetField(ref _manualQcDimension, value);
    }

    public string ManualDefectDescription
    {
        get => _manualDefectDescription;
        set => SetField(ref _manualDefectDescription, value);
    }

    public decimal ManualDeductPoints
    {
        get => _manualDeductPoints;
        set => SetField(ref _manualDeductPoints, value);
    }

    public string ManualResponsibleDoctor
    {
        get => _manualResponsibleDoctor;
        set => SetField(ref _manualResponsibleDoctor, value);
    }

    public bool IsCrossOrgFormVisible
    {
        get => _isCrossOrgFormVisible;
        set => SetField(ref _isCrossOrgFormVisible, value);
    }

    public string CrossOrgSourceCode
    {
        get => _crossOrgSourceCode;
        set => SetField(ref _crossOrgSourceCode, value);
    }

    public string CrossOrgDocId
    {
        get => _crossOrgDocId;
        set => SetField(ref _crossOrgDocId, value);
    }

    public string CrossOrgSummary
    {
        get => _crossOrgSummary;
        set => SetField(ref _crossOrgSummary, value);
    }

    public ICommand SelectInpatientCommand { get; }
    public ICommand SelectTaskCommand { get; }
    public ICommand SelectDefectCommand { get; }
    public ICommand RunAutoQcCommand { get; }
    public ICommand RectifyDefectCommand { get; }
    public ICommand VerifyPassCommand { get; }
    public ICommand VerifyRejectCommand { get; }
    public ICommand ToggleManualDefectFormCommand { get; }
    public ICommand SubmitManualDefectCommand { get; }
    public ICommand ToggleCrossOrgFormCommand { get; }
    public ICommand SubmitCrossOrgTrackCommand { get; }
    public ICommand RefreshCommand { get; }

    public EmrQcStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        _http = new HttpClient(new AuthenticatedHttpHandler(SessionContext.Current))
        {
            BaseAddress = new Uri(ApiEndpoints.BaseAddress),
            Timeout = TimeSpan.FromSeconds(10)
        };

        SelectInpatientCommand = new RelayCommand(p => SelectedInpatient = p as QcInpatientItem);
        SelectTaskCommand = new RelayCommand(p => SelectedTask = p as QcTaskItem);
        SelectDefectCommand = new RelayCommand(p => SelectedDefect = p as QcDefectItem);
        RunAutoQcCommand = new AsyncRelayCommand(ExecuteRunAutoQcAsync);
        RectifyDefectCommand = new AsyncRelayCommand(ExecuteRectifyDefectAsync);
        VerifyPassCommand = new AsyncRelayCommand(ExecuteVerifyPassAsync);
        VerifyRejectCommand = new AsyncRelayCommand(ExecuteVerifyRejectAsync);
        ToggleManualDefectFormCommand = new RelayCommand(_ => IsManualDefectFormVisible = !IsManualDefectFormVisible);
        SubmitManualDefectCommand = new AsyncRelayCommand(ExecuteSubmitManualDefectAsync);
        ToggleCrossOrgFormCommand = new RelayCommand(_ => IsCrossOrgFormVisible = !IsCrossOrgFormVisible);
        SubmitCrossOrgTrackCommand = new AsyncRelayCommand(ExecuteSubmitCrossOrgTrackAsync);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);

        // Pre-fill doctor from session
        ManualResponsibleDoctor = SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "DOCTOR";
    }

    public async Task LoadDataAsync()
    {
        try
        {
            StatusMessage = "正在同步全院在院患者与病历质控任务...";

            // 1. Fetch active inpatients
            var inpatRes = await _http.GetFromJsonAsync<ApiResponse<List<ActiveInpatientEncounterDto>>>("/api/v4/ipd/encounters/active");
            if (inpatRes?.Data != null)
            {
                Inpatients.Clear();
                foreach (var enc in inpatRes.Data)
                {
                    Inpatients.Add(new QcInpatientItem
                    {
                        EncounterId = enc.Id,
                        PatientId = enc.PatientId,
                        PatientName = enc.PatientName ?? "患者",
                        Gender = enc.Gender ?? "男",
                        Age = enc.Age > 0 ? enc.Age : 45,
                        BedCode = enc.BedCode ?? "未分床",
                        AdmissionNo = enc.AdmissionNo ?? enc.Id,
                        DeptName = enc.DeptName ?? "内科",
                        AttendingDoctorName = SessionContext.Current.DisplayName ?? "管床医师"
                    });
                }
            }

            // 2. Fetch all recent QC tasks
            var taskRes = await _http.GetFromJsonAsync<ApiResponse<List<EmrQcTaskApiDto>>>("/api/v4/emr-qc/tasks?limit=50");
            if (taskRes?.Data != null)
            {
                Tasks.Clear();
                foreach (var t in taskRes.Data)
                {
                    var matchingPat = Inpatients.FirstOrDefault(p => p.EncounterId == t.EncounterId);
                    Tasks.Add(new QcTaskItem
                    {
                        Id = t.Id,
                        EncounterId = t.EncounterId,
                        PatientId = t.PatientId,
                        PatientName = matchingPat?.PatientName ?? $"住院患者 ({t.EncounterId[..Math.Min(8, t.EncounterId.Length)]})",
                        AdmissionNo = matchingPat?.AdmissionNo ?? t.EncounterId,
                        TotalScore = t.TotalScore,
                        Grade = t.Grade,
                        TaskType = t.TaskType == "AUTO_PROCESS" ? "全内涵自动质控" : "人工环节质控",
                        Status = t.Status
                    });
                }
            }

            // 3. Fetch cross-org tracks
            var crossRes = await _http.GetFromJsonAsync<ApiResponse<List<EmrQcCrossOrgTrackApiDto>>>("/api/v4/emr-qc/cross-org/tracks");
            if (crossRes?.Data != null)
            {
                CrossOrgTracks.Clear();
                foreach (var c in crossRes.Data)
                {
                    CrossOrgTracks.Add(new QcCrossOrgTrackItem
                    {
                        Id = c.Id,
                        PatientId = c.PatientId,
                        SourceOrgCode = c.SourceOrgCode,
                        ExternalDocId = c.ExternalDocId,
                        QcReviewerId = c.QcReviewerId,
                        FindingSummary = c.FindingSummary,
                        TrackStatus = c.TrackStatus,
                        CreatedAtMs = c.CreatedAtMs
                    });
                }
            }

            // Select default patient if available
            if (Inpatients.Count > 0 && SelectedInpatient == null)
            {
                SelectedInpatient = Inpatients[0];
            }
            else if (SelectedInpatient != null)
            {
                await OnInpatientChangedAsync(SelectedInpatient);
            }

            StatusMessage = $"✓ 质控中心数据同步完成：在院患者 {Inpatients.Count} 人，质控任务 {Tasks.Count} 项，跨机构调阅 {CrossOrgTracks.Count} 条。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }

    private async Task OnInpatientChangedAsync(QcInpatientItem patient)
    {
        try
        {
            // Find task for this encounter
            var matchedTask = Tasks.FirstOrDefault(t => t.EncounterId == patient.EncounterId);
            if (matchedTask != null)
            {
                SelectedTask = matchedTask;
            }
            else
            {
                // Create placeholder task display
                SelectedTask = new QcTaskItem
                {
                    Id = string.Empty,
                    EncounterId = patient.EncounterId,
                    PatientId = patient.PatientId,
                    PatientName = patient.PatientName,
                    AdmissionNo = patient.AdmissionNo,
                    TotalScore = 100m,
                    Grade = "A",
                    TaskType = "待扫描",
                    Status = "PENDING"
                };
            }

            // Fetch defects for this encounter
            var defectRes = await _http.GetFromJsonAsync<ApiResponse<List<EmrQcDefectApiDto>>>($"/api/v4/emr-qc/defects?encounterId={patient.EncounterId}");
            Defects.Clear();
            if (defectRes?.Data != null)
            {
                foreach (var d in defectRes.Data)
                {
                    Defects.Add(new QcDefectItem
                    {
                        Id = d.Id,
                        TaskId = d.TaskId,
                        QcDimension = d.QcDimension,
                        Description = d.DefectDescription,
                        DeductPoints = d.DeductPoints,
                        DoctorId = d.ResponsibleDoctorId,
                        Status = d.Status,
                        RectificationNote = d.RectificationNote ?? string.Empty,
                        VerifiedBy = d.VerifiedBy ?? string.Empty,
                        DeadlineMs = d.DeadlineMs
                    });
                }
            }

            if (SelectedTask != null)
            {
                SelectedTask.DefectCount = Defects.Count(d => d.Status != "VERIFIED_CLOSED");
            }

            SelectedDefect = Defects.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取患者质控详情失败: {ex.Message}";
        }
    }

    private async Task ExecuteRunAutoQcAsync()
    {
        if (SelectedInpatient == null)
        {
            _shell?.ShowToast("请先选择待质控的住院患者", "Warning");
            return;
        }

        try
        {
            StatusMessage = $"正在对患者 {SelectedInpatient.PatientName} 运行全内涵自动质控扫描...";
            var res = await _http.PostAsync($"/api/v4/emr-qc/run-auto/{SelectedInpatient.EncounterId}", null);
            var content = await res.Content.ReadAsStringAsync();

            if (res.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResponse<EmrQcReportDto>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (result?.Data != null)
                {
                    var rep = result.Data;
                    if (SelectedTask != null)
                    {
                        SelectedTask.Id = rep.TaskId;
                        SelectedTask.TotalScore = rep.TotalScore;
                        SelectedTask.Grade = rep.Grade;
                        SelectedTask.TaskType = "全内涵自动质控";
                    }

                    // Refresh defects
                    await OnInpatientChangedAsync(SelectedInpatient);

                    StatusMessage = $"✓ 自动质控扫描完成！评分: {rep.TotalScore:F0} 分 ({rep.Grade})，发现缺陷 {rep.Defects.Count} 项。";
                    _shell?.ShowToast($"自动质控完成：{rep.TotalScore:F0}分 ({rep.Grade})", "Success");
                }
            }
            else
            {
                StatusMessage = $"质控扫描失败: {res.StatusCode} - {content}";
                _shell?.ShowToast($"质控扫描失败: {res.StatusCode}", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"质控异常: {ex.Message}";
            _shell?.ShowToast($"质控异常: {ex.Message}", "Error");
        }
    }

    private async Task ExecuteSubmitManualDefectAsync()
    {
        if (SelectedInpatient == null)
        {
            _shell?.ShowToast("请先选择患者", "Warning");
            return;
        }
        if (string.IsNullOrWhiteSpace(ManualDefectDescription))
        {
            _shell?.ShowToast("请录入质控缺陷描述", "Warning");
            return;
        }

        try
        {
            var payload = new
            {
                EncounterId = SelectedInpatient.EncounterId,
                DocId = (string?)null,
                RuleCode = $"MANUAL-{ManualQcDimension}",
                QcDimension = ManualQcDimension,
                DefectDescription = ManualDefectDescription.Trim(),
                DeductPoints = ManualDeductPoints,
                ResponsibleDoctorId = string.IsNullOrWhiteSpace(ManualResponsibleDoctor)
                    ? (SessionContext.Current.DisplayName ?? "DOCTOR")
                    : ManualResponsibleDoctor.Trim(),
                DeadlineMs = SystemTime.UtcNowMs + 24 * 3600 * 1000
            };

            var res = await _http.PostAsJsonAsync("/api/v4/emr-qc/defects", payload);
            if (res.IsSuccessStatusCode)
            {
                ManualDefectDescription = string.Empty;
                IsManualDefectFormVisible = false;
                await OnInpatientChangedAsync(SelectedInpatient);
                StatusMessage = "✓ 人工质控缺陷已成功下发至责任医师。";
                _shell?.ShowToast("人工质控缺陷下发成功", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"下发失败: {err}";
                _shell?.ShowToast("下发失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"下发异常: {ex.Message}";
            _shell?.ShowToast($"下发异常: {ex.Message}", "Error");
        }
    }

    private async Task ExecuteRectifyDefectAsync()
    {
        if (SelectedDefect == null)
        {
            _shell?.ShowToast("请选择待整改的质控缺陷", "Warning");
            return;
        }
        if (string.IsNullOrWhiteSpace(DoctorFixReason))
        {
            _shell?.ShowToast("请录入医师整改说明", "Warning");
            return;
        }

        try
        {
            var payload = new { RectificationNote = DoctorFixReason.Trim() };
            var res = await _http.PostAsJsonAsync($"/api/v4/emr-qc/defects/{SelectedDefect.Id}/rectify", payload);
            if (res.IsSuccessStatusCode)
            {
                SelectedDefect.Status = "DOCTOR_FIXED";
                SelectedDefect.RectificationNote = DoctorFixReason.Trim();
                StatusMessage = "✓ 医师整改意见已提交至质控中心，等待质控员复核闭环。";
                _shell?.ShowToast("医师整改说明已提交", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"提交整改失败: {err}";
                _shell?.ShowToast("提交整改失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"整改异常: {ex.Message}";
            _shell?.ShowToast($"整改异常: {ex.Message}", "Error");
        }
    }

    private async Task ExecuteVerifyPassAsync()
    {
        if (SelectedDefect == null)
        {
            _shell?.ShowToast("请选择需要复核的缺陷", "Warning");
            return;
        }

        try
        {
            var payload = new { Pass = true, RejectReason = (string?)null };
            var res = await _http.PostAsJsonAsync($"/api/v4/emr-qc/defects/{SelectedDefect.Id}/verify", payload);
            if (res.IsSuccessStatusCode)
            {
                SelectedDefect.Status = "VERIFIED_CLOSED";
                SelectedDefect.VerifiedBy = SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "质控员";
                StatusMessage = "✓ 质控员复核通过，病历缺陷已完全闭环关闭。";
                _shell?.ShowToast("缺陷已复核关闭，整改闭环完成", "Success");

                if (SelectedInpatient != null)
                {
                    await OnInpatientChangedAsync(SelectedInpatient);
                }
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"复核失败: {err}";
                _shell?.ShowToast("复核失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"复核异常: {ex.Message}";
            _shell?.ShowToast($"复核异常: {ex.Message}", "Error");
        }
    }

    private async Task ExecuteVerifyRejectAsync()
    {
        if (SelectedDefect == null)
        {
            _shell?.ShowToast("请选择需要复核的缺陷", "Warning");
            return;
        }

        try
        {
            var payload = new { Pass = false, RejectReason = "整改不彻底，请重新完善病历文书并审签" };
            var res = await _http.PostAsJsonAsync($"/api/v4/emr-qc/defects/{SelectedDefect.Id}/verify", payload);
            if (res.IsSuccessStatusCode)
            {
                SelectedDefect.Status = "REJECTED";
                StatusMessage = "⚠ 缺陷整改不合格，已打回管床医师重新修改。";
                _shell?.ShowToast("已打回医师重新整改", "Warning");

                if (SelectedInpatient != null)
                {
                    await OnInpatientChangedAsync(SelectedInpatient);
                }
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"驳回失败: {err}";
                _shell?.ShowToast("驳回失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"驳回异常: {ex.Message}";
            _shell?.ShowToast($"驳回异常: {ex.Message}", "Error");
        }
    }

    private async Task ExecuteSubmitCrossOrgTrackAsync()
    {
        if (SelectedInpatient == null)
        {
            _shell?.ShowToast("请先选择患者", "Warning");
            return;
        }
        if (string.IsNullOrWhiteSpace(CrossOrgDocId) || string.IsNullOrWhiteSpace(CrossOrgSummary))
        {
            _shell?.ShowToast("请录入外部文书编号与调阅质控意见", "Warning");
            return;
        }

        try
        {
            var payload = new
            {
                PatientId = SelectedInpatient.PatientId,
                SourceOrgCode = CrossOrgSourceCode.Trim(),
                ExternalDocId = CrossOrgDocId.Trim(),
                FindingSummary = CrossOrgSummary.Trim()
            };

            var res = await _http.PostAsJsonAsync("/api/v4/emr-qc/cross-org/tracks", payload);
            if (res.IsSuccessStatusCode)
            {
                CrossOrgDocId = string.Empty;
                CrossOrgSummary = string.Empty;
                IsCrossOrgFormVisible = false;
                await LoadDataAsync();
                StatusMessage = "✓ 跨机构病历质控调阅记录已成功登记归档。";
                _shell?.ShowToast("跨机构质控跟踪已归档", "Success");
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                StatusMessage = $"登记失败: {err}";
                _shell?.ShowToast("登记失败", "Error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"登记异常: {ex.Message}";
            _shell?.ShowToast($"登记异常: {ex.Message}", "Error");
        }
    }

    public void Activate() => _ = LoadDataAsync();
    public void Deactivate() { }
    public void Dispose() => _http.Dispose();
    public bool OnShortcut(string gesture) => false;

    // DTO records for json deserialization
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

    public record EmrQcTaskApiDto(
        string Id,
        string EncounterId,
        string PatientId,
        string TaskType,
        decimal TotalScore,
        string Grade,
        string Status
    );

    public record EmrQcDefectApiDto(
        string Id,
        string TaskId,
        string EncounterId,
        string? DocId,
        string RuleCode,
        string QcDimension,
        string DefectDescription,
        decimal DeductPoints,
        string ResponsibleDoctorId,
        long DeadlineMs,
        string? RectificationNote,
        string Status,
        string? VerifiedBy,
        long? VerifiedAtMs
    );

    public record EmrQcCrossOrgTrackApiDto(
        string Id,
        string PatientId,
        string SourceOrgCode,
        string ExternalDocId,
        string QcReviewerId,
        string FindingSummary,
        string TrackStatus,
        long CreatedAtMs
    );
}
