using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.Stations.MedTech;

public sealed class SpecimenHandoverItemVm : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string HandoverNo { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string? WardId { get; set; }
    public string SampleBarcode { get; set; } = string.Empty;
    public string DrawnByNurseId { get; set; } = string.Empty;
    public long DrawnAtMs { get; set; }
    public string? CourierId { get; set; }
    public long? DispatchedAtMs { get; set; }
    public string? LabReceiverId { get; set; }
    public long? ReceivedAtMs { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public string? Notes { get; set; }

    public string StatusDisplay => Status switch
    {
        "DRAWN" => "已采样待转运",
        "IN_TRANSIT" => "外勤运送中",
        "RECEIVED" => "检验科已核收",
        "REJECTED" => "已拒收退回",
        _ => Status
    };

    public string TimeText => DrawnAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(DrawnAtMs).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss")
        : "-";

    public string SummaryLine => $"{HandoverNo} | 条码 {SampleBarcode} | 申请 {RequestId} | 患者 {PatientId} | {StatusDisplay}"
        + (!string.IsNullOrWhiteSpace(CourierId) ? $" | 物流:{CourierId}" : "")
        + (!string.IsNullOrWhiteSpace(LabReceiverId) ? $" | 核收:{LabReceiverId}" : "")
        + (!string.IsNullOrWhiteSpace(RejectionReason) ? $" | 退因:{RejectionReason}" : "");
}

public sealed class DicomStudySummaryVm
{
    public string Id { get; set; } = string.Empty;
    public string StudyInstanceUid { get; set; } = string.Empty;
    public string AccessionNo { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public int SeriesCount { get; set; }
    public int InstanceCount { get; set; }
    public long StudyTimeMs { get; set; }

    public string StudyTimeText => StudyTimeMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(StudyTimeMs).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss")
        : "-";

    public string SummaryLine => $"[{Modality}] 检查号 {AccessionNo} | 患者 {PatientId} | {SeriesCount} 序列 / {InstanceCount} 影像帧 | {StudyTimeText} | UID {StudyInstanceUid}";
}

public sealed partial class MedTechStationViewModel
{
    private static readonly JsonSerializerOptions CourierJsonOpts = new() { PropertyNameCaseInsensitive = true };

    private SpecimenHandoverItemVm? _selectedSpecimenHandover;
    private DicomStudySummaryVm? _selectedDicomStudy;
    private string _specimenDrawRequestId = string.Empty;
    private string _specimenDrawPatientId = "PAT85061801";
    private string _specimenDrawBarcode = $"BAR-{DateTime.Now:HHmmss}";
    private string _specimenCourierId = "COURIER-01";
    private string _specimenRejectReason = string.Empty;
    private string _specimenHandoverStatusText = "标本全链路：采血注册条码 -> 外勤物流运送 -> 检验科核收/退回重采";
    private string _dicomFilterPatientId = string.Empty;
    private string _dicomStatusText = "PACS / DICOM 影像检查研究元数据查询";

    private ICommand? _loadSpecimenHandoversCommand;
    private ICommand? _drawSpecimenCommand;
    private ICommand? _dispatchSpecimenCommand;
    private ICommand? _receiveSpecimenCommand;
    private ICommand? _rejectSpecimenCommand;
    private ICommand? _loadDicomStudiesCommand;

    public ObservableCollection<SpecimenHandoverItemVm> SpecimenHandovers { get; } = new();
    public ObservableCollection<DicomStudySummaryVm> DicomStudies { get; } = new();

    public SpecimenHandoverItemVm? SelectedSpecimenHandover
    {
        get => _selectedSpecimenHandover;
        set
        {
            if (SetField(ref _selectedSpecimenHandover, value) && value != null)
            {
                ScanBarcode = value.SampleBarcode;
            }
        }
    }

    public DicomStudySummaryVm? SelectedDicomStudy
    {
        get => _selectedDicomStudy;
        set => SetField(ref _selectedDicomStudy, value);
    }

