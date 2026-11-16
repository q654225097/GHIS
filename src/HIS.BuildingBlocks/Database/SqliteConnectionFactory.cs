using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace HIS.BuildingBlocks.Database;

public class SqliteConnectionFactory : IDbConnectionFactory
{
	private sealed record AmbientTransactionScope(SqliteConnection Connection, SqliteTransaction Transaction);

	private sealed class NonDisposingDbConnection : DbConnection
	{
		private readonly DbConnection _inner;

		private readonly DbTransaction _transaction;

		public override string ConnectionString
		{
			get
			{
				return _inner.ConnectionString;
			}
			[param: AllowNull]
			set
			{
				_inner.ConnectionString = value;
			}
		}

		public override string Database => _inner.Database;

		public override string DataSource => _inner.DataSource;

		public override string ServerVersion => _inner.ServerVersion;

		public override ConnectionState State => _inner.State;

		public NonDisposingDbConnection(DbConnection inner, DbTransaction transaction)
		{
			_inner = inner;
			_transaction = transaction;
		}

		public override void ChangeDatabase(string databaseName)
		{
			_inner.ChangeDatabase(databaseName);
		}

		public override void Close()
		{
		}

		public override void Open()
		{
			if (_inner.State != ConnectionState.Open)
			{
				_inner.Open();
			}
		}

		protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
		{
			return _transaction;
		}

		protected override DbCommand CreateDbCommand()
		{
			DbCommand dbCommand = _inner.CreateCommand();
			dbCommand.Transaction = _transaction;
			return dbCommand;
		}

		protected override void Dispose(bool disposing)
		{
		}
	}

	private readonly string _connectionString;

	private readonly SqliteConnection? _sharedKeepAliveConnection;

	private static readonly AsyncLocal<AmbientTransactionScope?> CurrentScope;

	public DbTransaction? CurrentTransaction => CurrentScope.Value?.Transaction;
	public bool IsAmbientTransaction => CurrentScope.Value != null;
	public string ConnectionString => _connectionString;

	static SqliteConnectionFactory()
	{
		CurrentScope = new AsyncLocal<AmbientTransactionScope>();
		SqliteTypeHandlers.Initialize();
	}

	public SqliteConnectionFactory(string connectionString = "Data Source=his_v4.db;Mode=ReadWriteCreate")
	{
		if (connectionString.Contains("Memory", StringComparison.OrdinalIgnoreCase) && connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
		{
			string text = "Mem_" + Guid.NewGuid().ToString("N");
			_connectionString = "Data Source=" + text + ";Mode=Memory;Cache=Shared;Default Timeout=30";
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
		AmbientTransactionScope value = CurrentScope.Value;
		if (value != null)
		{
			return new NonDisposingDbConnection(value.Connection, value.Transaction);
		}
		SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		return sqliteConnection;
	}

	public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default(CancellationToken))
	{
		await ExecuteInTransactionAsync(async (CancellationToken token) =>
		{
			await action(token);
			return true;
		}, ct);
	}

	public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken ct = default(CancellationToken))
	{
		if (CurrentScope.Value != null)
		{
			return await action(ct);
		}
		TResult result2;
		await using (SqliteConnection conn = new SqliteConnection(_connectionString))
		{
			await conn.OpenAsync(ct);
			TResult val;
			await using (SqliteTransaction tx = (SqliteTransaction)(await conn.BeginTransactionAsync(ct)))
			{
				CurrentScope.Value = new AmbientTransactionScope(conn, tx);
				try
				{
					TResult result = await action(ct);
					await tx.CommitAsync(ct);
					val = result;
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
			result2 = val;
		}
		return result2;
	}
}
