using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.BuildingBlocks.Database;

public enum HisDbProviderKind
{
    Sqlite = 0,
    Dameng = 1,
    Postgres = 2
}

/// <summary>
/// 生产级多数据库方言与连接工厂（支持 SQLite / 达梦 DM8 / PostgreSQL 无缝切换）
/// 通过环境变量 HIS_DB_PROVIDER (Sqlite | Dameng | Postgres) 与 HIS_DB_CONNECTION_STRING 控制
/// </summary>
public sealed class MultiDbConnectionFactory : IDbConnectionFactory
{
    private sealed record AmbientScope(DbConnection Connection, DbTransaction Transaction);

    private sealed class NonDisposingWrapper : DbConnection
    {
        private readonly DbConnection _inner;
        private readonly DbTransaction _tx;

        public NonDisposingWrapper(DbConnection inner, DbTransaction tx)
        {
            _inner = inner;
            _tx = tx;
        }

        [AllowNull]
        public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;
        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() { }
        public override void Open() { if (_inner.State != ConnectionState.Open) _inner.Open(); }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => _tx;
        protected override DbCommand CreateDbCommand()
        {
            var cmd = _inner.CreateCommand();
            cmd.Transaction = _tx;
            return cmd;
        }
        protected override void Dispose(bool disposing) { }
    }

    private static readonly AsyncLocal<AmbientScope?> CurrentScope = new();

    private readonly HisDbProviderKind _providerKind;
    private readonly string _connectionString;
    private readonly SqliteConnectionFactory? _sqliteDelegate;
    private readonly Func<string, DbConnection>? _externalCreator;

    public HisDbProviderKind ProviderKind => _providerKind;
    public string ConnectionString => _connectionString;
    public SqliteConnectionFactory? SqliteDelegate => _sqliteDelegate;
    public DbTransaction? CurrentTransaction => _sqliteDelegate != null ? _sqliteDelegate.CurrentTransaction : CurrentScope.Value?.Transaction;

    static MultiDbConnectionFactory()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public MultiDbConnectionFactory(HisDbProviderKind providerKind, string connectionString)
    {
        _providerKind = providerKind;
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=his_v4.db;Mode=ReadWriteCreate;Default Timeout=30"
            : connectionString.Trim();

        if (_providerKind == HisDbProviderKind.Sqlite)
        {
            _sqliteDelegate = new SqliteConnectionFactory(_connectionString);
        }
        else if (_providerKind == HisDbProviderKind.Dameng)
        {
            _externalCreator = ResolveDynamicConnectionCreator(
                providerName: "达梦 DM8 (DmProvider)",
                typeCandidates: new[] { "Dm.DmConnection, DmProvider", "Dm.DmConnection, DM.DmProvider" },
                nugetHint: "DM.DmProvider");
        }
        else if (_providerKind == HisDbProviderKind.Postgres)
        {
            _externalCreator = ResolveDynamicConnectionCreator(
                providerName: "PostgreSQL (Npgsql)",
                typeCandidates: new[] { "Npgsql.NpgsqlConnection, Npgsql" },
                nugetHint: "Npgsql");
        }
    }

    public static MultiDbConnectionFactory FromConfig(string? providerName, string? connectionString)
    {
        var kind = (providerName ?? "Sqlite").Trim().ToLowerInvariant() switch
        {
            "dameng" or "dm" or "dm8" => HisDbProviderKind.Dameng,
            "postgres" or "postgresql" or "pg" or "npgsql" => HisDbProviderKind.Postgres,
            _ => HisDbProviderKind.Sqlite
        };
        string cs = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=his_v4.db;Mode=ReadWriteCreate;Default Timeout=30"
            : connectionString.Trim();
        return new MultiDbConnectionFactory(kind, cs);
    }

