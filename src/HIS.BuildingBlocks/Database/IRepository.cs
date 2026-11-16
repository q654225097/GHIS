using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.BuildingBlocks.Database;

public interface IRepository<T> where T : BaseEntity
{
	string TableName { get; }

	Task<T?> GetByIdAsync(string id, CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default(CancellationToken));

	Task<IReadOnlyList<T>> QueryAsync(string sql, object? param = null, CancellationToken ct = default(CancellationToken));

	Task<T?> QuerySingleOrDefaultAsync(string sql, object? param = null, CancellationToken ct = default(CancellationToken));

	Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default(CancellationToken));

	Task<int> InsertAsync(T entity, CancellationToken ct = default(CancellationToken));

	Task<int> UpdateAsync(T entity, CancellationToken ct = default(CancellationToken));

	Task<int> DeleteAsync(string id, CancellationToken ct = default(CancellationToken));
}
