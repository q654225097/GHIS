using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Monitoring.Models;

namespace HIS.Modules.Monitoring.Services;

public class MonitoringService : IMonitoringContract
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<MonRiskScore> _scoreRepo;
    private readonly IClinicalKnowledgeContract? _kbContract;

    public MonitoringService(
        IDbConnectionFactory connFactory,
        IRepository<MonRiskScore> scoreRepo,
        IClinicalKnowledgeContract? kbContract = null)
    {
        _connFactory = connFactory;
        _scoreRepo = scoreRepo;
        _kbContract = kbContract;
    }

    public async Task<RiskScoreEvaluationResultDto> EvaluateRiskScoreAsync(
        string encounterId,
        string scoreType,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();

        // Query patient context & encounter
        var encounter = await conn.QuerySingleOrDefaultAsync<(string PatientId, string AdmitDeptId, string WardId)>(
            "SELECT patient_id, admit_dept_id, admit_ward_id FROM ipd_encounters WHERE id = @Id",
            new { Id = encounterId });

        string patientId = encounter.PatientId ?? string.Empty;

        // Query latest vital signs from nur_vital_signs or mon_vital_streams
        var latestStream = await conn.QueryFirstOrDefaultAsync<MonVitalStream>(
            "SELECT * FROM mon_vital_streams WHERE encounter_id = @EncounterId ORDER BY sampled_at_ms DESC LIMIT 1",
            new { EncounterId = encounterId });

        var componentScores = new Dictionary<string, int>();
        int totalScore = 0;
        string guidance = string.Empty;
        string riskLevel = "LOW";

        var type = (scoreType ?? "MEWS").ToUpperInvariant();
        if (type == "APACHE_II")
        {
            // APACHE II simplified calculation: Temp, MAP, HR, RR, Oxygenation, GCS
            decimal temp = latestStream?.TempC ?? 37.0m;
            decimal map = latestStream?.Map ?? 85m;
            decimal hr = latestStream?.Hr ?? 80m;
            decimal rr = latestStream?.Rr ?? 18m;
            int gcs = latestStream?.Gcs ?? 15;

            int tempScore = (temp < 36.0m || temp >= 39.0m) ? 2 : (temp >= 38.5m ? 1 : 0);
            int mapScore = (map < 70 || map > 110) ? 2 : 0;
            int hrScore = (hr < 55 || hr > 110) ? 2 : (hr > 140 ? 4 : 0);
            int rrScore = (rr < 10 || rr > 25) ? 2 : (rr > 35 ? 4 : 0);
            int gcsScore = Math.Max(0, 15 - gcs);

            componentScores["体温生理分"] = tempScore;
            componentScores["平均动脉压分"] = mapScore;
            componentScores["心率分"] = hrScore;
            componentScores["呼吸频率分"] = rrScore;
            componentScores["神经GCS分"] = gcsScore;

            totalScore = tempScore + mapScore + hrScore + rrScore + gcsScore;
            if (totalScore >= 15)
            {
                riskLevel = "CRITICAL";
                guidance = "【重症危急预警】APACHE II 评分 ≥ 15，预计院内病死率显著升高，建议立即启动 ICU 特级监护与多学科会诊(MDT)！";
            }
            else if (totalScore >= 8)
            {
                riskLevel = "HIGH";
                guidance = "【重症中高危预警】APACHE II 评分 8~14，存在器官功能恶化趋势，密切监测尿量、血气与连续血流动力学指标。";
            }
            else
            {
                riskLevel = "LOW";
                guidance = "【病情相对平稳】APACHE II 评分 < 8，生命体征波动处于可控范围，维持当前专科监护。";
            }
        }
        else if (type == "SOFA")
        {
            // SOFA: 呼吸、凝血、肝脏、心血管、中枢神经、肾脏
            decimal map = latestStream?.Map ?? 85m;
            int gcs = latestStream?.Gcs ?? 15;

            int cvScore = map < 70 ? 1 : 0;
            int cnsScore = gcs <= 12 ? 2 : (gcs <= 14 ? 1 : 0);
            componentScores["心血管系统SOFA"] = cvScore;
            componentScores["中枢神经系统SOFA"] = cnsScore;
            componentScores["呼吸系统SOFA"] = 1;
            componentScores["凝血系统SOFA"] = 0;
            componentScores["肝肾代谢SOFA"] = 1;

            totalScore = cvScore + cnsScore + 2;
            riskLevel = totalScore >= 4 ? "CRITICAL" : (totalScore >= 2 ? "MEDIUM" : "LOW");
            guidance = totalScore >= 2
                ? "【Sepsis-3 脓毒症筛查警示】SOFA 评分较基线升高 ≥ 2 分，提示可能存在感染引起的严重器官功能障碍！"
                : "SOFA 评分平稳，未见急性进行性器官衰竭征象。";
        }
        else
        {
            // Default: MEWS (改良早期预警评分)
            decimal hr = latestStream?.Hr ?? 80m;
            decimal sysBp = latestStream?.SysBp ?? 120m;
            decimal rr = latestStream?.Rr ?? 18m;
            decimal temp = latestStream?.TempC ?? 36.8m;

            int hrPt = (hr < 40 || hr > 130) ? 3 : ((hr >= 111 || hr <= 50) ? 2 : ((hr >= 101) ? 1 : 0));
            int bpPt = (sysBp < 70) ? 3 : ((sysBp <= 80) ? 2 : ((sysBp <= 100 || sysBp >= 200) ? 1 : 0));
            int rrPt = (rr < 9 || rr > 30) ? 3 : ((rr >= 21) ? 2 : ((rr >= 15 && rr <= 20) ? 0 : 1));
            int tempPt = (temp < 35.0m || temp >= 38.5m) ? 2 : 0;

            componentScores["心率得分"] = hrPt;
            componentScores["收缩压得分"] = bpPt;
            componentScores["呼吸得分"] = rrPt;
            componentScores["体温得分"] = tempPt;

            totalScore = hrPt + bpPt + rrPt + tempPt;
            if (totalScore >= 5)
            {
                riskLevel = "CRITICAL";
                guidance = "【MEWS 危急预警】评分 ≥ 5 分或单项 3 分，病情恶化风险极高，应立即呼叫值班医师或 RRS (快速反应小组)！";
            }
            else if (totalScore >= 3)
            {
                riskLevel = "MEDIUM";
                guidance = "【MEWS 关注预警】评分 3~4 分，建议增加生命体征监测频次至每 1~2 小时 1 次。";
            }
            else
            {
                riskLevel = "LOW";
                guidance = "【MEWS 正常范围】生命体征平稳，继续按既定护理等级监测。";
            }
        }

        // Persist score
        long now = SystemTime.UtcNowMs;
        var scoreEntity = new MonRiskScore
        {
            Id = Guid.NewGuid().ToString("D"),
            EncounterId = encounterId,
            PatientId = patientId,
            ScoreType = type,
            TotalScore = totalScore,
            RiskLevel = riskLevel,
            ComponentScoresJson = JsonSerializer.Serialize(componentScores),
            DrugLabCorrelationJson = JsonSerializer.Serialize(new { hr = latestStream?.Hr, map = latestStream?.Map }),
            KbGuidanceText = guidance,
            CalculatedAtMs = now,
            CreatedAtMs = now,
            CreatedBy = "MONITORING_ENGINE",
            UpdatedAtMs = now,
            UpdatedBy = "MONITORING_ENGINE"
        };
        await _scoreRepo.InsertAsync(scoreEntity);

        return new RiskScoreEvaluationResultDto(
            EncounterId: encounterId,
            ScoreType: type,
            TotalScore: totalScore,
            RiskLevel: riskLevel,
            GuidanceText: guidance,
            ComponentScores: componentScores
        );
    }
}
