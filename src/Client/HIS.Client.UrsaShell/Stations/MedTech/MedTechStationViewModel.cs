using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.MedTech;

public class SurgeryScheduleItemModel : ClientModelBase
{
    private string _id = string.Empty;
    private string _scheduleNo = string.Empty;
    private string _encounterId = string.Empty;
    private string _patientName = string.Empty;
    private string _surgeryName = string.Empty;
    private int _surgeryLevel = 1;
    private string _orRoomId = string.Empty;
    private int _tableSeq = 1;
    private string _surgeonName = string.Empty;
    private string _anesthesiologistName = string.Empty;
    private int _status;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string ScheduleNo { get => _scheduleNo; set => SetField(ref _scheduleNo, value); }
    public string EncounterId { get => _encounterId; set => SetField(ref _encounterId, value); }
    public string PatientName { get => _patientName; set => SetField(ref _patientName, value); }
    public string SurgeryName { get => _surgeryName; set => SetField(ref _surgeryName, value); }
    public int SurgeryLevel { get => _surgeryLevel; set => SetField(ref _surgeryLevel, value); }
    public string OrRoomId { get => _orRoomId; set => SetField(ref _orRoomId, value); }
    public int TableSeq { get => _tableSeq; set => SetField(ref _tableSeq, value); }
    public string SurgeonName { get => _surgeonName; set => SetField(ref _surgeonName, value); }
    public string AnesthesiologistName { get => _anesthesiologistName; set => SetField(ref _anesthesiologistName, value); }
    public int Status { get => _status; set { if (SetField(ref _status, value)) { OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanFinish)); } } }

    public string StatusText => Status switch { 0 => "待手术", 1 => "手术中", 2 => "已完成", _ => "已取消" };
    public bool CanStart => Status == 0;
    public bool CanFinish => Status == 1;
}

public class AnesthesiaMedicationItemModel : ClientModelBase
{
    public string DrugName { get; set; } = string.Empty;
    public decimal Dose { get; set; }
    public string DoseUnit { get; set; } = "mg";
    public string Route { get; set; } = "IV";
    public string Remark { get; set; } = string.Empty;
}

public class ExamSlotItemModel : ClientModelBase
{
    private string _id = string.Empty;
    private string _resourceCode = string.Empty;
    private string _resourceName = string.Empty;
    private string _modality = string.Empty;
    private string _slotDate = string.Empty;
    private string _startTime = string.Empty;
    private string _endTime = string.Empty;
    private int _maxCapacity = 10;
    private int _bookedCount = 0;
    private bool _isAvailable = true;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string ResourceCode { get => _resourceCode; set => SetField(ref _resourceCode, value); }
    public string ResourceName { get => _resourceName; set => SetField(ref _resourceName, value); }
    public string Modality { get => _modality; set => SetField(ref _modality, value); }
    public string SlotDate { get => _slotDate; set => SetField(ref _slotDate, value); }
    public string StartTime { get => _startTime; set => SetField(ref _startTime, value); }
    public string EndTime { get => _endTime; set => SetField(ref _endTime, value); }
    public int MaxCapacity { get => _maxCapacity; set { if (SetField(ref _maxCapacity, value)) OnPropertyChanged(nameof(CapacityText)); } }
    public int BookedCount { get => _bookedCount; set { if (SetField(ref _bookedCount, value)) OnPropertyChanged(nameof(CapacityText)); } }
    public bool IsAvailable { get => _isAvailable; set => SetField(ref _isAvailable, value); }
    public string CapacityText => $"{BookedCount}/{MaxCapacity}";
    public string TimeRange => $"{StartTime} - {EndTime}";
}

