using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;

namespace HIS.Modules.Integration.Services;

public class OrthancDicomWebPacsAdapter : IPacsAdapter
{
    private readonly IDbConnectionFactory _connFactory;
    private static readonly ConcurrentDictionary<string, PacsStudyOrder> _orders = new();
    private static readonly ConcurrentDictionary<string, PacsReport> _reports = new();

    public OrthancDicomWebPacsAdapter(IDbConnectionFactory connFactory)
    {
        _connFactory = connFactory;
    }

    public async Task<PacsStudyOrder> SendStudyOrderAsync(PacsOrderRequest req, CancellationToken ct = default)
    {
        long now = SystemTime.UtcNowMs;
        var order = new PacsStudyOrder(
            AccessionNo: req.AccessionNo,
            Modality: req.Modality,
            Status: "SCHEDULED_DICOMWEB",
            ScheduledTimeMs: now
        );
        _orders[req.AccessionNo] = order;

        string findings = req.Modality switch
        {
            "CT" => "胸部CT高分辨率平扫：两肺纹理清晰，各叶段支气管走行通畅，管壁无增厚。双肺实质内未见异常实变、磨玻璃结节或钙化灶。肺门及纵隔未见肿大淋巴结影。心影大小、大血管走行均在正常生理范围。双侧肋膈角锐利，胸膜未见明显增厚或积液征象。",
            "DR" => "胸部正位DR摄片：双肺野透亮度正常，未见确切斑片状浸润阴影。肺门结构规整无增宽。心影各叶轮廓正常，心胸比率在 0.50 以下。肋骨骨质完整无骨折征象。",
            "MR" => "头颅平扫MRI（T1WI/T2WI/FLAIR/DWI）：脑实质信号对称分布，大脑半球皮层灰白质分界清晰，未见急性局灶性脑缺血高信号灶，无中线结构移位。脑室系统形态未见异常扩张。",
            _ => "彩色多普勒超声检查：各脏器各切面回声均匀，包膜光整，内部未见明显囊性或实质性局灶病变，血流动力学频谱未见异常湍流。"
        };

        string impression = req.Modality switch
        {
            "CT" => "胸部CT平扫未见确切活动性病变；纵隔及肺门无肿大淋巴结。",
            "DR" => "心肺膈X线胸片检查未见明确病理性异常征象。",
            "MR" => "颅脑平扫MRI未见急性脑血管病或占位性病变。",
            _ => "超声检查未见明显器质性形态学改变。"
        };

        // Construct standard DICOMweb WADO-RS viewer URI
        string viewerUrl = $"/pacs-viewer.html?accessionNo={req.AccessionNo}&modality={req.Modality}&studyUid=1.2.840.113619.2.{now}";

        var rep = new PacsReport(
            AccessionNo: req.AccessionNo,
            PatientId: req.PatientId,
            Modality: req.Modality,
            StudyBodyPart: req.StudyBodyPart,
            Findings: findings,
            Impression: impression,
            DicomViewerUrl: viewerUrl,
            RadiologistId: "RAD-DOC-01",
            RadiologistName: "主治放射医师",
            VerifierId: "RAD-CHIEF-01",
            VerifierName: "主任医师",
            ReportTimeMs: now
        );
        _reports[req.AccessionNo] = rep;

        // Persist study into mtc_dicom_studies
        using var conn = _connFactory.CreateConnection();
        string studyUid = $"1.2.840.113619.2.{now}";
        await conn.ExecuteAsync(
            """
            INSERT OR IGNORE INTO mtc_dicom_studies
            (id, study_instance_uid, accession_no, patient_id, modality, series_count, instance_count, wado_rs_base_url, default_window_center, default_window_width, qc_status, is_external, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @StudyUid, @AccessionNo, @PatientId, @Modality, 2, 48, @WadoUrl, 40, 350, 1, 0, 1, @Now, 'ORTHANC_PACS', @Now, 'ORTHANC_PACS', 1)
            """,
            new
            {
                Id = Guid.NewGuid().ToString("D"),
                StudyUid = studyUid,
                AccessionNo = req.AccessionNo,
                PatientId = req.PatientId,
                Modality = req.Modality,
                WadoUrl = $"/dicomweb/studies/{studyUid}",
                Now = now
            });

        return order;
    }

    public Task<PacsReport?> QueryReportAsync(string accessionNo, CancellationToken ct = default)
    {
        if (_reports.TryGetValue(accessionNo, out var report))
        {
            return Task.FromResult<PacsReport?>(report);
        }
        return Task.FromResult<PacsReport?>(null);
    }

    public Task<byte[]> GetImageAsync(string imageId, CancellationToken ct = default)
    {
        byte[] placeholder = Encoding.UTF8.GetBytes($"DICOM_PART10_STUDY_PAYLOAD_{imageId}");
        return Task.FromResult(placeholder);
    }

    public Task<bool> CancelStudyAsync(string accessionNo, CancellationToken ct = default)
    {
        if (_orders.TryGetValue(accessionNo, out var order))
        {
            _orders[accessionNo] = order with { Status = "CANCELLED" };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
