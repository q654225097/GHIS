using System.Data;
using System.Reflection;
using System.Text;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public class DapperRepository<T> : IRepository<T> where T : BaseEntity
{
    static DapperRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    protected readonly IDbConnectionFactory ConnectionFactory;
    public string TableName { get; }

    private static readonly PropertyInfo[] Properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite).ToArray();

    public DapperRepository(IDbConnectionFactory connectionFactory, string tableName)
    {
        ConnectionFactory = connectionFactory;
        TableName = tableName;
    }

    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        var sql = $"SELECT * FROM {TableName} WHERE id = @Id";
        return await conn.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        var sql = $"SELECT * FROM {TableName}";
        var result = await conn.QueryAsync<T>(new CommandDefinition(sql, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<IReadOnlyList<T>> QueryAsync(string sql, object? param = null, CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        var result = await conn.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<T?> QuerySingleOrDefaultAsync(string sql, object? param = null, CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        return await conn.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    public async Task<int> InsertAsync(T entity, CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        var cols = new List<string>();
        var paramNames = new List<string>();
        var dynamicParams = new DynamicParameters();

        foreach (var prop in Properties)
        {
            var col = ToSnakeCase(prop.Name);
            cols.Add(col);
            paramNames.Add("@" + prop.Name);
            dynamicParams.Add(prop.Name, prop.GetValue(entity));
        }

        var sql = $"INSERT INTO {TableName} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", paramNames)})";
        return await conn.ExecuteAsync(new CommandDefinition(sql, dynamicParams, cancellationToken: ct));
    }

    public async Task<int> UpdateAsync(T entity, CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        var sets = new List<string>();
        var dynamicParams = new DynamicParameters();

        entity.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var currentVersion = entity.VersionNo;
        entity.VersionNo++;

        foreach (var prop in Properties)
        {
            if (prop.Name == nameof(BaseEntity.Id)) continue;
            var col = ToSnakeCase(prop.Name);
            sets.Add($"{col} = @{prop.Name}");
            dynamicParams.Add(prop.Name, prop.GetValue(entity));
        }
        dynamicParams.Add("Id", entity.Id);
        dynamicParams.Add("ExpectedVersion", currentVersion);

        var sql = $"UPDATE {TableName} SET {string.Join(", ", sets)} WHERE id = @Id AND version_no = @ExpectedVersion";
        return await conn.ExecuteAsync(new CommandDefinition(sql, dynamicParams, cancellationToken: ct));
    }

    public async Task<int> DeleteAsync(string id, CancellationToken ct = default)
    {
        using var conn = ConnectionFactory.CreateConnection();
        var sql = $"DELETE FROM {TableName} WHERE id = @Id";
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }
}
