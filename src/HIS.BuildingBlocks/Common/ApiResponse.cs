namespace HIS.BuildingBlocks.Common;

public sealed record ApiResponse<T>
{
	public int Code { get; init; }

	public string Message { get; init; }

	public T? Data { get; init; }

	public string TraceId { get; init; }

	public long TimestampMs { get; init; }

	public static ApiResponse<T> Success(T data, string message = "OK")
	{
		return new ApiResponse<T>
		{
			Code = 200,
			Message = message,
			Data = data
		};
	}

	public static ApiResponse<T> Fail(int code, string message, T? data = default(T?))
	{
		return new ApiResponse<T>
		{
			Code = code,
			Message = message,
			Data = data
		};
	}
}
