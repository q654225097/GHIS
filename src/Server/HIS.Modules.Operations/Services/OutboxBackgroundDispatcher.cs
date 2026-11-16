using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Modules.Operations.Services;

/// <summary>
/// 可靠事件 Outbox 自动后台轮询投递服务：
/// 周期性轮询 ops_outbox_messages 中未投递的消息并派发，确保分布式最终一致性
/// </summary>
public class OutboxBackgroundDispatcher : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<OutboxBackgroundDispatcher>? _logger;
	private readonly TimeSpan _pollingInterval;

	public OutboxBackgroundDispatcher(
		IServiceScopeFactory scopeFactory,
		ILogger<OutboxBackgroundDispatcher>? logger = null,
		TimeSpan? pollingInterval = null)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
		_pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(3);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger?.LogInformation("OutboxBackgroundDispatcher 自动投递后台服务已启动，轮询周期: {Interval}s", _pollingInterval.TotalSeconds);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				using (var scope = _scopeFactory.CreateScope())
				{
					var outboxService = scope.ServiceProvider.GetRequiredService<OutboxService>();
					int dispatched = await outboxService.DispatchPendingMessagesAsync(50, stoppingToken);
					if (dispatched > 0)
					{
						_logger?.LogInformation("Outbox 自动派发成功: {Count} 条消息已标记投递", dispatched);
					}
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Outbox 自动派发过程中发生异常");
			}

			try
			{
				await Task.Delay(_pollingInterval, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}

		_logger?.LogInformation("OutboxBackgroundDispatcher 自动投递后台服务已平稳停止");
	}
}