public class HospitalScheduleItemModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string ServiceItemCode { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public string SlotDate { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string AppointmentStatus { get; set; } = string.Empty;
}

public class SpecimenTimelineItemModel : ClientModelBase
{
    public string Stage { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public long TimestampMs { get; set; }
    public string StorageTemp { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string TimeText => TimestampMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}

public class LabTrendItemModel : ClientModelBase
{
    public string ValNumeric { get; set; } = string.Empty;
    public string ValString { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string RefRange { get; set; } = string.Empty;
    public string AbnormalFlag { get; set; } = string.Empty;
    public int IsCritical { get; set; }
    public long CreatedAtMs { get; set; }
    public string TimeText => CreatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}

public class LabHistoryItemModel : ClientModelBase
{
    public string RequestNo { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ValString { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string RefRange { get; set; } = string.Empty;
    public string AbnormalFlag { get; set; } = string.Empty;
    public long CreatedAtMs { get; set; }
    public string TimeText => CreatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}

public sealed partial class MedTechStationViewModel : ClientModelBase, IShortcutTarget, IPrintContextProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel? _shell;
    private string _searchKeyword = string.Empty;
    private string _statusFilter = "ALL";
    private bool _subscribed;
    private int _selectedTab;

    // Surgery & Anesthesia fields
    private SurgeryScheduleItemModel? _selectedSchedule;
    private string _anesthesiaRecordId = string.Empty;
    private string _asaGrade = string.Empty;
    private string _anesthesiaType = string.Empty;
    private decimal _bloodLossMl;
    private decimal _urineOutputMl;
    private decimal _fluidInputMl;
    private bool _anesthesiaCompleted;

    // Med form
    private string _newMedDrugName = "丙泊酚乳状注射液";
    private decimal _newMedDose = 150m;
    private string _newMedUnit = "mg";
    private string _newMedRoute = "IV";

    // PACU form
    private int _stewardScore = 6;
    private int _aldreteScore = 9;
    private bool _pacuDischargeMet = true;

    // Followup form
    private int _followupDay = 1;
    private int _followupPainScore = 2;
    private bool _followupNausea;
    private string _followupComplications = "无特殊异常，创口愈合良好";

    public MedTechStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        OpenCriticalValueCommand = new RelayCommand(() => _shell?.OpenCriticalValueDrawerCommand.Execute(null));
        OpenWhoSurgeryDrawerCommand = new AsyncRelayCommand(p => OpenSurgerySafetyAsync(p as SurgeryScheduleItemModel ?? SelectedSchedule));
        OpenRefundProofDrawerCommand = new RelayCommand(OpenRefundProofDrawer);
        FilterByStatusCommand = new RelayCommand(p => ApplyFilter(p as string ?? "ALL"));
        RefreshCommand = new RelayCommand(RefreshList);
        ConfirmFeeCommand = new AsyncRelayCommand(p => ConfirmFeeAsync(p as MedTechOrderItemModel));
        CancelOrderCommand = new AsyncRelayCommand(p => CancelOrderAsync(p as MedTechOrderItemModel));
        OpenReportStationCommand = new RelayCommand(p => OpenReportStation(p as MedTechOrderItemModel));

        // P1-B LIS/PACS commands
        SyncLisCommand = new AsyncRelayCommand(SyncLisAsync);
        SyncPacsCommand = new AsyncRelayCommand(SyncPacsAsync);

        // P1-C Surgery & Anesthesia commands
        LoadSchedulesCommand = new AsyncRelayCommand(LoadSchedulesAsync);
        SelectScheduleCommand = new AsyncRelayCommand(p => SelectScheduleAsync(p as SurgeryScheduleItemModel));
        SaveAnesthesiaCommand = new AsyncRelayCommand(SaveAnesthesiaAsync);
        AddMedicationCommand = new AsyncRelayCommand(AddMedicationAsync);
        RecordPacuCommand = new AsyncRelayCommand(RecordPacuAsync);
        RecordFollowupCommand = new AsyncRelayCommand(RecordFollowupAsync);

        // Level 7 commands
        LoadExamSlotsCommand = new AsyncRelayCommand(LoadExamSlotsAsync);
        LoadHospitalScheduleCommand = new AsyncRelayCommand(LoadHospitalScheduleAsync);
        AutoScheduleExamCommand = new AsyncRelayCommand(AutoScheduleExamAsync);
        ScanStageCommand = new AsyncRelayCommand(ScanStageAsync);
        PreCollectCheckCommand = new AsyncRelayCommand(PreCollectCheckAsync);
        LoadSpecimenTimelineCommand = new AsyncRelayCommand(LoadSpecimenTimelineAsync);
        RunAutoVerifyCommand = new AsyncRelayCommand(RunAutoVerifyAsync);
        LoadLabTrendCommand = new AsyncRelayCommand(LoadLabTrendAsync);
        LoadLabHistoryCommand = new AsyncRelayCommand(LoadLabHistoryAsync);

        RebuildFiltered();
        _ = LoadMedTechRequestsAsync();
        _ = LoadSchedulesAsync();
        _ = LoadExamSlotsAsync();
        _ = LoadSpecimenHandoversAsync();
        _ = LoadDicomStudiesAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<MedTechOrderItemModel> FilteredOrders { get; } = new();
    private MedTechOrderItemModel? _selectedOrderItem;
    public MedTechOrderItemModel? SelectedOrderItem
    {
        get => _selectedOrderItem;
        set
        {
            if (SetField(ref _selectedOrderItem, value) && value != null)
            {
                ScheduleRequestId = value.RequestId;
                SchedulePatientId = value.PatientId;
                CheckPatientId = value.PatientId;
                CheckItemCode = value.ItemName;
                AutoVerifyReqId = value.RequestId;
                TrendPatientId = value.PatientId;
            }
        }
    }
    public ObservableCollection<SurgeryScheduleItemModel> SurgerySchedules { get; } = new();
    public ObservableCollection<AnesthesiaMedicationItemModel> AnesthesiaMedications { get; } = new();

    // Level 7 Collections & Properties
    public ObservableCollection<ExamSlotItemModel> ExamSlots { get; } = new();
    public ObservableCollection<HospitalScheduleItemModel> HospitalAppointments { get; } = new();
    private string _selectedModality = "CT";
    public string SelectedModality { get => _selectedModality; set { if (SetField(ref _selectedModality, value)) _ = LoadExamSlotsAsync(); } }
    private string _examSlotDate = DateTime.Now.ToString("yyyy-MM-dd");
    public string ExamSlotDate { get => _examSlotDate; set { if (SetField(ref _examSlotDate, value)) _ = LoadExamSlotsAsync(); } }
    private string _schedulePatientId = string.Empty;
    public string SchedulePatientId { get => _schedulePatientId; set => SetField(ref _schedulePatientId, value); }
    private string _scheduleRequestId = string.Empty;
    public string ScheduleRequestId { get => _scheduleRequestId; set => SetField(ref _scheduleRequestId, value); }
    private string _autoScheduleResult = string.Empty;
    public string AutoScheduleResult { get => _autoScheduleResult; set => SetField(ref _autoScheduleResult, value); }

    public ObservableCollection<SpecimenTimelineItemModel> SpecimenTimeline { get; } = new();
    private string _scanBarcode = string.Empty;
    public string ScanBarcode { get => _scanBarcode; set => SetField(ref _scanBarcode, value); }
    private string _scanStage = "COLLECTED";
    public string ScanStage { get => _scanStage; set => SetField(ref _scanStage, value); }
    private string _storageTemperature = "4℃";
    public string StorageTemperature { get => _storageTemperature; set => SetField(ref _storageTemperature, value); }
    private string _scanNote = string.Empty;
    public string ScanNote { get => _scanNote; set => SetField(ref _scanNote, value); }
    private string _wristbandCheckBarcode = string.Empty;
    public string WristbandCheckBarcode { get => _wristbandCheckBarcode; set => SetField(ref _wristbandCheckBarcode, value); }
    private string _tubeCheckBarcode = string.Empty;
    public string TubeCheckBarcode { get => _tubeCheckBarcode; set => SetField(ref _tubeCheckBarcode, value); }
    private string _checkItemCode = "LAB_BLOOD_ROUTINE";
    public string CheckItemCode { get => _checkItemCode; set => SetField(ref _checkItemCode, value); }
    private string _checkPatientId = string.Empty;
    public string CheckPatientId { get => _checkPatientId; set => SetField(ref _checkPatientId, value); }
    private string _checkResultText = string.Empty;
    public string CheckResultText { get => _checkResultText; set => SetField(ref _checkResultText, value); }

    public ObservableCollection<LabTrendItemModel> LabTrends { get; } = new();
    public ObservableCollection<LabHistoryItemModel> LabHistoryList { get; } = new();
    private string _autoVerifyReqId = string.Empty;
    public string AutoVerifyReqId { get => _autoVerifyReqId; set => SetField(ref _autoVerifyReqId, value); }
    private string _trendPatientId = string.Empty;
    public string TrendPatientId { get => _trendPatientId; set => SetField(ref _trendPatientId, value); }
    private string _trendItemCode = "WBC";
    public string TrendItemCode { get => _trendItemCode; set => SetField(ref _trendItemCode, value); }
    private string _autoVerifyResultSummary = string.Empty;
    public string AutoVerifyResultSummary { get => _autoVerifyResultSummary; set => SetField(ref _autoVerifyResultSummary, value); }

    public int SelectedTab
    {
        get => _selectedTab;
        set => SetField(ref _selectedTab, value);
    }

    public SurgeryScheduleItemModel? SelectedSchedule
    {
        get => _selectedSchedule;
        set
        {
            if (SetField(ref _selectedSchedule, value))
            {
                OnPropertyChanged(nameof(HasSelectedSchedule));
            }
        }
    }

    public bool HasSelectedSchedule => SelectedSchedule != null;

    public string AsaGrade { get => _asaGrade; set => SetField(ref _asaGrade, value); }
    public string AnesthesiaType { get => _anesthesiaType; set => SetField(ref _anesthesiaType, value); }
    public decimal BloodLossMl { get => _bloodLossMl; set => SetField(ref _bloodLossMl, value); }
    public decimal UrineOutputMl { get => _urineOutputMl; set => SetField(ref _urineOutputMl, value); }
    public decimal FluidInputMl { get => _fluidInputMl; set => SetField(ref _fluidInputMl, value); }
    public bool AnesthesiaCompleted { get => _anesthesiaCompleted; set => SetField(ref _anesthesiaCompleted, value); }

    public string NewMedDrugName { get => _newMedDrugName; set => SetField(ref _newMedDrugName, value); }
    public decimal NewMedDose { get => _newMedDose; set => SetField(ref _newMedDose, value); }
    public string NewMedUnit { get => _newMedUnit; set => SetField(ref _newMedUnit, value); }
    public string NewMedRoute { get => _newMedRoute; set => SetField(ref _newMedRoute, value); }

    public int StewardScore { get => _stewardScore; set => SetField(ref _stewardScore, value); }
    public int AldreteScore { get => _aldreteScore; set => SetField(ref _aldreteScore, value); }
    public bool PacuDischargeMet { get => _pacuDischargeMet; set => SetField(ref _pacuDischargeMet, value); }

    public int FollowupDay { get => _followupDay; set => SetField(ref _followupDay, value); }
    public int FollowupPainScore { get => _followupPainScore; set => SetField(ref _followupPainScore, value); }
    public bool FollowupNausea { get => _followupNausea; set => SetField(ref _followupNausea, value); }
    public string FollowupComplications { get => _followupComplications; set => SetField(ref _followupComplications, value); }

    public Action? FocusSearchAction { get; set; }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetField(ref _searchKeyword, value))
            {
                RebuildFiltered();
            }
        }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        private set
        {
            if (SetField(ref _statusFilter, value))
            {
                OnPropertyChanged(nameof(IsAllFilter));
                OnPropertyChanged(nameof(IsPendingFeeFilter));
                OnPropertyChanged(nameof(IsLockedFilter));
                OnPropertyChanged(nameof(IsReportedFilter));
                RebuildFiltered();
            }
        }
    }

    public bool IsAllFilter => StatusFilter == "ALL";
    public bool IsPendingFeeFilter => StatusFilter == "PENDING";
    public bool IsLockedFilter => StatusFilter == "LOCKED";
    public bool IsReportedFilter => StatusFilter == "REPORTED";

    public int TotalCount => Workbench.MedTechOrders.Count;
    public int PendingFeeCount => Workbench.MedTechOrders.Count(o => o.CanConfirmFee);
    public int LockedCount => Workbench.MedTechOrders.Count(o => o.IsFeeLocked && o.Status != "已出报告");
    public int ReportedCount => Workbench.MedTechOrders.Count(o => o.Status == "已出报告");

    public ICommand OpenCriticalValueCommand { get; }
    public ICommand OpenWhoSurgeryDrawerCommand { get; }
    public ICommand OpenRefundProofDrawerCommand { get; }
    public ICommand FilterByStatusCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ConfirmFeeCommand { get; }
    public ICommand CancelOrderCommand { get; }
    public ICommand OpenReportStationCommand { get; }

    // P1-B & P1-C commands
    public ICommand SyncLisCommand { get; }
    public ICommand SyncPacsCommand { get; }
    public ICommand LoadSchedulesCommand { get; }
    public ICommand SelectScheduleCommand { get; }
    public ICommand SaveAnesthesiaCommand { get; }
    public ICommand AddMedicationCommand { get; }
    public ICommand RecordPacuCommand { get; }
    public ICommand RecordFollowupCommand { get; }

    // Level 7 commands
    public ICommand LoadExamSlotsCommand { get; }
    public ICommand LoadHospitalScheduleCommand { get; }
    public ICommand AutoScheduleExamCommand { get; }
    public ICommand ScanStageCommand { get; }
    public ICommand PreCollectCheckCommand { get; }
    public ICommand LoadSpecimenTimelineCommand { get; }
    public ICommand RunAutoVerifyCommand { get; }
    public ICommand LoadLabTrendCommand { get; }
    public ICommand LoadLabHistoryCommand { get; }

    public async Task SyncLisAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsync("/api/v4/medtech/lis/sync-results", null);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<ApiResponse<int>>(JsonOptions);
                _shell?.ShowToast(body?.Message ?? "LIS 结果批量同步成功", "Success");
                RefreshList();
            }
            else
            {
                _shell?.ShowToast("LIS 结果同步失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"LIS 同步异常: {ex.Message}", "Error");
        }
    }

