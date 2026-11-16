namespace HIS.BuildingBlocks.Common;

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

	public static Result<T> Success(T value)
	{
		return new Result<T>(isSuccess: true, value, string.Empty, string.Empty);
	}

	public static Result<T> Fail(string error, string errorCode = "OPERATION_FAILED")
	{
		return new Result<T>(isSuccess: false, default, error, errorCode);
	}
}
