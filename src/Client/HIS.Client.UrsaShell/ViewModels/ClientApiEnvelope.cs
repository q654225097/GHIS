namespace HIS.Client.UrsaShell.ViewModels;

public sealed class ClientApiEnvelope<T>
{
	public int Code { get; set; }

	public string Message { get; set; } = string.Empty;

	public T? Data { get; set; }
}
