using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Modules.Inpatient.Services;

public class NursingCarePlanReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NursingCarePlanReminderWorker> _logger;

    public NursingCarePlanReminderWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<NursingCarePlanReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Nursing Care Plan Periodic Reminder Worker started (Interval: 10 minutes).");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanCarePlanRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during Nursing Care Plan reminder scan cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task ScanCarePlanRemindersAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var alertPublisher = scope.ServiceProvider.GetService<IClinicalAlertPublisher>();

        if (alertPublisher == null) return;

        using var conn = connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;

        var plans = (await conn.QueryAsync<(string PlanId, string EncounterId, string PlanTitle, long PlanCreatedAtMs, long? TargetDateMs, string WardId, int? RemindIntervalMinutes, long LastEvalAtMs)>(
            """
            SELECT 
                p.id AS PlanId,
                p.encounter_id AS EncounterId,
                p.diagnosis_or_problem AS PlanTitle,
                p.created_at_ms AS PlanCreatedAtMs,
                p.target_date_ms AS TargetDateMs,
                e.admit_ward_id AS WardId,
                COALESCE(t.remind_interval_minutes, 120) AS RemindIntervalMinutes,
                COALESCE((SELECT MAX(evaluated_at_ms) FROM nur_care_plan_evaluations WHERE plan_id = p.id), p.created_at_ms) AS LastEvalAtMs
            FROM nur_care_plans p
            JOIN ipd_encounters e ON p.encounter_id = e.id
            LEFT JOIN nur_care_plan_templates t ON p.ext_data_json LIKE '%' || t.template_code || '%'
            WHERE p.status = 0 AND e.status = 1
            LIMIT 200
            """)).AsList();

        foreach (var plan in plans)
        {
            if (ct.IsCancellationRequested) break;

            int intervalMins = plan.RemindIntervalMinutes.GetValueOrDefault(120);
            if (intervalMins <= 0) intervalMins = 120;

            long intervalMs = intervalMins * 60L * 1000L;
            long elapsedSinceLastEval = now - plan.LastEvalAtMs;

            if (elapsedSinceLastEval >= intervalMs)
            {
                string reminderMsg = $"【到期护理提醒】护理计划「{plan.PlanTitle}」已到达执行与阶段评估时限，请责任护士及时巡视与记录！";
                await alertPublisher.PublishCarePlanReminderAsync(plan.EncounterId, plan.WardId ?? string.Empty, reminderMsg);
                _logger.LogInformation("Pushed Care Plan Reminder: PlanId={PlanId}, WardId={WardId}", plan.PlanId, plan.WardId);
            }
        }
    }
}
