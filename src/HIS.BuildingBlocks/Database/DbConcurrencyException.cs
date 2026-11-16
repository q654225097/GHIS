using System;

namespace HIS.BuildingBlocks.Database;

public sealed class DbConcurrencyException : Exception
{
	public string TableName { get; }

	public string EntityId { get; }

	public DbConcurrencyException(string tableName, string entityId, string message)
		: base(message)
	{
		TableName = tableName;
		EntityId = entityId;
	}
}