    public string SpecimenDrawRequestId { get => _specimenDrawRequestId; set => SetField(ref _specimenDrawRequestId, value); }
    public string SpecimenDrawPatientId { get => _specimenDrawPatientId; set => SetField(ref _specimenDrawPatientId, value); }
    public string SpecimenDrawBarcode { get => _specimenDrawBarcode; set => SetField(ref _specimenDrawBarcode, value); }
    public string SpecimenCourierId { get => _specimenCourierId; set => SetField(ref _specimenCourierId, value); }
    public string SpecimenRejectReason { get => _specimenRejectReason; set => SetField(ref _specimenRejectReason, value); }
    public string SpecimenHandoverStatusText { get => _specimenHandoverStatusText; set => SetField(ref _specimenHandoverStatusText, value); }
    public string DicomFilterPatientId { get => _dicomFilterPatientId; set => SetField(ref _dicomFilterPatientId, value); }
    public string DicomStatusText { get => _dicomStatusText; set => SetField(ref _dicomStatusText, value); }

    public ICommand LoadSpecimenHandoversCommand => _loadSpecimenHandoversCommand ??= new AsyncRelayCommand(LoadSpecimenHandoversAsync);
    public ICommand DrawSpecimenCommand => _drawSpecimenCommand ??= new AsyncRelayCommand(DrawSpecimenAsync);
    public ICommand DispatchSpecimenCommand => _dispatchSpecimenCommand ??= new AsyncRelayCommand(DispatchSpecimenAsync);
    public ICommand ReceiveSpecimenCommand => _receiveSpecimenCommand ??= new AsyncRelayCommand(ReceiveSpecimenAsync);
    public ICommand RejectSpecimenCommand => _rejectSpecimenCommand ??= new AsyncRelayCommand(RejectSpecimenAsync);
    public ICommand LoadDicomStudiesCommand => _loadDicomStudiesCommand ??= new AsyncRelayCommand(LoadDicomStudiesAsync);

    public async Task LoadSpecimenHandoversAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<SpecimenHandoverItemVm>>>("/api/v4/medtech/specimens", CourierJsonOpts);
            SpecimenHandovers.Clear();
            if (res?.Code is 200 or 0 && res.Data != null)
            {
                foreach (var item in res.Data)
                {
                    SpecimenHandovers.Add(item);
                }
            }
            SelectedSpecimenHandover ??= SpecimenHandovers.FirstOrDefault();
            SpecimenHandoverStatusText = $"已同步标本物流台账 {SpecimenHandovers.Count} 条";
        }
        catch (Exception ex)
        {
            SpecimenHandoverStatusText = $"加载标本物流台账异常: {ex.Message}";
        }
    }