    private static Func<string, DbConnection> ResolveDynamicConnectionCreator(
        string providerName,
        string[] typeCandidates,
        string nugetHint)
    {
        Type? connType = null;
        foreach (string candidate in typeCandidates)
        {
            connType = Type.GetType(candidate, throwOnError: false);
            if (connType != null) break;
        }
        if (connType == null)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                connType = asm.GetTypes().FirstOrDefault(t =>
                    typeof(DbConnection).IsAssignableFrom(t) &&
                    typeCandidates.Any(c => string.Equals(t.FullName, c.Split(',')[0].Trim(), StringComparison.OrdinalIgnoreCase)));
                if (connType != null) break;
            }
        }
        if (connType == null)
        {
            throw new InvalidOperationException(
                $"已配置数据库引擎 {providerName}，但当前运行目录未加载对应驱动程序集。请在 HIS.Server.Host 中引用 NuGet 包 `{nugetHint}` 后重新编译部署。");
        }

        return cs =>
        {
            var conn = (DbConnection)(Activator.CreateInstance(connType, cs)
                ?? throw new InvalidOperationException($"无法实例化数据库连接类型 {connType.FullName}"));
            return conn;
        };
    }

    public DbConnection CreateConnection()
    {
        if (_sqliteDelegate != null)
            return _sqliteDelegate.CreateConnection();

        AmbientScope? scope = CurrentScope.Value;
        if (scope != null)
            return new NonDisposingWrapper(scope.Connection, scope.Transaction);

        DbConnection conn = _externalCreator!(_connectionString);
        conn.Open();
        return conn;
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        if (_sqliteDelegate != null)
            return _sqliteDelegate.ExecuteInTransactionAsync(action, ct);

        return ExecuteInTransactionAsync(async token =>
        {
            await action(token);
            return true;
        }, ct);
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken ct = default)
    {
        if (_sqliteDelegate != null)
            return await _sqliteDelegate.ExecuteInTransactionAsync(action, ct);

        if (CurrentScope.Value != null)
            return await action(ct);

        await using DbConnection conn = _externalCreator!(_connectionString);
        await conn.OpenAsync(ct);
        await using DbTransaction tx = await conn.BeginTransactionAsync(ct);
        CurrentScope.Value = new AmbientScope(conn, tx);
        try
        {
            TResult result = await action(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            CurrentScope.Value = null;
        }
    }

    /// <summary>
    /// 跨库 DDL 方言适配器（将标准建表脚本平滑翻译至达梦 DM8 或 PostgreSQL）
    /// </summary>
    public static string TranslateDdlForDialect(string sql, HisDbProviderKind kind)
    {
        if (string.IsNullOrWhiteSpace(sql) || kind == HisDbProviderKind.Sqlite)
            return sql;

        string translated = sql;
        if (kind == HisDbProviderKind.Dameng)
        {
            translated = Regex.Replace(translated, @"\bTEXT\b", "CLOB", RegexOptions.IgnoreCase);
            translated = Regex.Replace(translated, @"\bINTEGER\s+PRIMARY\s+KEY\s+AUTOINCREMENT\b", "BIGINT IDENTITY(1,1) PRIMARY KEY", RegexOptions.IgnoreCase);
        }
        else if (kind == HisDbProviderKind.Postgres)
        {
            translated = Regex.Replace(translated, @"\bINTEGER\s+PRIMARY\s+KEY\s+AUTOINCREMENT\b", "BIGSERIAL PRIMARY KEY", RegexOptions.IgnoreCase);
        }
        return translated;
    }

    /// <summary>
    /// 跨库查询与 DML 方言实时翻译器（解决 SQLite 专属语法在达梦 DM8 / PostgreSQL 下的兼容性问题）：
    /// - INSERT OR IGNORE INTO  -> PostgreSQL: INSERT INTO ... ON CONFLICT DO NOTHING / Dameng: INSERT /*+ IGNORE_ROW_ON_DUPKEY_INDEX */ INTO
    /// - CAST(x AS TEXT)        -> Dameng: CAST(x AS VARCHAR(4000))
    /// - datetime('now')        -> Dameng: SYSDATE / PostgreSQL: NOW()
    /// - strftime('%Y-%m', ...) -> Dameng/PG: TO_CHAR(...)
    /// </summary>
    public static string TranslateSqlForDialect(string sql, HisDbProviderKind kind)
    {
        if (string.IsNullOrWhiteSpace(sql) || kind == HisDbProviderKind.Sqlite)
            return sql;

        string t = TranslateDdlForDialect(sql, kind);

        if (kind == HisDbProviderKind.Dameng)
        {
            t = Regex.Replace(t, @"INSERT\s+OR\s+IGNORE\s+INTO", "INSERT /*+ IGNORE_ROW_ON_DUPKEY_INDEX */ INTO", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"INSERT\s+OR\s+REPLACE\s+INTO", "INSERT /*+ IGNORE_ROW_ON_DUPKEY_INDEX */ INTO", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"CAST\s*\(([^)]+?)\s+AS\s+TEXT\s*\)", "CAST($1 AS VARCHAR(4000))", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"datetime\s*\(\s*'now'\s*\)", "SYSDATE", RegexOptions.IgnoreCase);
        }
        else if (kind == HisDbProviderKind.Postgres)
        {
            if (Regex.IsMatch(t, @"INSERT\s+OR\s+IGNORE\s+INTO", RegexOptions.IgnoreCase))
            {
                t = Regex.Replace(t, @"INSERT\s+OR\s+IGNORE\s+INTO", "INSERT INTO", RegexOptions.IgnoreCase);
                t = Regex.Replace(t, @";\s*$", " ON CONFLICT DO NOTHING;", RegexOptions.Multiline);
                if (!t.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase))
                    t += " ON CONFLICT DO NOTHING";
            }
            t = Regex.Replace(t, @"datetime\s*\(\s*'now'\s*\)", "NOW()", RegexOptions.IgnoreCase);
        }

        return t;
    }
}
