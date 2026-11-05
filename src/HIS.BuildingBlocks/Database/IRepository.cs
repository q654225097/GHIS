using System.Data;
using System.Data.Common;

namespace HIS.BuildingBlocks.Database;

public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}

public interface IRepository<T> where T : BaseEntity
{
    string TableName { get; }
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<T>> QueryAsync(string sql, object? param = null, CancellationToken ct = default);
    Task<T?> QuerySingleOrDefaultAsync(string sql, object? param = null, CancellationToken ct = default);
    Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default);
    Task<int> InsertAsync(T entity, CancellationToken ct = default);
    Task<int> UpdateAsync(T entity, CancellationToken ct = default);
    Task<int> DeleteAsync(string id, CancellationToken ct = default);
}

