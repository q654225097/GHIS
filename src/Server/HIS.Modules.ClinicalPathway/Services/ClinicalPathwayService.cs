using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.ClinicalPathway.Models;

namespace HIS.Modules.ClinicalPathway.Services;

public sealed class ClinicalPathwayService : IClinicalPathwayContract
{
    private readonly IRepository<CpwPathway> _pathwayRepo;
    private readonly IRepository<CpwStage> _stageRepo;
    private readonly IRepository<CpwStageItem> _itemRepo;
    private readonly IRepository<CpwPatientEnrollment> _enrollmentRepo;
    private readonly IRepository<CpwVariance> _varianceRepo;
    private readonly IDbConnectionFactory? _connFactory;

    public ClinicalPathwayService(
        IRepository<CpwPathway> pathwayRepo,
        IRepository<CpwStage> stageRepo,
        IRepository<CpwStageItem> itemRepo,
        IRepository<CpwPatientEnrollment> enrollmentRepo,
        IRepository<CpwVariance> varianceRepo,
        IDbConnectionFactory? connFactory = null)
    {
        _pathwayRepo = pathwayRepo;
        _stageRepo = stageRepo;
        _itemRepo = itemRepo;
        _enrollmentRepo = enrollmentRepo;
        _varianceRepo = varianceRepo;
        _connFactory = connFactory;
    }

