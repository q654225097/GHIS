using System;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Hr.Controllers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Modules.Hr.Services;

/// <summary>
/// 职工全生命周期后台自动巡检熔断服务：
/// 定时扫描外院多点执业限时账号到期熔断、医务授权超期自动停权、以及住培/进修轮转到期自动切换科室上下文。
/// </summary>
public sealed class StaffLifecycleGovernanceWorker : BackgroundService
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly ILogger<StaffLifecycleGovernanceWorker> _logger;

    public StaffLifecycleGovernanceWorker(
        IDbConnectionFactory connFactory,
        ILogger<StaffLifecycleGovernanceWorker> logger)
    {
        _connFactory = connFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StaffLifecycleController.ExecuteGovernanceSweepCoreAsync(_connFactory, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "职工全生命周期到期巡检执行异常");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
