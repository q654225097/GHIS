using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Emr.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Modules.Emr.Services;

public interface IEmrPrefetchCache
{
    void SetCachedDocuments(string encounterOrVisitId, IReadOnlyList<EmrDocument> docs);
    IReadOnlyList<EmrDocument>? TryGetCachedDocuments(string encounterOrVisitId);
}

public class EmrPrefetchCache : IEmrPrefetchCache
{
    private readonly ConcurrentDictionary<string, (IReadOnlyList<EmrDocument> Docs, long CachedAtMs)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void SetCachedDocuments(string encounterOrVisitId, IReadOnlyList<EmrDocument> docs)
    {
        _cache[encounterOrVisitId] = (docs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public IReadOnlyList<EmrDocument>? TryGetCachedDocuments(string encounterOrVisitId)
    {
        if (_cache.TryGetValue(encounterOrVisitId, out var entry))
        {
            // Valid for 5 minutes
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - entry.CachedAtMs < 300_000)
            {
                return entry.Docs;
            }
        }
        return null;
    }
}

public class EmrPrefetchBackgroundService : BackgroundService
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IEmrPrefetchCache _cache;
    private readonly ILogger<EmrPrefetchBackgroundService> _logger;

    public EmrPrefetchBackgroundService(
        IDbConnectionFactory connFactory,
        IEmrPrefetchCache cache,
        ILogger<EmrPrefetchBackgroundService> logger)
    {
        _connFactory = connFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EMR Level 7 High-Speed Prefetch Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PrefetchActiveRecordsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EMR prefetch cycle encountered error, retrying in next cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PrefetchActiveRecordsAsync(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();

        // 1. Fetch active inpatient encounters
        var activeEncounters = await conn.QueryAsync<string>(
            "SELECT id FROM ipd_encounters WHERE status = 1 ORDER BY admit_time_ms DESC LIMIT 50");

        foreach (var encId in activeEncounters)
        {
            if (ct.IsCancellationRequested) break;
            var docs = (await conn.QueryAsync<EmrDocument>(
                "SELECT * FROM emr_documents WHERE visit_or_encounter_id = @Id ORDER BY clinical_time_ms DESC",
                new { Id = encId })).AsList();

            _cache.SetCachedDocuments(encId, docs);
        }

        // 2. Fetch active outpatient visits
        var activeVisits = await conn.QueryAsync<string>(
            "SELECT id FROM opd_visits WHERE status IN (1, 2) ORDER BY created_at_ms DESC LIMIT 50");

        foreach (var visitId in activeVisits)
        {
            if (ct.IsCancellationRequested) break;
            var docs = (await conn.QueryAsync<EmrDocument>(
                "SELECT * FROM emr_documents WHERE visit_or_encounter_id = @Id ORDER BY clinical_time_ms DESC",
                new { Id = visitId })).AsList();

            _cache.SetCachedDocuments(visitId, docs);
        }
    }
}
