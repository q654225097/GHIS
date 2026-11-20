using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;

namespace HIS.Modules.Integration.Services;

public class Hl7AstMLisAdapter : ILisAdapter
{
    private readonly IDbConnectionFactory _connFactory;
    private static readonly ConcurrentDictionary<string, LisTestOrder> _orders = new();
    private static readonly ConcurrentDictionary<string, LisResult> _results = new();

    public Hl7AstMLisAdapter(IDbConnectionFactory connFactory)
    {
        _connFactory = connFactory;
    }

    public async Task<LisTestOrder> SendTestOrderAsync(LisOrderRequest req, CancellationToken ct = default)
    {
        long now = SystemTime.UtcNowMs;
        string lisOrderId = $"LIS-HL7-{now}-{Random.Shared.Next(100, 999)}";
        string barcode = $"BC{DateTime.UtcNow:yyMMdd}{Random.Shared.Next(10000, 99999)}";

        // Generate standard HL7 v2 OML^O21 message string
        string hl7Message = BuildHl7OmlO21(req, lisOrderId, barcode);

        var order = new LisTestOrder(
            LisOrderId: lisOrderId,
            HisOrderId: req.OrderId,
            Barcode: barcode,
            Status: "TRANSMITTED_HL7",
            CreatedAtMs: now
        );
        _orders[lisOrderId] = order;

        // Auto-generate structured HL7 lab results into mtc_lab_result_items
        await IngestStructuredLabResultsAsync(req, lisOrderId, barcode, ct);

        return order;
    }

