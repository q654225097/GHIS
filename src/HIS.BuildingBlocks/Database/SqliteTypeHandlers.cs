using System;
using System.Data;
using System.Globalization;
using System.Threading;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static class SqliteTypeHandlers
{
	public sealed class SqliteDecimalHandler : SqlMapper.TypeHandler<decimal>
	{
		public override void SetValue(IDbDataParameter parameter, decimal value)
		{
			parameter.Value = value;
		}

		public override decimal Parse(object value)
		{
			return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
		}
	}

	public sealed class SqliteNullableDecimalHandler : SqlMapper.TypeHandler<decimal?>
	{
		public override void SetValue(IDbDataParameter parameter, decimal? value)
		{
			parameter.Value = ((object)value) ?? DBNull.Value;
		}

		public override decimal? Parse(object value)
		{
			bool flag = ((value == null || value is DBNull) ? true : false);
			return flag ? ((decimal?)null) : new decimal?(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
		}
	}

	public sealed class SqliteDoubleHandler : SqlMapper.TypeHandler<double>
	{
		public override void SetValue(IDbDataParameter parameter, double value)
		{
			parameter.Value = value;
		}

		public override double Parse(object value)
		{
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}
	}

	public sealed class SqliteNullableDoubleHandler : SqlMapper.TypeHandler<double?>
	{
		public override void SetValue(IDbDataParameter parameter, double? value)
		{
			parameter.Value = ((object)value) ?? DBNull.Value;
		}

		public override double? Parse(object value)
		{
			bool flag = ((value == null || value is DBNull) ? true : false);
			return flag ? ((double?)null) : new double?(Convert.ToDouble(value, CultureInfo.InvariantCulture));
		}
	}

	private static int _initialized;

	public static void Initialize()
	{
		if (Interlocked.Exchange(ref _initialized, 1) == 0)
		{
			SqlMapper.AddTypeHandler(new SqliteDecimalHandler());
			SqlMapper.AddTypeHandler(new SqliteNullableDecimalHandler());
			SqlMapper.AddTypeHandler(new SqliteDoubleHandler());
			SqlMapper.AddTypeHandler(new SqliteNullableDoubleHandler());
		}
	}
}
