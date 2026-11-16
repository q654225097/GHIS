using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public class DapperRepository<T> : IRepository<T> where T : BaseEntity
{
	protected readonly IDbConnectionFactory ConnectionFactory;

	private static readonly PropertyInfo[] Properties;

	public string TableName { get; }

	static DapperRepository()
	{
		Properties = (from p in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
			where p.CanRead && p.CanWrite
			select p).ToArray();
		SqliteTypeHandlers.Initialize();
		DefaultTypeMap.MatchNamesWithUnderscores = true;
	}

	public DapperRepository(IDbConnectionFactory connectionFactory, string tableName)
	{
		ConnectionFactory = connectionFactory;
		TableName = tableName;
	}

	public static string ToSnakeCase(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return input;
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < input.Length; i++)
		{
			char c = input[i];
			if (char.IsUpper(c))
			{
				if (i > 0)
				{
					stringBuilder.Append('_');
				}
				stringBuilder.Append(char.ToLowerInvariant(c));
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		string sql = "SELECT * FROM " + TableName + " WHERE id = @Id";
		return await conn.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, new
		{
			Id = id
		}, ConnectionFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
	}

	public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		string sql = "SELECT * FROM " + TableName;
		return (await conn.QueryAsync<T>(new CommandDefinition(sql, null, ConnectionFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct))).ToList();
	}

	public async Task<IReadOnlyList<T>> QueryAsync(string sql, object? param = null, CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		return (await conn.QueryAsync<T>(new CommandDefinition(sql, param, ConnectionFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct))).ToList();
	}

	public async Task<T?> QuerySingleOrDefaultAsync(string sql, object? param = null, CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		return await conn.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, param, ConnectionFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
	}

	public async Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		return await conn.ExecuteAsync(new CommandDefinition(sql, param, ConnectionFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
	}

	public async Task<int> InsertAsync(T entity, CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		List<string> cols = new List<string>();
		List<string> paramNames = new List<string>();
		DynamicParameters dynamicParams = new DynamicParameters();
		PropertyInfo[] properties = Properties;
		foreach (PropertyInfo prop in properties)
		{
			string col = ToSnakeCase(prop.Name);
			cols.Add(col);
			paramNames.Add("@" + prop.Name);
			dynamicParams.Add(prop.Name, prop.GetValue(entity));
		}
		string sql = $"INSERT INTO {TableName} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", paramNames)})";
		return await conn.ExecuteAsync(new CommandDefinition(sql, dynamicParams, ConnectionFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
	}

	public async Task<int> UpdateAsync(T entity, CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		List<string> sets = new List<string>();
		DynamicParameters dynamicParams = new DynamicParameters();
		entity.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		int currentVersion = entity.VersionNo;
		T val = entity;
		int versionNo = val.VersionNo;
		val.VersionNo = versionNo + 1;
		PropertyInfo[] properties = Properties;
		foreach (PropertyInfo prop in properties)
		{
			if (!(prop.Name == "Id"))
			{
				string col = ToSnakeCase(prop.Name);
				sets.Add(col + " = @" + prop.Name);
				dynamicParams.Add(prop.Name, prop.GetValue(entity));
			}
		}
		dynamicParams.Add("Id", entity.Id);
		dynamicParams.Add("ExpectedVersion", currentVersion);
		string sql = $"UPDATE {TableName} SET {string.Join(", ", sets)} WHERE id = @Id AND version_no = @ExpectedVersion";
		DbTransaction? currentTransaction = ConnectionFactory.CurrentTransaction;
		CancellationToken cancellationToken = ct;
		int affected = await conn.ExecuteAsync(new CommandDefinition(sql, dynamicParams, currentTransaction, null, null, CommandFlags.Buffered, cancellationToken));
		if (affected == 0)
		{
			string commandText = "SELECT count(*) FROM " + TableName + " WHERE id = @Id";
			var parameters = new { entity.Id };
			DbTransaction? currentTransaction2 = ConnectionFactory.CurrentTransaction;
			cancellationToken = ct;
			if (await conn.ExecuteScalarAsync<int>(new CommandDefinition(commandText, parameters, currentTransaction2, null, null, CommandFlags.Buffered, cancellationToken)) > 0)
			{
				entity.VersionNo = currentVersion;
				throw new DbConcurrencyException(TableName, entity.Id, $"并发乐观锁冲突：表 [{TableName}] 记录 [{entity.Id}] 已被其他事务修改 (期望版本 {currentVersion})");
			}
		}
		return affected;
	}

	public async Task<int> DeleteAsync(string id, CancellationToken ct = default(CancellationToken))
	{
		using DbConnection conn = ConnectionFactory.CreateConnection();
		string sql = "DELETE FROM " + TableName + " WHERE id = @Id";
		return await conn.ExecuteAsync(new CommandDefinition(sql, new
		{
			Id = id
		}, ConnectionFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
	}
}