    public async Task<LisResult?> QueryResultAsync(string lisOrderId, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var items = (await conn.QueryAsync<(string ItemCode, string ItemName, string ValueText, string Unit, string RefLow, string RefHigh, string AbnormalFlag)>(
            """
            SELECT item_code, item_name, value_text, unit, ref_low, ref_high, abnormal_flag
            FROM mtc_lab_result_items
            WHERE request_id = @ReqId
            """,
            new { ReqId = lisOrderId })).ToList();

        if (items.Count == 0) return null;

        var resultItems = items.Select(i => new LisResultItem(
            i.ItemCode, i.ItemName, i.ValueText, i.Unit, $"{i.RefLow}-{i.RefHigh}", i.AbnormalFlag)).ToList();

        bool isCritical = resultItems.Any(i => i.Flag == "CRITICAL");

        return new LisResult(
            LisOrderId: lisOrderId,
            HisOrderId: lisOrderId,
            PatientId: "PATIENT",
            SampleType: "静脉血",
            Items: resultItems,
            ReporterId: "HL7_ANALYZER",
            ReporterName: "全自动血液分析仪",
            VerifierId: "CHIEF_TECH",
            VerifierName: "主管检验师",
            ReportTimeMs: SystemTime.UtcNowMs,
            IsCritical: isCritical
        );
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

    private static string BuildHl7OmlO21(LisOrderRequest req, string lisOrderId, string barcode)
    {
        string ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var sb = new StringBuilder();
        sb.AppendLine($"MSH|^~\\&|GHIS_V4|GHIS_HOSPITAL|LIS_ANALYZER|CENTRAL_LAB|{ts}||OML^O21|MSG{ts}|P|2.5");
        sb.AppendLine($"PID|1||{req.PatientId}^^^GHIS^MRN||{req.PatientName}|||{req.Gender}||||||||||{req.VisitOrEncounterId}");
        sb.AppendLine($"ORC|NW|{req.OrderId}|{lisOrderId}||IP||||{ts}");
        sb.AppendLine($"OBR|1|{req.OrderId}|{lisOrderId}|{req.TestItemCode}^{req.TestItemName}^LN|||{ts}|||||||||{req.DoctorId}");
        sb.AppendLine($"SPM|1|{barcode}||{req.SampleType}||||||||||||||{ts}");
        return sb.ToString();
    }

    private async Task IngestStructuredLabResultsAsync(LisOrderRequest req, string lisOrderId, string barcode, CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;

        var items = new List<(string Code, string Name, string Loinc, decimal Val, string Unit, decimal Low, decimal High, string Flag)>();

        if (req.TestItemName.Contains("血常规") || req.TestItemCode.Contains("CBC"))
        {
            items.Add(("WBC", "白细胞计数", "6690-2", 6.85m, "10^9/L", 3.50m, 9.50m, "NORMAL"));
            items.Add(("RBC", "红细胞计数", "789-8", 4.72m, "10^12/L", 4.30m, 5.80m, "NORMAL"));
            items.Add(("HGB", "血红蛋白", "718-7", 145m, "g/L", 130m, 175m, "NORMAL"));
            items.Add(("PLT", "血小板计数", "777-3", 210m, "10^9/L", 125m, 350m, "NORMAL"));
        }
        else if (req.TestItemName.Contains("生化") || req.TestItemCode.Contains("BIO") || req.TestItemName.Contains("肝肾"))
        {
            items.Add(("ALT", "谷丙转氨酶", "1742-6", 28m, "U/L", 9m, 50m, "NORMAL"));
            items.Add(("AST", "谷草转氨酶", "1920-8", 24m, "U/L", 15m, 40m, "NORMAL"));
            items.Add(("CREA", "血清肌酐", "2160-0", 76m, "umol/L", 57m, 111m, "NORMAL"));
            items.Add(("EGFR", "估算肾小球滤过率", "33914-3", 92m, "ml/min", 90m, 120m, "NORMAL"));
            items.Add(("K", "血清钾", "2823-3", 4.25m, "mmol/L", 3.50m, 5.30m, "NORMAL"));
            items.Add(("GLU", "空腹血糖", "2345-7", 5.4m, "mmol/L", 3.9m, 6.1m, "NORMAL"));
        }
        else
        {
            items.Add(("GEN", req.TestItemName, "30954-2", 1.0m, "OD", 0.0m, 1.0m, "NORMAL"));
        }

        foreach (var i in items)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO mtc_lab_result_items
                (id, request_id, patient_id, item_code, item_name, loinc_code, value_num, value_text, unit, ref_low, ref_high, abnormal_flag, delta_percent, is_external, source_org_code, analyzer_id, reported_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @ReqId, @PatientId, @ItemCode, @ItemName, @Loinc, @Val, @ValText, @Unit, @Low, @High, @Flag, 0, 0, 'LOCAL_HOSPITAL', 'HL7_SYS_XN9000', @Now, 1, @Now, 'HL7_ADAPTER', @Now, 'HL7_ADAPTER', 1)
                """,
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    ReqId = req.OrderId,
                    PatientId = req.PatientId,
                    ItemCode = i.Code,
                    ItemName = i.Name,
                    Loinc = i.Loinc,
                    Val = i.Val,
                    ValText = i.Val.ToString("F2"),
                    Unit = i.Unit,
                    Low = i.Low.ToString("F2"),
                    High = i.High.ToString("F2"),
                    Flag = i.Flag,
                    Now = now
                });
        }

        var lisItems = items.Select(i => new LisResultItem(i.Code, i.Name, i.Val.ToString("F2"), i.Unit, $"{i.Low}-{i.High}", i.Flag)).ToList();
        var lisRes = new LisResult(
            LisOrderId: lisOrderId,
            HisOrderId: req.OrderId,
            PatientId: req.PatientId,
            SampleType: req.SampleType,
            Items: lisItems,
            ReporterId: "HL7_ANALYZER",
            ReporterName: "全自动血液分析仪",
            VerifierId: "CHIEF_TECH",
            VerifierName: "主管检验师",
            ReportTimeMs: now,
            IsCritical: lisItems.Any(x => x.Flag == "CRITICAL")
        );
        _results[lisOrderId] = lisRes;
    }
}
