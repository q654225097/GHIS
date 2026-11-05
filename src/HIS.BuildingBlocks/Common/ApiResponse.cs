namespace HIS.BuildingBlocks.Common;

public sealed record ApiResponse<T>
{
    public int Code { get; init; } = 200;
    public string Message { get; init; } = "OK";
    public T? Data { get; init; }
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N");
    public long TimestampMs { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static ApiResponse<T> Success(T data, string message = "OK") =>
        new() { Code = 200, Message = message, Data = data };

    public static ApiResponse<T> Fail(int code, string message, T? data = default) =>
        new() { Code = code, Message = message, Data = data };
}

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string Error { get; }
    public string ErrorCode { get; }

    private Result(bool isSuccess, T? value, string error, string errorCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result<T> Success(T value) => new(true, value, string.Empty, string.Empty);
    public static Result<T> Fail(string error, string errorCode = "OPERATION_FAILED") => new(false, default, error, errorCode);
}

public static class SystemTime
{
    public static long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static int TodayInt
    {
        get
        {
            var now = DateTime.UtcNow;
            return now.Year * 10000 + now.Month * 100 + now.Day;
        }
    }
}

