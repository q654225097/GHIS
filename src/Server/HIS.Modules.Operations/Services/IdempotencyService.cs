using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Operations.Models;
using Microsoft.Data.Sqlite;

namespace HIS.Modules.Operations.Services;

public class IdempotencyService : IIdempotencyContract
{
	private readonly IRepository<OpsIdempotencyKey> _idempotencyRepo;

	private readonly IDbConnectionFactory? _connFactory;

	public IdempotencyService(IRepository<OpsIdempotencyKey> idempotencyRepo, IDbConnectionFactory? connFactory = null)
	{
		_idempotencyRepo = idempotencyRepo;
		_connFactory = connFactory;
	}

	public async Task<(bool IsDuplicate, string? CachedResponseJson, bool IsConflict)> TryAcquireAsync(string requestKey, string payloadJson, TimeSpan? ttl = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(requestKey))
		{
			return (IsDuplicate: false, CachedResponseJson: null, IsConflict: false);
		}
		string key = requestKey.Trim();
		string hash = CryptoHelper.ComputeSha256(payloadJson ?? string.Empty);
		long ttlMs = (long)(ttl ?? TimeSpan.FromHours(24)).TotalMilliseconds;
		if (ttlMs <= 0)
			throw new ArgumentOutOfRangeException(nameof(ttl), "Idempotency TTL must be positive.");
		if (_connFactory == null)
			return await TryAcquireLegacyAsync(key, hash, ttlMs, ct);
		if (_connFactory is SqliteConnectionFactory sqliteFactory && !sqliteFactory.IsAmbientTransaction)
			return await TryAcquireSqliteAsync(sqliteFactory, key, hash, ttlMs, ct);
		await using DbConnection conn = _connFactory.CreateConnection();
		for (int attempt = 0; attempt < 100; attempt++)
		{
			if (attempt > 0)
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(100, 5 * attempt)), ct);
			long nowMs = SystemTime.UtcNowMs;
			IdempotencyRow? existing = await ReadAsync(conn, key, ct);
			if (existing != null && existing.ExpiresAtMs >= nowMs)
			{
				if (!string.Equals(existing.RequestSha256, hash, StringComparison.OrdinalIgnoreCase))
					return (IsDuplicate: true, CachedResponseJson: null, IsConflict: true);
				return (IsDuplicate: true, CachedResponseJson: existing.ResponseJson, IsConflict: false);
			}
			int inserted = await ExecuteWithLockRetryAsync(conn, () => conn.ExecuteAsync(new CommandDefinition(
				"INSERT OR IGNORE INTO ops_idempotency_keys (id, request_key, request_sha256, response_json, expires_at_ms, created_at_ms, updated_at_ms, version_no) " +
				"VALUES (@Id, @Key, @Hash, NULL, @ExpiresAtMs, @NowMs, @NowMs, 1)",
				new { Id = Guid.NewGuid().ToString("D"), Key = key, Hash = hash, ExpiresAtMs = nowMs + ttlMs, NowMs = nowMs },
				cancellationToken: ct)), ct);
			if (inserted == 1)
				return (IsDuplicate: false, CachedResponseJson: null, IsConflict: false);
			IdempotencyRow? current = await ReadAsync(conn, key, ct);
			if (current == null)
				continue;
			if (current.ExpiresAtMs >= nowMs)
			{
				if (!string.Equals(current.RequestSha256, hash, StringComparison.OrdinalIgnoreCase))
					return (IsDuplicate: true, CachedResponseJson: null, IsConflict: true);
				return (IsDuplicate: true, CachedResponseJson: current.ResponseJson, IsConflict: false);
			}
			int replaced = await ExecuteWithLockRetryAsync(conn, () => conn.ExecuteAsync(new CommandDefinition(
				"UPDATE ops_idempotency_keys SET request_sha256 = @Hash, response_json = NULL, expires_at_ms = @NewExpiry, updated_at_ms = @NowMs, version_no = version_no + 1 " +
				"WHERE request_key = @Key AND request_sha256 = @OldHash AND expires_at_ms = @OldExpiry AND expires_at_ms < @NowMs",
				new { Key = key, Hash = hash, OldHash = current.RequestSha256, OldExpiry = current.ExpiresAtMs, NewExpiry = nowMs + ttlMs, NowMs = nowMs },
				cancellationToken: ct)), ct);
			if (replaced == 1)
				return (IsDuplicate: false, CachedResponseJson: null, IsConflict: false);
		}
		throw new InvalidOperationException("Unable to acquire idempotency key after repeated concurrent updates.");
	}

	private static async Task<(bool IsDuplicate, string? CachedResponseJson, bool IsConflict)> TryAcquireSqliteAsync(
		SqliteConnectionFactory factory, string key, string hash, long ttlMs, CancellationToken ct)
	{
		await using SqliteConnection conn = new SqliteConnection(factory.ConnectionString);
		await conn.OpenAsync(ct);
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				await using SqliteTransaction transaction = conn.BeginTransaction(deferred: false);
				long nowMs = SystemTime.UtcNowMs;
				IdempotencyRow? existing = await conn.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition(
					"SELECT request_sha256 AS RequestSha256, response_json AS ResponseJson, expires_at_ms AS ExpiresAtMs FROM ops_idempotency_keys WHERE request_key = @Key",
					new { Key = key }, transaction, cancellationToken: ct));
				if (existing == null)
				{
					await conn.ExecuteAsync(new CommandDefinition(
						"INSERT INTO ops_idempotency_keys (id, request_key, request_sha256, response_json, expires_at_ms, schema_version, created_at_ms, updated_at_ms, version_no) " +
						"VALUES (@Id, @Key, @Hash, NULL, @ExpiresAtMs, 1, @NowMs, @NowMs, 1)",
						new { Id = Guid.NewGuid().ToString("D"), Key = key, Hash = hash, ExpiresAtMs = nowMs + ttlMs, NowMs = nowMs },
						transaction, cancellationToken: ct));
					await transaction.CommitAsync(ct);
					return (IsDuplicate: false, CachedResponseJson: null, IsConflict: false);
				}
				if (existing.ExpiresAtMs >= nowMs)
				{
					await transaction.CommitAsync(ct);
					if (!string.Equals(existing.RequestSha256, hash, StringComparison.OrdinalIgnoreCase))
						return (IsDuplicate: true, CachedResponseJson: null, IsConflict: true);
					return (IsDuplicate: true, CachedResponseJson: existing.ResponseJson, IsConflict: false);
				}
				await conn.ExecuteAsync(new CommandDefinition(
					"UPDATE ops_idempotency_keys SET request_sha256 = @Hash, response_json = NULL, expires_at_ms = @NewExpiry, updated_at_ms = @NowMs, version_no = version_no + 1 " +
					"WHERE request_key = @Key AND expires_at_ms = @OldExpiry",
					new { Key = key, Hash = hash, OldExpiry = existing.ExpiresAtMs, NewExpiry = nowMs + ttlMs, NowMs = nowMs },
					transaction, cancellationToken: ct));
				await transaction.CommitAsync(ct);
				return (IsDuplicate: false, CachedResponseJson: null, IsConflict: false);
			}
			catch (SqliteException ex) when ((ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) && attempt < 20)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), ct);
			}
		}
	}

	private async Task<(bool IsDuplicate, string? CachedResponseJson, bool IsConflict)> TryAcquireLegacyAsync(string key, string hash, long ttlMs, CancellationToken ct)
	{
		long nowMs = SystemTime.UtcNowMs;
		OpsIdempotencyKey existing = await _idempotencyRepo.QuerySingleOrDefaultAsync("SELECT * FROM ops_idempotency_keys WHERE request_key = @Key", new
		{
			Key = key
		}, ct);
		if (existing != null)
		{
			if (existing.ExpiresAtMs >= nowMs)
			{
				if (!string.Equals(existing.RequestSha256, hash, StringComparison.OrdinalIgnoreCase))
				{
					return (IsDuplicate: true, CachedResponseJson: null, IsConflict: true);
				}
				return (IsDuplicate: true, CachedResponseJson: existing.ResponseJson, IsConflict: false);
			}
			await _idempotencyRepo.DeleteAsync(existing.Id, ct);
		}
		OpsIdempotencyKey record = new OpsIdempotencyKey
		{
			RequestKey = key,
			RequestSha256 = hash,
			ExpiresAtMs = nowMs + ttlMs
		};
		await _idempotencyRepo.InsertAsync(record, ct);
		return (IsDuplicate: false, CachedResponseJson: null, IsConflict: false);
	}

	public async Task SaveResponseAsync(string requestKey, string responseJson, CancellationToken ct = default(CancellationToken))
	{
		if (!string.IsNullOrWhiteSpace(requestKey) && _connFactory != null)
		{
			await using DbConnection conn = _connFactory.CreateConnection();
			await conn.ExecuteAsync(new CommandDefinition(
				"UPDATE ops_idempotency_keys SET response_json = @ResponseJson, updated_at_ms = @NowMs, version_no = version_no + 1 " +
				"WHERE request_key = @Key AND expires_at_ms >= @NowMs",
				new { Key = requestKey.Trim(), ResponseJson = responseJson, NowMs = SystemTime.UtcNowMs },
				cancellationToken: ct));
		}
		else if (!string.IsNullOrWhiteSpace(requestKey))
		{
			OpsIdempotencyKey existing = await _idempotencyRepo.QuerySingleOrDefaultAsync("SELECT * FROM ops_idempotency_keys WHERE request_key = @Key", new
			{
				Key = requestKey.Trim()
			}, ct);
			if (existing != null)
			{
				existing.ResponseJson = responseJson;
				await _idempotencyRepo.UpdateAsync(existing, ct);
			}
		}
	}

	private static async Task<IdempotencyRow?> ReadAsync(DbConnection conn, string key, CancellationToken ct)
	{
		return await conn.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition(
			"SELECT request_sha256 AS RequestSha256, response_json AS ResponseJson, expires_at_ms AS ExpiresAtMs FROM ops_idempotency_keys WHERE request_key = @Key",
			new { Key = key },
			cancellationToken: ct));
	}

	private static async Task<int> ExecuteWithLockRetryAsync(DbConnection conn, Func<Task<int>> action, CancellationToken ct)
	{
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				return await action();
			}
			catch (SqliteException ex) when ((ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) && attempt < 6)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), ct);
			}
		}
	}

	private sealed class IdempotencyRow
	{
		public string RequestSha256 { get; set; } = string.Empty;
		public string? ResponseJson { get; set; }
		public long ExpiresAtMs { get; set; }
	}
}
