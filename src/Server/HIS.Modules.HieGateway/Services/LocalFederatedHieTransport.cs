using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;

namespace HIS.Modules.HieGateway.Services;

public class LocalFederatedHieTransport : IHieRemoteTransport
{
    public string TransportMode => "LocalSimulator";

    public Task<EmpiResolveResultDto?> QueryEmpiMappingAsync(string localPatientId, string rawIndex, CancellationToken ct = default)
    {
        string empiId = "EMPI-" + CryptoHelper.ComputeSm3(rawIndex)[..16].ToUpperInvariant();
        return Task.FromResult<EmpiResolveResultDto?>(new EmpiResolveResultDto(
            EmpiId: empiId,
            LocalPatientId: localPatientId,
            ExternalOrgCode: "ORG-PEKING-UNION",
            ExternalPatientId: "PUMCH-" + localPatientId[..Math.Min(8, localPatientId.Length)],
            MatchConfidence: 0.98m
        ));
    }

    public Task<IReadOnlyList<ExternalEhrRecordDto>> FetchRemoteEhrRecordsAsync(string patientId, CancellationToken ct = default)
    {
        long now = SystemTime.UtcNowMs;
        IReadOnlyList<ExternalEhrRecordDto> simRecords = new List<ExternalEhrRecordDto>
        {
            new(
                SourceOrgCode: "ORG-PEKING-UNION",
                SourceOrgName: "北京协和医院",
                RecordCategory: "EMR",
                ExternalRecordId: "PUMCH-EMR-20260812-001",
                ClinicalTimeMs: now - 90L * 24 * 3600 * 1000,
                SummaryText: "【协和门诊病历】主诉发作性胸闷胸痛 2 月。诊断：冠状动脉粥样硬化性心脏病、不稳定性心绞痛。给予阿司匹林肠溶片 100mg qd，阿托伐他汀钙片 20mg qn。"
            ),
            new(
                SourceOrgCode: "ORG-RUJIN-SHANGHAI",
                SourceOrgName: "上海交通大学医学院附属瑞金医院",
                RecordCategory: "LAB",
                ExternalRecordId: "RUJIN-LAB-20261005-092",
                ClinicalTimeMs: now - 45L * 24 * 3600 * 1000,
                SummaryText: "【瑞金生化检验】血肌酐 Cr: 88.5 umol/L, eGFR: 82.1 ml/min, 血钾 K+: 4.35 mmol/L, 空腹血糖 GLU: 6.2 mmol/L, 糖化血红蛋白 HbA1c: 6.4%。"
            ),
            new(
                SourceOrgCode: "ORG-PEKING-UNION",
                SourceOrgName: "北京协和医院",
                RecordCategory: "EXAM",
                ExternalRecordId: "PUMCH-EXAM-20260901-314",
                ClinicalTimeMs: now - 60L * 24 * 3600 * 1000,
                SummaryText: "【协和心脏彩超】左心房轻度扩大 (LAD 39mm)，左室壁厚度正常，EF 62%。二尖瓣轻度反流，舒张功能减退 (E/A < 1)。"
            ),
            new(
                SourceOrgCode: "ORG-REGIONAL-ARCHIVE",
                SourceOrgName: "国家区域卫生信息平台健康档案",
                RecordCategory: "RX",
                ExternalRecordId: "HIE-RX-20261020-008",
                ClinicalTimeMs: now - 30L * 24 * 3600 * 1000,
                SummaryText: "【外院流转处方】琥珀酸美托洛尔缓释片 47.5mg qd × 30天；缬沙坦胶囊 80mg qd × 30天。已在院外合作药房调剂配发。"
            )
        };
        return Task.FromResult(simRecords);
    }

    public Task<bool> SendOutboundResourceAsync(string resourceType, string resourceId, string targetOrgCode, string payloadJson, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}
