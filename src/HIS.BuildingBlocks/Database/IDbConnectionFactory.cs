using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.BuildingBlocks.Database;

public interface IDbConnectionFactory
{
	DbTransaction? CurrentTransaction => null;

	DbConnection CreateConnection();

	Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default(CancellationToken))
	{
		return action(ct);
	}

	Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken ct = default(CancellationToken))
	{
		return action(ct);
	}
}
