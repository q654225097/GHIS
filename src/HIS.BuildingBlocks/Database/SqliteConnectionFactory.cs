using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace HIS.BuildingBlocks.Database;

public class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _sharedKeepAliveConnection;

    public SqliteConnectionFactory(string connectionString = "Data Source=his_v4.db;Mode=ReadWriteCreate")
    {
        if (connectionString.Contains("Memory", StringComparison.OrdinalIgnoreCase) &&
            connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            // Convert anonymous :memory: to named in-memory DB so Cache=Shared works across connections
            var dbName = "Mem_" + Guid.NewGuid().ToString("N");
            _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        }
        else
        {
            _connectionString = connectionString;
        }

        if (_connectionString.Contains("Memory", StringComparison.OrdinalIgnoreCase))
        {
            _sharedKeepAliveConnection = new SqliteConnection(_connectionString);
            _sharedKeepAliveConnection.Open();
        }
    }

    public DbConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