    public async Task DrawSpecimenAsync()
    {
        string reqId = string.IsNullOrWhiteSpace(SpecimenDrawRequestId)
            ? (SelectedOrderItem?.RequestNo ?? SelectedOrderItem?.RequestId ?? $"MTC-LAB-{DateTime.Now:HHmmss}")
            : SpecimenDrawRequestId.Trim();
        string patId = string.IsNullOrWhiteSpace(SpecimenDrawPatientId) ? "PAT85061801" : SpecimenDrawPatientId.Trim();
        string barcode = string.IsNullOrWhiteSpace(SpecimenDrawBarcode) ? $"BAR-{DateTime.Now:yyyyMMddHHmmss}" : SpecimenDrawBarcode.Trim();

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("/api/v4/medtech/specimens/draw", new
            {
                RequestId = reqId,
                PatientId = patId,
                SampleBarcode = barcode,
                WardId = SessionContext.Current.ActiveDeptId,
                Notes = "采血台打包待外勤运送"
            });
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<SpecimenHandoverItemVm>>(CourierJsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                SpecimenHandoverStatusText = env?.Message ?? "登记采样标本失败";
                _shell?.ShowToast(SpecimenHandoverStatusText, "Warning");
                return;
            }
            SpecimenHandoverStatusText = env.Message ?? $"标本 {barcode} 已采样";
            _shell?.ShowToast(SpecimenHandoverStatusText, "Success");
            SpecimenDrawBarcode = $"BAR-{DateTime.Now:HHmmss}";
            await LoadSpecimenHandoversAsync();
        }
        catch (Exception ex)
        {
            SpecimenHandoverStatusText = $"登记采样异常: {ex.Message}";
        }
    }

    public async Task DispatchSpecimenAsync()
    {
        var item = SelectedSpecimenHandover;
        if (item == null || string.IsNullOrWhiteSpace(SpecimenCourierId))
        {
            SpecimenHandoverStatusText = "请先选定待转运标本并填写物流工号/姓名";
            _shell?.ShowToast(SpecimenHandoverStatusText, "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync($"/api/v4/medtech/specimens/{Uri.EscapeDataString(item.Id)}/dispatch", new
            {
                CourierId = SpecimenCourierId.Trim(),
                Notes = "病区外勤冷链箱发出"
            });
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<SpecimenHandoverItemVm>>(CourierJsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                SpecimenHandoverStatusText = env?.Message ?? "标本发出转运失败";
                _shell?.ShowToast(SpecimenHandoverStatusText, "Warning");
                return;
            }
            SpecimenHandoverStatusText = env.Message ?? "标本已出发转运";
            _shell?.ShowToast(SpecimenHandoverStatusText, "Success");
            await LoadSpecimenHandoversAsync();
        }
        catch (Exception ex)
        {
            SpecimenHandoverStatusText = $"转运交接异常: {ex.Message}";
        }
    }

    public async Task ReceiveSpecimenAsync()
    {
        var item = SelectedSpecimenHandover;
        if (item == null)
        {
            SpecimenHandoverStatusText = "请先选定待检验核收标本";
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsync($"/api/v4/medtech/specimens/{Uri.EscapeDataString(item.Id)}/receive", null);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<SpecimenHandoverItemVm>>(CourierJsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                SpecimenHandoverStatusText = env?.Message ?? "检验科核收失败";
                _shell?.ShowToast(SpecimenHandoverStatusText, "Warning");
                return;
            }
            SpecimenHandoverStatusText = env.Message ?? "标本已核收";
            _shell?.ShowToast(SpecimenHandoverStatusText, "Success");
            await LoadSpecimenHandoversAsync();
        }
        catch (Exception ex)
        {
            SpecimenHandoverStatusText = $"核收异常: {ex.Message}";
        }
    }

    public async Task RejectSpecimenAsync()
    {
        var item = SelectedSpecimenHandover;
        if (item == null || string.IsNullOrWhiteSpace(SpecimenRejectReason))
        {
            SpecimenHandoverStatusText = "拒收退回不合格标本时必须填写原因（如溶血/凝血/量少）";
            _shell?.ShowToast(SpecimenHandoverStatusText, "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync($"/api/v4/medtech/specimens/{Uri.EscapeDataString(item.Id)}/reject", new
            {
                Reason = SpecimenRejectReason.Trim()
            });
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<SpecimenHandoverItemVm>>(CourierJsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
            {
                SpecimenHandoverStatusText = env?.Message ?? "退回不合格标本失败";
                _shell?.ShowToast(SpecimenHandoverStatusText, "Warning");
                return;
            }
            SpecimenHandoverStatusText = env.Message ?? "标本已退回重采";
            _shell?.ShowToast(SpecimenHandoverStatusText, "Warning");
            await LoadSpecimenHandoversAsync();
        }
        catch (Exception ex)
        {
            SpecimenHandoverStatusText = $"拒收异常: {ex.Message}";
        }
    }

    public async Task LoadDicomStudiesAsync()
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(DicomFilterPatientId)
                ? "/api/v4/medtech/exam/studies"
                : $"/api/v4/medtech/exam/studies?patientId={Uri.EscapeDataString(DicomFilterPatientId.Trim())}";
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<DicomStudySummaryVm>>>(url, CourierJsonOpts);
            DicomStudies.Clear();
            if (res?.Code is 200 or 0 && res.Data != null)
            {
                foreach (var s in res.Data) DicomStudies.Add(s);
            }
            SelectedDicomStudy ??= DicomStudies.FirstOrDefault();
            DicomStatusText = DicomStudies.Count > 0
                ? $"已检索 PACS DICOM 检查研究 {DicomStudies.Count} 项"
                : "暂无匹配的 DICOM 影像研究记录（PACS 同步后自动归档 StudyUID）";
        }
        catch (Exception ex)
        {
            DicomStatusText = $"加载 DICOM 影像元信息异常: {ex.Message}";
        }
    }
}