    public async Task SyncPacsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsync("/api/v4/medtech/pacs/sync-reports", null);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<ApiResponse<int>>(JsonOptions);
                _shell?.ShowToast(body?.Message ?? "PACS 报告批量同步成功", "Success");
                RefreshList();
            }
            else
            {
                _shell?.ShowToast("PACS 报告同步失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"PACS 同步异常: {ex.Message}", "Error");
        }
    }

    public async Task LoadSchedulesAsync()
    {
        var context = SurgeryContext();
        var generation = ++_scheduleLoadGeneration;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<SurgeryScheduleItemModel>>>("/api/v4/medtech/surgeries/schedules", JsonOptions);
            if (context != SurgeryContext() || generation != _scheduleLoadGeneration) return;
            if (res is { Code: 200, Data: not null })
            {
                SurgerySchedules.Clear();
                foreach (var s in res.Data)
                {
                    SurgerySchedules.Add(s);
                }
                if (SelectedSchedule != null && !SurgerySchedules.Any(x => x.Id == SelectedSchedule.Id))
                    await SelectScheduleAsync(null);
            }
            else _shell?.ShowToast(res?.Message ?? "未取得手术排班", "Warning");
        }
        catch
        {
            if (context == SurgeryContext() && generation == _scheduleLoadGeneration)
                _shell?.ShowToast("无法刷新手术排班，请重试。", "Warning");
        }
    }

    public async Task SelectScheduleAsync(SurgeryScheduleItemModel? schedule)
    {
        SelectedSchedule = schedule;
        var context = SurgeryContext();
        var generation = ++_anesthesiaSelectionGeneration;
        bool Current() => generation == _anesthesiaSelectionGeneration && SurgeryContext() == context;
        _anesthesiaRecordId = string.Empty;
        AsaGrade = AnesthesiaType = string.Empty;
        BloodLossMl = UrineOutputMl = FluidInputMl = 0;
        AnesthesiaCompleted = false;
        AnesthesiaMedications.Clear();
        if (schedule == null) return;

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var res = await http.GetAsync($"/api/v4/medtech/anesthesia/records/by-schedule/{Uri.EscapeDataString(schedule.Id)}");
            var result = await res.Content.ReadFromJsonAsync<ApiResponse<AnesthesiaReceipt>>(JsonOptions);
            if (!Current()) return;
            if (res.IsSuccessStatusCode && result is { Code: 200, Data: { } data } && data.ScheduleId == schedule.Id)
            {
                _anesthesiaRecordId = data.Id;
                AsaGrade = data.AsaGrade;
                AnesthesiaType = data.AnesthesiaType;
                BloodLossMl = data.BloodLossMl;
                UrineOutputMl = data.UrineOutputMl;
                FluidInputMl = data.FluidInputMl;
                AnesthesiaCompleted = data.Status == 1;
                var medRes = await http.GetFromJsonAsync<ApiResponse<List<AnesthesiaMedicationItemModel>>>(
                    $"/api/v4/medtech/anesthesia/records/{data.Id}/medications", JsonOptions);
                if (Current() && medRes is { Code: 200, Data: not null })
                    foreach (var m in medRes.Data) AnesthesiaMedications.Add(m);
            }
            else if (result?.Code != 404) _shell?.ShowToast(result?.Message ?? "未取得匹配的麻醉记录", "Warning");
        }
        catch
        {
            if (Current()) _shell?.ShowToast("麻醉记录查询未确认，请重新选择排班。", "Warning");
        }
    }

    public async Task SaveAnesthesiaAsync()
    {
        if (SelectedSchedule is not { } schedule) return;
        var context = SurgeryContext();
        var generation = _anesthesiaSelectionGeneration;
        bool Current() => generation == _anesthesiaSelectionGeneration && context == SurgeryContext();
        var updateReq = new { AsaGrade, AnesthesiaType, BloodLossMl, UrineOutputMl, FluidInputMl, MarkCompleted = AnesthesiaCompleted };
        if (string.IsNullOrWhiteSpace(AsaGrade) || string.IsNullOrWhiteSpace(AnesthesiaType))
        {
            _shell?.ShowToast("请填写实际 ASA 分级和麻醉方式。", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            if (string.IsNullOrWhiteSpace(_anesthesiaRecordId))
            {
                using var createRes = await http.PostAsJsonAsync("/api/v4/medtech/anesthesia/records", new { ScheduleId = schedule.Id, AsaGrade, AnesthesiaType });
                var result = await createRes.Content.ReadFromJsonAsync<ApiResponse<AnesthesiaReceipt>>(JsonOptions);
                if (!Current()) return;
                if (!createRes.IsSuccessStatusCode || result is not { Code: 200, Data: { } receipt } || receipt.ScheduleId != schedule.Id || string.IsNullOrWhiteSpace(receipt.Id))
                {
                    _shell?.ShowToast(result?.Message ?? "未确认麻醉记录创建，请查询原记录。", "Warning");
                    return;
                }
                _anesthesiaRecordId = receipt.Id;
            }

            if (!string.IsNullOrWhiteSpace(_anesthesiaRecordId))
            {
                var id = _anesthesiaRecordId;
                using var updateRes = await http.PutAsJsonAsync($"/api/v4/medtech/anesthesia/records/{id}", updateReq);
                var result = await updateRes.Content.ReadFromJsonAsync<ApiResponse<AnesthesiaReceipt>>(JsonOptions);
                if (!Current()) return;
                if (updateRes.IsSuccessStatusCode && result is { Code: 200, Data: { } saved } && saved.Id == id &&
                    saved.ScheduleId == schedule.Id && saved.Status == (updateReq.MarkCompleted ? 1 : 0))
                    _shell?.ShowToast(result.Message, "Success");
                else _shell?.ShowToast(result?.Code != 200 ? result?.Message ?? "麻醉记录保存未确认。" : "未取得匹配回执，请查询原记录。", "Warning");
            }
        }
        catch (Exception ex)
        {
            if (Current()) _shell?.ShowToast($"麻醉记录结果待核对，请先查询原记录。{ex.Message}", "Warning");
        }
    }

    public async Task AddMedicationAsync()
    {
        if (string.IsNullOrWhiteSpace(_anesthesiaRecordId))
        {
            await SaveAnesthesiaAsync();
        }
        if (string.IsNullOrWhiteSpace(_anesthesiaRecordId)) return;

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                DrugId = "DRUG-ANES-01",
                DrugName = NewMedDrugName,
                Dose = NewMedDose,
                DoseUnit = NewMedUnit,
                Route = NewMedRoute,
                Remark = "术中维持"
            };
            var res = await http.PostAsJsonAsync($"/api/v4/medtech/anesthesia/records/{_anesthesiaRecordId}/medications", payload);
            if (res.IsSuccessStatusCode)
            {
                AnesthesiaMedications.Add(new AnesthesiaMedicationItemModel
                {
                    DrugName = NewMedDrugName,
                    Dose = NewMedDose,
                    DoseUnit = NewMedUnit,
                    Route = NewMedRoute,
                    Remark = "术中维持"
                });
                _shell?.ShowToast($"已记录麻醉用药：{NewMedDrugName} {NewMedDose}{NewMedUnit}", "Success");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"用药记录失败: {ex.Message}", "Error");
        }
    }

    public async Task RecordPacuAsync()
    {
        if (string.IsNullOrWhiteSpace(_anesthesiaRecordId))
        {
            _shell?.ShowToast("请先保存麻醉记录", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                PacuBedNo = "PACU-02",
                StewardScore,
                AldreteScore,
                Consciousness = "完全清醒",
                Respiration = "平稳自如",
                Circulation = "血压心率正常",
                Spo2 = 99m,
                DischargeCriteriaMet = PacuDischargeMet,
                Remark = "达到 PACU 转出病房标准"
            };
            var res = await http.PostAsJsonAsync($"/api/v4/medtech/anesthesia/records/{_anesthesiaRecordId}/recovery", payload);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast("PACU 复苏室评分评估已保存", "Success");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"PACU 评估保存失败: {ex.Message}", "Error");
        }
    }

    public async Task RecordFollowupAsync()
    {
        if (string.IsNullOrWhiteSpace(_anesthesiaRecordId))
        {
            _shell?.ShowToast("请先选择手术排班并保存麻醉记录", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                FollowupDay,
                PainScore = FollowupPainScore,
                NauseaVomiting = FollowupNausea,
                ConsciousnessRecovery = true,
                ThroatPain = false,
                Complications = FollowupComplications
            };
            var res = await http.PostAsJsonAsync($"/api/v4/medtech/anesthesia/records/{_anesthesiaRecordId}/followup", payload);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"术后第 {FollowupDay} 天随访记录已保存", "Success");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"随访保存失败: {ex.Message}", "Error");
        }
    }

    public void Attach()
    {
        if (!_subscribed)
        {
            Workbench.PropertyChanged += OnWorkbenchPropertyChanged;
            Workbench.MedTechOrders.CollectionChanged += OnOrdersCollectionChanged;
            _subscribed = true;
        }
        RebuildFiltered();
        RaiseCounts();
    }

    public void Detach()
    {
        if (_subscribed)
        {
            Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
            Workbench.MedTechOrders.CollectionChanged -= OnOrdersCollectionChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                OpenWhoSurgeryDrawerCommand.Execute(null);
                return true;
            case "F3":
            case "Ctrl+F":
                FocusSearchAction?.Invoke();
                return true;
            case "F5":
                RefreshList();
                return true;
            case "Escape" when !string.IsNullOrEmpty(SearchKeyword):
                SearchKeyword = string.Empty;
                return true;
            default:
                return false;
        }
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.MedTechOrders)
            or nameof(WorkbenchModel.HasFeeLockedMedTechOrder)
            or nameof(WorkbenchModel.FeeLockedMedTechOrderText))
        {
            RebuildFiltered();
            RaiseCounts();
        }
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFiltered();
        RaiseCounts();
    }

    private void ApplyFilter(string mode) => StatusFilter = mode;

    private sealed class MtcServiceRequestRow
    {
        public string Id { get; set; } = string.Empty;
        public string RequestNo { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string OrderDeptId { get; set; } = string.Empty;
        public string ServiceItemCode { get; set; } = string.Empty;
        public string ExecRoomCode { get; set; } = string.Empty;
        public decimal FeeAmount { get; set; }
        public short IsFeeConfirmed { get; set; }
        public short Status { get; set; }
    }

    public async Task LoadMedTechRequestsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<MtcServiceRequestRow>>>("/api/v4/medtech/requests", JsonOptions);
            if ((res?.Code is 200 or 0) && res.Data != null && res.Data.Count > 0)
            {
                Workbench.MedTechOrders.Clear();
                foreach (var r in res.Data)
                {
                    bool isLocked = r.IsFeeConfirmed == 1;
                    string status = r.Status switch
                    {
                        0 => "待核收确费",
                        1 => "核收中",
                        2 => "已核收确费 (待出报告)",
                        3 => "已出报告",
                        4 => "已撤单退回",
                        _ => "未知状态"
                    };
                    string statusClass = r.Status switch
                    {
                        2 => "Primary",
                        3 => "Success",
                        4 => "Secondary",
                        _ => "Warning"
                    };
                    Workbench.MedTechOrders.Add(new MedTechOrderItemModel
                    {
                        RequestId = r.Id,
                        RequestNo = r.RequestNo,
                        PatientId = r.PatientId,
                        ItemName = r.ServiceItemCode,
                        DeptName = r.OrderDeptId,
                        ExecLocation = string.IsNullOrWhiteSpace(r.ExecRoomCode) ? "医技科室" : r.ExecRoomCode,
                        Fee = $"¥{r.FeeAmount:0.00}",
                        IsFeeLocked = isLocked,
                        Status = status,
                        StatusClass = statusClass
                    });
                }
            }
        }
        catch { }
        RebuildFiltered();
        RaiseCounts();
    }

    public async Task ConfirmFeeAsync(MedTechOrderItemModel? order)
    {
        if (order == null) return;
        if (order.IsFeeLocked)
        {
            _shell?.ShowToast($"申请单 [{order.RequestNo}] 已经完成核收确费锁单", "Info");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string targetId = !string.IsNullOrWhiteSpace(order.RequestId) ? order.RequestId : order.RequestNo;
            var resp = await http.PostAsync($"/api/v4/medtech/requests/{Uri.EscapeDataString(targetId)}/confirm-fee-and-receive", null);
            var res = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<MtcServiceRequestRow>>(JsonOptions);
            if (resp.IsSuccessStatusCode && (res?.Code is 200 or 0))
            {
                order.IsFeeLocked = true;
                order.Status = "已核收确费 (待出报告)";
                order.StatusClass = "Primary";
                _shell?.ShowToast($"申请单 [{order.RequestNo}] 已成功核收确费并锁单，现可出具报告", "Success");
                RebuildFiltered();
                RaiseCounts();
            }
            else
            {
                string msg = res?.Message ?? $"核收失败 ({resp.StatusCode})";
                _shell?.ShowToast($"核收确费拦截: {msg}", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"确费异常: {ex.Message}", "Error");
        }
    }

    public async Task CancelOrderAsync(MedTechOrderItemModel? order)
    {
        if (order == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string targetId = !string.IsNullOrWhiteSpace(order.RequestId) ? order.RequestId : order.RequestNo;
            var resp = await http.PostAsync($"/api/v4/medtech/requests/{Uri.EscapeDataString(targetId)}/cancel", null);
            var res = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<bool>>(JsonOptions);
            if (resp.IsSuccessStatusCode && (res?.Code is 200 or 0))
            {
                order.Status = "已撤单退回";
                order.StatusClass = "Secondary";
                _shell?.ShowToast($"申请单 [{order.RequestNo}] 已成功撤单", "Success");
                RebuildFiltered();
                RaiseCounts();
            }
            else
            {
                string msg = res?.Message ?? $"撤单失败 ({resp.StatusCode})";
                _shell?.ShowToast($"撤单拦截: {msg}", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"撤单异常: {ex.Message}", "Error");
        }
    }

    private void OpenReportStation(MedTechOrderItemModel? order)
    {
        _shell?.NavigateToStation("medtech_report");
        _shell?.ShowToast($"已切换到「医技报告与审签」工作站，请核对患者与申请并录入报告结论。", "Info");
    }

    private void RefreshList()
    {
        SearchKeyword = string.Empty;
        StatusFilter = "ALL";
        _ = LoadMedTechRequestsAsync();
        _shell?.ShowToast($"已刷新医技申请列表（共 {TotalCount} 条）。", "Info");
    }

    private void RebuildFiltered()
    {
        FilteredOrders.Clear();
        string kw = (SearchKeyword ?? string.Empty).Trim();

        foreach (var item in Workbench.MedTechOrders)
        {
            if (StatusFilter == "PENDING" && !item.CanConfirmFee) continue;
            if (StatusFilter == "LOCKED" && !(item.IsFeeLocked && item.Status != "已出报告")) continue;
            if (StatusFilter == "REPORTED" && item.Status != "已出报告") continue;

            if (!string.IsNullOrEmpty(kw))
            {
                bool hit = item.RequestNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.ItemName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.DeptName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.ExecLocation.Contains(kw, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            FilteredOrders.Add(item);
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PendingFeeCount));
        OnPropertyChanged(nameof(LockedCount));
        OnPropertyChanged(nameof(ReportedCount));
    }

    private void OpenRefundProofDrawer()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(Workbench.FeeLockedMedTechOrderText)
                ? "当前无可撤单锁单异常项；已完成确费的申请单不可直接撤单。"
                : Workbench.FeeLockedMedTechOrderText,
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(6),
            Background = ThemeBrushes.WarningLight,
            BorderBrush = ThemeBrushes.Warning,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new TextBlock
            {
                Text = "冲正规定：已确费锁单项目不得单向撤销，须凭医技科室主任签章的《医技冲正退费证明单》前往收费处办理冲正退费。",
                FontSize = 12,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        });
        _shell?.OpenDrawer("医技确费锁单与退费依据", "防误退费审计规范", panel, 460);
    }

    public async Task LoadExamSlotsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/medtech/exam/slots?modality={Uri.EscapeDataString(_selectedModality ?? "")}&slotDate={Uri.EscapeDataString(_examSlotDate ?? "")}";
            var res = await http.GetFromJsonAsync<ApiResponse<List<ExamSlotItemModel>>>(url, JsonOptions);
            if (res?.Data != null)
            {
                ExamSlots.Clear();
                foreach (var s in res.Data)
                {
                    ExamSlots.Add(s);
                }
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"获取机位时隙异常: {ex.Message}", "Error");
        }
    }

    public async Task LoadHospitalScheduleAsync()
    {
        if (string.IsNullOrWhiteSpace(_schedulePatientId))
        {
            _shell?.ShowToast("请输入患者ID", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/medtech/exam/hospital-schedule?patientId={Uri.EscapeDataString(_schedulePatientId.Trim())}";
            var res = await http.GetFromJsonAsync<ApiResponse<List<HospitalScheduleItemModel>>>(url, JsonOptions);
            if (res?.Data != null)
            {
                HospitalAppointments.Clear();
                foreach (var a in res.Data)
                {
                    HospitalAppointments.Add(a);
                }
                _shell?.ShowToast($"已加载患者预约排程 {HospitalAppointments.Count} 条", "Info");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"获取患者排程异常: {ex.Message}", "Error");
        }
    }

    public async Task AutoScheduleExamAsync()
    {
        if (string.IsNullOrWhiteSpace(_scheduleRequestId))
        {
            _shell?.ShowToast("请输入检查申请单ID", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                RequestId = _scheduleRequestId.Trim(),
                PreferredDate = _examSlotDate,
                PreferredModality = _selectedModality
            };
            var res = await http.PostAsJsonAsync("/api/v4/medtech/exam/auto-schedule", payload);
            var content = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                AutoScheduleResult = "✓ 智能冲突排程成功，预约时隙已锁定";
                _shell?.ShowToast("智能排程成功", "Success");
                _ = LoadExamSlotsAsync();
                if (!string.IsNullOrWhiteSpace(_schedulePatientId))
                    _ = LoadHospitalScheduleAsync();
            }
            else
            {
                AutoScheduleResult = $"排程拦截: {content}";
                _shell?.ShowToast($"排程失败: {content}", "Warning");
            }
        }
        catch (Exception ex)
        {
            AutoScheduleResult = $"异常: {ex.Message}";
            _shell?.ShowToast($"自动排程异常: {ex.Message}", "Error");
        }
    }

    public async Task PreCollectCheckAsync()
    {
        if (string.IsNullOrWhiteSpace(_wristbandCheckBarcode) || string.IsNullOrWhiteSpace(_checkPatientId))
        {
            _shell?.ShowToast("请输入患者腕带条码与患者ID", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                PatientId = _checkPatientId.Trim(),
                WristbandBarcode = _wristbandCheckBarcode.Trim(),
                TubeBarcode = _tubeCheckBarcode.Trim(),
                ItemCode = _checkItemCode.Trim()
            };
            var res = await http.PostAsJsonAsync("/api/v4/medtech/specimens/pre-collect-check", payload);
            var content = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                CheckResultText = "✓ 采血前双条码核对通过：患者身份与试管条码一致";
                _shell?.ShowToast("双条码核对通过", "Success");
            }
            else
            {
                CheckResultText = $"【409 拦截】{content}";
                _shell?.ShowToast("条码核验拦截，严禁采血！", "Error");
            }
        }
        catch (Exception ex)
        {
            CheckResultText = $"核对异常: {ex.Message}";
            _shell?.ShowToast($"核对异常: {ex.Message}", "Error");
        }
    }

    public async Task ScanStageAsync()
    {
        if (string.IsNullOrWhiteSpace(_scanBarcode))
        {
            _shell?.ShowToast("请扫描或输入标本条码", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                Barcode = _scanBarcode.Trim(),
                Stage = _scanStage,
                Temperature = _storageTemperature,
                Note = _scanNote
            };
            var res = await http.PostAsJsonAsync($"/api/v4/medtech/specimens/{Uri.EscapeDataString(_scanBarcode.Trim())}/scan-stage", payload);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"标本全流程节点 [{_scanStage}] 扫码交接成功", "Success");
                _ = LoadSpecimenTimelineAsync();
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                _shell?.ShowToast($"标本流转记录失败: {err}", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"标本扫码异常: {ex.Message}", "Error");
        }
    }

    public async Task LoadSpecimenTimelineAsync()
    {
        if (string.IsNullOrWhiteSpace(_scanBarcode)) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<SpecimenTimelineItemModel>>>($"/api/v4/medtech/requests/{Uri.EscapeDataString(_scanBarcode.Trim())}/timeline", JsonOptions);
            if (res?.Data != null)
            {
                SpecimenTimeline.Clear();
                foreach (var log in res.Data)
                {
                    SpecimenTimeline.Add(log);
                }
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"获取标本轨迹异常: {ex.Message}", "Error");
        }
    }

    public async Task RunAutoVerifyAsync()
    {
        if (string.IsNullOrWhiteSpace(_autoVerifyReqId))
        {
            _shell?.ShowToast("请输入待审核申请单ID", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsync($"/api/v4/medtech/lab/auto-verify/{Uri.EscapeDataString(_autoVerifyReqId.Trim())}", null);
            var content = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
            {
                AutoVerifyResultSummary = "✓ 智能自动审核规则引擎通过，已完成结构化结果归档与自动电子签名";
                _shell?.ShowToast("智能自动审核通过", "Success");
                RefreshList();
            }
            else
            {
                AutoVerifyResultSummary = $"【422 自动审核拦截】{content}";
                _shell?.ShowToast("自动审核未通过，已转人工双签复审", "Warning");
            }
        }
        catch (Exception ex)
        {
            AutoVerifyResultSummary = $"审核异常: {ex.Message}";
            _shell?.ShowToast($"自动审核异常: {ex.Message}", "Error");
        }
    }

    public async Task LoadLabTrendAsync()
    {
        if (string.IsNullOrWhiteSpace(_trendPatientId) || string.IsNullOrWhiteSpace(_trendItemCode))
        {
            _shell?.ShowToast("请输入患者ID与项目代码", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/medtech/lab/trend?patientId={Uri.EscapeDataString(_trendPatientId.Trim())}&itemCode={Uri.EscapeDataString(_trendItemCode.Trim())}";
            var res = await http.GetFromJsonAsync<ApiResponse<List<LabTrendItemModel>>>(url, JsonOptions);
            if (res?.Data != null)
            {
                LabTrends.Clear();
                foreach (var item in res.Data)
                {
                    LabTrends.Add(item);
                }
                _shell?.ShowToast($"已加载 [{_trendItemCode}] 历史趋势 {LabTrends.Count} 次", "Info");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"获取检验趋势异常: {ex.Message}", "Error");
        }
    }

    public async Task LoadLabHistoryAsync()
    {
        if (string.IsNullOrWhiteSpace(_trendPatientId))
        {
            _shell?.ShowToast("请输入患者ID", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/medtech/lab/history?patientId={Uri.EscapeDataString(_trendPatientId.Trim())}";
            var res = await http.GetFromJsonAsync<ApiResponse<List<LabHistoryItemModel>>>(url, JsonOptions);
            if (res?.Data != null)
            {
                LabHistoryList.Clear();
                foreach (var h in res.Data)
                {
                    LabHistoryList.Add(h);
                }
                _shell?.ShowToast($"已加载患者历史检验记录 {LabHistoryList.Count} 条", "Info");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"获取检验历史异常: {ex.Message}", "Error");
        }
    }

    public Dictionary<string, string> BuildPrintContext(object? rowContext = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pat = Workbench.SelectedPatient;
        if (pat != null)
        {
            ctx["patient.fullName"] = pat.PatientName ?? string.Empty;
            ctx["patient.gender"] = pat.Gender ?? string.Empty;
            ctx["patient.age"] = pat.Age.ToString();
            ctx["patient.medicalRecordNo"] = pat.MedicalRecordNo ?? string.Empty;
            ctx["patient.visitNo"] = pat.VisitNo ?? string.Empty;
            ctx["patient.deptName"] = pat.DeptName ?? string.Empty;
            ctx["patient.doctorName"] = pat.DoctorName ?? string.Empty;
        }
        var order = rowContext as MedTechOrderItemModel ?? FilteredOrders.FirstOrDefault();
        if (order != null)
        {
            ctx["medtech.requestNo"] = order.RequestNo ?? string.Empty;
            ctx["medtech.itemName"] = order.ItemName ?? string.Empty;
            ctx["medtech.deptName"] = order.DeptName ?? string.Empty;
            ctx["medtech.execLocation"] = order.ExecLocation ?? string.Empty;
            ctx["medtech.fee"] = order.Fee ?? string.Empty;
            ctx["medtech.status"] = order.Status ?? string.Empty;
        }
        return ctx;
    }
}
