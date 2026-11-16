using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Operations.Models;

namespace HIS.Modules.Operations.Services;

public class OutboxService : IOutboxContract
{
	private readonly IRepository<OpsOutboxMessage> _outboxRepo;

	public OutboxService(IRepository<OpsOutboxMessage> outboxRepo)
	{
		_outboxRepo = outboxRepo;
	}

	public async Task EnqueueEventAsync(string eventType, object payload, string? operatorId = null, CancellationToken ct = default(CancellationToken))
	{
		OpsOutboxMessage msg = new OpsOutboxMessage
		{
			EventType = eventType,
			PayloadJson = ((payload is string s) ? s : JsonSerializer.Serialize(payload)),
			Status = 0,
			RetryCount = 0,
			CreatedBy = operatorId,
			UpdatedBy = operatorId
		};
		await _outboxRepo.InsertAsync(msg, ct);
	}

	public async Task<int> DispatchPendingMessagesAsync(int batchSize = 50, CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<OpsOutboxMessage> pending = await _outboxRepo.QueryAsync("SELECT * FROM ops_outbox_messages WHERE status = 0 ORDER BY created_at_ms ASC LIMIT @Limit", new
		{
			Limit = Math.Clamp(batchSize, 1, 200)
		}, ct);
		int count = 0;
		foreach (OpsOutboxMessage msg in pending)
		{
			msg.Status = 1;
			await _outboxRepo.UpdateAsync(msg, ct);
			count++;
		}
		return count;
	}
}