    public async Task<IReadOnlyList<CpwMatchResultDto>> MatchPathwaysAsync(
        string icdCode,
        CancellationToken ct = default)
    {
        var pathways = await _pathwayRepo.QueryAsync("SELECT * FROM cpw_pathways WHERE status = 1", ct: ct);
        var matched = new List<CpwMatchResultDto>();

        foreach (var pw in pathways)
        {
            bool isMatch = false;
            try
            {
                var icdList = JsonSerializer.Deserialize<List<string>>(pw.ApplicableIcd10Json);
                if (icdList != null && icdList.Any(code => icdCode.StartsWith(code, StringComparison.OrdinalIgnoreCase) || code.StartsWith(icdCode, StringComparison.OrdinalIgnoreCase)))
                {
                    isMatch = true;
                }
            }
            catch
            {
                if (pw.ApplicableIcd10Json.Contains(icdCode, StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }
            }

            if (isMatch)
            {
                matched.Add(new CpwMatchResultDto(
                    PathwayId: pw.Id,
                    PathwayCode: pw.PathwayCode,
                    PathwayName: pw.PathwayName,
                    StandardDays: pw.StandardDays,
                    MinCost: pw.MinCost,
                    MaxCost: pw.MaxCost
                ));
            }
        }

        return matched;
    }

    public async Task<CpwEnrollmentDto> EnrollPatientAsync(
        string encounterId,
        string patientId,
        string pathwayId,
        string enrolledBy,
        CancellationToken ct = default)
    {
        var pathway = await _pathwayRepo.GetByIdAsync(pathwayId, ct);
        if (pathway == null) throw new InvalidOperationException("临床路径不存在");

        var stages = await _stageRepo.QueryAsync("SELECT * FROM cpw_stages WHERE pathway_id = @PwId ORDER BY stage_order ASC", new { PwId = pathwayId }, ct);
        var firstStage = stages.FirstOrDefault();

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new CpwPatientEnrollment
        {
            Id = Guid.NewGuid().ToString(),
            EncounterId = encounterId,
            PatientId = patientId,
            PathwayId = pathwayId,
            CurrentStageId = firstStage?.Id,
            EnrolledAtMs = now,
            EnrolledBy = enrolledBy,
            Status = "ACTIVE",
            CreatedAtMs = now,
            CreatedBy = enrolledBy,
            UpdatedAtMs = now,
            UpdatedBy = enrolledBy,
            VersionNo = 1
        };

        await _enrollmentRepo.InsertAsync(entity, ct);

        return new CpwEnrollmentDto(
            Id: entity.Id,
            EncounterId: entity.EncounterId,
            PatientId: entity.PatientId,
            PathwayId: pathway.Id,
            PathwayName: pathway.PathwayName,
            CurrentStageId: firstStage?.Id ?? string.Empty,
            CurrentStageName: firstStage?.StageName ?? "第一阶段",
            Status: entity.Status
        );
    }

    public async Task<CpwOrderComparisonResultDto> CompareOrdersAsync(
        string enrollmentId,
        CancellationToken ct = default)
    {
        var enrollment = await _enrollmentRepo.GetByIdAsync(enrollmentId, ct);
        if (enrollment == null)
        {
            return new CpwOrderComparisonResultDto(Array.Empty<string>(), Array.Empty<string>());
        }

        var stageItems = string.IsNullOrEmpty(enrollment.CurrentStageId)
            ? new List<CpwStageItem>()
            : (await _itemRepo.QueryAsync("SELECT * FROM cpw_stage_items WHERE stage_id = @StageId", new { StageId = enrollment.CurrentStageId }, ct)).ToList();

        // 真实关联住院医嘱表 ord_medical_orders 逐条核对路径必选执行项与路径外变异医嘱
        var actualOrders = new List<(string ItemCode, string ItemName, int Status)>();
        if (_connFactory != null && !string.IsNullOrWhiteSpace(enrollment.EncounterId))
        {
            using var conn = _connFactory.CreateConnection();
            var rows = await conn.QueryAsync<dynamic>(
                "SELECT item_code AS ItemCode, item_name AS ItemName, status AS Status FROM ord_medical_orders WHERE encounter_id = @EncId AND status >= 0",
                new { EncId = enrollment.EncounterId });
            foreach (var r in rows)
            {
                actualOrders.Add(((string?)r.ItemCode ?? string.Empty, (string?)r.ItemName ?? string.Empty, Convert.ToInt32(r.Status)));
            }
        }

        var missing = new List<string>();
        int matchedMandatory = 0;
        int totalMandatory = 0;
        foreach (var item in stageItems.Where(i => i.IsMandatory == 1))
        {
            totalMandatory++;
            bool executed = actualOrders.Any(o =>
                (!string.IsNullOrWhiteSpace(item.ItemCode) && string.Equals(o.ItemCode, item.ItemCode, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(item.ItemName) && o.ItemName.Contains(item.ItemName, StringComparison.OrdinalIgnoreCase)));
            if (executed)
                matchedMandatory++;
            else
                missing.Add($"【必选未开】{item.ItemName} ({item.ItemCategory})");
        }

        var variances = new List<string>();
        if (missing.Count > 0)
        {
            variances.Add($"当前阶段必选项完成 {matchedMandatory}/{totalMandatory}，尚缺 {missing.Count} 项未开立");
        }
        // 检测路径外新增变异医嘱
        var outOfPathway = actualOrders.Where(o => !stageItems.Any(i =>
            (!string.IsNullOrWhiteSpace(i.ItemCode) && string.Equals(i.ItemCode, o.ItemCode, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(i.ItemName) && o.ItemName.Contains(i.ItemName, StringComparison.OrdinalIgnoreCase)))).ToList();
        foreach (var extra in outOfPathway.Take(5))
        {
            variances.Add($"【路径外变异医嘱】{extra.ItemName} ({extra.ItemCode})");
        }

        return new CpwOrderComparisonResultDto(missing, variances);
    }

    public async Task<CpwEmrFusionSnippetDto> GetEmrFusionSnippetAsync(
        string enrollmentId,
        CancellationToken ct = default)
    {
        var enrollment = await _enrollmentRepo.GetByIdAsync(enrollmentId, ct);
        if (enrollment == null || string.IsNullOrEmpty(enrollment.CurrentStageId))
        {
            return new CpwEmrFusionSnippetDto("默认阶段", "无路径推荐医嘱", "常规护理", "病情平稳");
        }

        var stage = await _stageRepo.GetByIdAsync(enrollment.CurrentStageId, ct);
        var items = await _itemRepo.QueryAsync("SELECT * FROM cpw_stage_items WHERE stage_id = @StageId", new { StageId = enrollment.CurrentStageId }, ct);

        string orders = string.Join("; ", items.Select(i => $"{i.ItemName}({i.ItemCategory})"));
        string stageName = stage?.StageName ?? "路径执行阶段";

        return new CpwEmrFusionSnippetDto(
            StageName: stageName,
            RecommendedOrdersMarkdown: $"**【{stageName} 临床路径推荐项目】**：{orders}",
            NursingGuidelinesMarkdown: $"按 {stageName} 规范实施生命体征每4小时监测一次，完成跌倒/压疮评估。",
            TreatmentGoalsMarkdown: $"达到 {stageName} 设定的临床控制指标，准备过渡至下一诊疗阶段。"
        );
    }

    public async Task<string> RecordVarianceAsync(
        string enrollmentId,
        string stageId,
        string? stageItemId,
        string? relatedOrderId,
        string varianceType,
        string reasonCategory,
        string reasonDetail,
        string recordedBy,
        CancellationToken ct = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var variance = new CpwVariance
        {
            Id = Guid.NewGuid().ToString(),
            EnrollmentId = enrollmentId,
            StageId = stageId,
            StageItemId = stageItemId,
            RelatedOrderId = relatedOrderId,
            VarianceType = varianceType,
            ReasonCategory = reasonCategory,
            ReasonDetail = reasonDetail,
            RecordedBy = recordedBy,
            RecordedAtMs = now,
            CreatedAtMs = now,
            CreatedBy = recordedBy,
            UpdatedAtMs = now,
            UpdatedBy = recordedBy,
            VersionNo = 1
        };

        await _varianceRepo.InsertAsync(variance, ct);
        return variance.Id;
    }
}
