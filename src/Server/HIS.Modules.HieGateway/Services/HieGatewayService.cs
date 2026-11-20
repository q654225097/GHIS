using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.HieGateway.Models;

namespace HIS.Modules.HieGateway.Services;

public class HieGatewayService : IHieGatewayContract
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<HieEmpiMapping> _empiRepo;
    private readonly IRepository<HieExchangeLog> _logRepo;
    private readonly IHieRemoteTransport _transport;

    public HieGatewayService(
        IDbConnectionFactory connFactory,
        IRepository<HieEmpiMapping> empiRepo,
        IRepository<HieExchangeLog> logRepo,
        IHieRemoteTransport? transport = null)
    {
        _connFactory = connFactory;
        _empiRepo = empiRepo;
        _logRepo = logRepo;
        _transport = transport ?? new LocalFederatedHieTransport();
    }

    public async Task<EmpiResolveResultDto?> ResolveEmpiAsync(
        string localPatientId,
        string fullName,
        string idCardHash,
        string genderCode,
        int birthDateInt,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();

        // 1. Look up existing mapping in local database
        var existing = await conn.QueryFirstOrDefaultAsync<HieEmpiMapping>(
            "SELECT * FROM hie_empi_mappings WHERE local_patient_id = @Id LIMIT 1",
            new { Id = localPatientId });

        if (existing != null)
        {
            return new EmpiResolveResultDto(
                EmpiId: existing.EmpiId,
                LocalPatientId: existing.LocalPatientId,
                ExternalOrgCode: existing.ExternalOrgCode,
                ExternalPatientId: existing.ExternalPatientId,
                MatchConfidence: existing.MatchConfidence
            );
        }

        // 2. Resolve via configured HIE transport
        string rawIndex = $"{fullName}|{genderCode}|{birthDateInt}|{idCardHash}";
        var resolved = await _transport.QueryEmpiMappingAsync(localPatientId, rawIndex, ct);

        if (resolved != null)
        {
            long now = SystemTime.UtcNowMs;
            var mapping = new HieEmpiMapping
            {
                Id = Guid.NewGuid().ToString("D"),
                LocalPatientId = localPatientId,
                EmpiId = resolved.EmpiId,
                IdCardBlindIndex = idCardHash,
                ExternalOrgCode = resolved.ExternalOrgCode,
                ExternalOrgName = resolved.ExternalOrgCode,
                ExternalPatientId = resolved.ExternalPatientId,
                MatchConfidence = resolved.MatchConfidence,
                VerifiedAtMs = now,
                CreatedAtMs = now,
                CreatedBy = "EMPI_RESOLVER",
                UpdatedAtMs = now,
                UpdatedBy = "EMPI_RESOLVER"
            };
            await _empiRepo.InsertAsync(mapping);
            return resolved;
        }

        return null;
    }

    public async Task<IReadOnlyList<ExternalEhrRecordDto>> FetchExternalEhrSummaryAsync(
        string patientId,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();

        // Check if cached external health records exist in emr_external_health_records
        var cached = (await conn.QueryAsync<(string SourceOrgCode, string SourceOrgName, string RecordCategory, string ExternalRecordId, long ClinicalTimeMs, string SummaryText)>(
            """
            SELECT source_org_code, source_org_name, record_category, external_record_id, clinical_time_ms, summary_text
            FROM emr_external_health_records
            WHERE patient_id = @PatientId
            ORDER BY clinical_time_ms DESC
            """,
            new { PatientId = patientId })).ToList();

        if (cached.Count > 0)
        {
            return cached.Select(c => new ExternalEhrRecordDto(
                c.SourceOrgCode, c.SourceOrgName, c.RecordCategory, c.ExternalRecordId, c.ClinicalTimeMs, c.SummaryText)).ToList();
        }

        // Fetch via remote transport and cache
        var remoteRecords = await _transport.FetchRemoteEhrRecordsAsync(patientId, ct);
        long now = SystemTime.UtcNowMs;

        foreach (var rec in remoteRecords)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO emr_external_health_records
                (id, patient_id, source_org_code, source_org_name, record_category, external_record_id, clinical_time_ms, cda_or_fhir_json, summary_text, synced_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @PatientId, @OrgCode, @OrgName, @Category, @ExtId, @TimeMs, @CdaJson, @Summary, @Now, 1, @Now, 'HIE_GATEWAY', @Now, 'HIE_GATEWAY', 1)
                """,
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    PatientId = patientId,
                    OrgCode = rec.SourceOrgCode,
                    OrgName = rec.SourceOrgName,
                    Category = rec.RecordCategory,
                    ExtId = rec.ExternalRecordId,
                    TimeMs = rec.ClinicalTimeMs,
                    CdaJson = "{}",
                    Summary = rec.SummaryText,
                    Now = now
                });
        }

        return remoteRecords;
    }

    public async Task<bool> PublishOutboundResourceAsync(
        string resourceType,
        string resourceId,
        string targetOrgCode,
        object payload,
        CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(payload);
        string hash = CryptoHelper.ComputeSm3(json);
        long now = SystemTime.UtcNowMs;

        bool sendSuccess = await _transport.SendOutboundResourceAsync(resourceType, resourceId, targetOrgCode, json, ct);

        var log = new HieExchangeLog
        {
            Id = Guid.NewGuid().ToString("D"),
            Direction = "OUTBOUND",
            Protocol = _transport.TransportMode == "FhirHttp" ? "FHIR_R4" : "LOCAL_FEDERATED",
            PeerOrgCode = targetOrgCode,
            ResourceType = resourceType,
            ResourceId = resourceId,
            PayloadSm3Hash = hash,
            Status = sendSuccess ? "SUCCESS" : "FAILED",
            CreatedAtMs = now,
            CreatedBy = "HIE_PUBLISHER",
            UpdatedAtMs = now,
            UpdatedBy = "HIE_PUBLISHER"
        };
        await _logRepo.InsertAsync(log);
        return sendSuccess;
    }
}
