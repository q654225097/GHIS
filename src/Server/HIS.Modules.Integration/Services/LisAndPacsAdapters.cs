using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HIS.Contracts;

namespace HIS.Modules.Integration.Services;

public class MockLisAdapter : ILisAdapter
{
    private static readonly ConcurrentDictionary<string, LisTestOrder> _orders = new();
    private static readonly ConcurrentDictionary<string, LisResult> _results = new();

    public Task<LisTestOrder> SendTestOrderAsync(LisOrderRequest req, CancellationToken ct = default)
    {
        string lisOrderId = $"LIS-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(100, 999)}";
        string barcode = $"BC{DateTimeOffset.UtcNow:yyMMdd}{Random.Shared.Next(10000, 99999)}";

        var order = new LisTestOrder(
            LisOrderId: lisOrderId,
            HisOrderId: req.OrderId,
            Barcode: barcode,
            Status: "RECEIVED",
            CreatedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        _orders[lisOrderId] = order;

        // Auto-generate a result ready for query or sync
        var items = new List<LisResultItem>();
        bool isCritical = false;

        if (req.TestItemName.Contains("血常规") || req.TestItemCode.Contains("CBC"))
        {
            items.Add(new LisResultItem("WBC", "白细胞计数", "7.15", "10^9/L", "3.50-9.50", "NORMAL"));
            items.Add(new LisResultItem("RBC", "红细胞计数", "4.62", "10^12/L", "4.30-5.80", "NORMAL"));
            items.Add(new LisResultItem("HGB", "血红蛋白", "142", "g/L", "130-175", "NORMAL"));
            items.Add(new LisResultItem("PLT", "血小板计数", "225", "10^9/L", "125-350", "NORMAL"));
        }
        else if (req.TestItemName.Contains("生化") || req.TestItemCode.Contains("BIO"))
        {
            items.Add(new LisResultItem("ALT", "丙氨酸氨基转移酶", "28", "U/L", "9-50", "NORMAL"));
            items.Add(new LisResultItem("AST", "天门冬氨酸氨基转移酶", "24", "U/L", "15-40", "NORMAL"));
            items.Add(new LisResultItem("CREA", "肌酐", "78", "umol/L", "57-111", "NORMAL"));
            items.Add(new LisResultItem("GLU", "空腹血糖", "5.4", "mmol/L", "3.9-6.1", "NORMAL"));
        }
        else if (req.TestItemName.Contains("危急") || req.TestItemCode.Contains("CRIT"))
        {
            items.Add(new LisResultItem("K", "血清钾", "2.1", "mmol/L", "3.5-5.3", "CRITICAL"));
            items.Add(new LisResultItem("GLU", "血糖", "28.5", "mmol/L", "3.9-6.1", "CRITICAL"));
            isCritical = true;
        }
        else
        {
            items.Add(new LisResultItem("GEN-01", req.TestItemName, "阴性 (-)", "", "阴性", "NORMAL"));
        }

        var res = new LisResult(
            LisOrderId: lisOrderId,
            HisOrderId: req.OrderId,
            PatientId: req.PatientId,
            SampleType: req.SampleType,
            Items: items,
            ReporterId: "LIS-DOC-01",
            ReporterName: "检验技师(Mock)",
            VerifierId: "LIS-DOC-02",
            VerifierName: "主检技师(Mock)",
            ReportTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IsCritical: isCritical
        );

        _results[lisOrderId] = res;

        return Task.FromResult(order);
    }

    public Task<LisResult?> QueryResultAsync(string lisOrderId, CancellationToken ct = default)
    {
        if (_results.TryGetValue(lisOrderId, out var result))
        {
            return Task.FromResult<LisResult?>(result);
        }
        return Task.FromResult<LisResult?>(null);
    }

    public Task<List<LisResult>> BatchSyncResultsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_results.Values.ToList());
    }

    public Task<bool> CancelOrderAsync(string lisOrderId, CancellationToken ct = default)
    {
        if (_orders.TryGetValue(lisOrderId, out var order))
        {
            _orders[lisOrderId] = order with { Status = "CANCELLED" };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}

public class MockPacsAdapter : IPacsAdapter
{
    private static readonly ConcurrentDictionary<string, PacsStudyOrder> _orders = new();
    private static readonly ConcurrentDictionary<string, PacsReport> _reports = new();

    public Task<PacsStudyOrder> SendStudyOrderAsync(PacsOrderRequest req, CancellationToken ct = default)
    {
        var order = new PacsStudyOrder(
            AccessionNo: req.AccessionNo,
            Modality: req.Modality,
            Status: "SCHEDULED",
            ScheduledTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        _orders[req.AccessionNo] = order;

        // Auto-generate report ready for retrieval
        string findings = req.Modality switch
        {
            "CT" => "胸部CT平扫：双侧胸廓对称，纵隔居中。气管及主要支气管通畅。双肺野透亮度正常，未见明显实质性浸润、肿块或空洞。肺门影不大。心脏大小形态正常，大血管未见异常。双侧胸膜无增厚，胸腔内无明显积液征象。扫及纵隔未见明显肿大淋巴结。",
            "DR" => "胸部正位DR：双肺纹理走行规整，未见明确斑片状高密度浸润影。心影大小形态在正常生理范围。主动脉结未见钙化。双侧肋膈角锐利。胸廓骨骼未见异常。",
            "MR" => "头颅平扫MRI：双侧大脑半球对称，脑实质内未见异常信号区。各脑室、脑池系统大小形态如常，中线结构居中。未见水肿或占位效应。",
            _ => "超声检查：脏器各切面形态大小正常，实质回声均匀，未见占位性病变，血流信号丰富且规律。"
        };

        string impression = req.Modality switch
        {
            "CT" => "胸部CT平扫未见明显活动性病变；纵隔及肺门未见明显肿大淋巴结。",
            "DR" => "心肺膈X线胸片未见确切活动性病变。",
            "MR" => "头颅平扫未见明显新发脑缺血或出血性病灶。",
            _ => "检查脏器未见明显形态学异常。"
        };

        var report = new PacsReport(
            AccessionNo: req.AccessionNo,
            PatientId: req.PatientId,
            Modality: req.Modality,
            StudyBodyPart: req.StudyBodyPart,
            Findings: findings,
            Impression: impression,
            DicomViewerUrl: $"http://localhost:5200/api/v4/medtech/pacs/viewer/{req.AccessionNo}",
            RadiologistId: "PACS-DOC-01",
            RadiologistName: "影像科医师(Mock)",
            VerifierId: "PACS-DOC-02",
            VerifierName: "主任医师(Mock)",
            ReportTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        _reports[req.AccessionNo] = report;

        return Task.FromResult(order);
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
        // 1x1 transparent PNG or placeholder bytes
        byte[] placeholder = Encoding.UTF8.GetBytes($"DICOM_STUDY_PLACEHOLDER_{imageId}");
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
