using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.Stations.MedTechReport;

public sealed class ReportDicomStudyVm
{
    public string Id { get; set; } = string.Empty;
    public string StudyInstanceUid { get; set; } = string.Empty;
    public string AccessionNo { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public int SeriesCount { get; set; }
    public int InstanceCount { get; set; }
    public long StudyTimeMs { get; set; }

    public string TimeText => StudyTimeMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(StudyTimeMs).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss")
        : "-";

    public string DisplayLine => $"DICOM [{Modality}] 检查号:{AccessionNo} · {SeriesCount} 序列 / {InstanceCount} 图像帧 · {TimeText} · UID {StudyInstanceUid}";
}

public sealed partial class MedTechReportViewModel
{
    private string _dicomSummaryText = "选择影像或检查报告后自动联动查询 PACS DICOM 影像序列信息";
    private ICommand? _refreshDicomStudiesCommand;

    public ObservableCollection<ReportDicomStudyVm> ReportDicomStudies { get; } = new();

    public string DicomSummaryText
    {
        get => _dicomSummaryText;
        private set => SetField(ref _dicomSummaryText, value);
    }

    public ICommand RefreshDicomStudiesCommand => _refreshDicomStudiesCommand ??= new AsyncRelayCommand(() => LoadDicomStudiesForReportAsync(_selectedReport));

    private async Task LoadDicomStudiesForReportAsync(MtcDiagnosticReportRow? rpt)
    {
        ReportDicomStudies.Clear();
        if (rpt == null)
        {
            DicomSummaryText = "选择报告后联动查看 PACS DICOM 研究信息";
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = !string.IsNullOrWhiteSpace(rpt.RequestNo)
                ? $"/api/v4/medtech/exam/studies?accessionNo={Uri.EscapeDataString(rpt.RequestNo)}"
                : "/api/v4/medtech/exam/studies";

            var env = await http.GetFromJsonAsync<ApiResponse<List<ReportDicomStudyVm>>>(url, JsonOpts);
            if ((env?.Data == null || env.Data.Count == 0) && !string.IsNullOrWhiteSpace(rpt.RequestNo))
            {
                env = await http.GetFromJsonAsync<ApiResponse<List<ReportDicomStudyVm>>>("/api/v4/medtech/exam/studies", JsonOpts);
            }

            if (env?.Code is 200 or 0 && env.Data != null)
            {
                foreach (var item in env.Data.Take(8))
                {
                    ReportDicomStudies.Add(item);
                }
            }

            DicomSummaryText = ReportDicomStudies.Count > 0
                ? ReportDicomStudies[0].DisplayLine
                : $"申请单 {rpt.RequestNo ?? rpt.ReportNo} 暂无归档 DICOM Study（PACS 回传后自动挂载）";
        }
        catch (Exception ex)
        {
            DicomSummaryText = $"查询 PACS DICOM 研究失败：{ex.Message}";
        }
    }
}
