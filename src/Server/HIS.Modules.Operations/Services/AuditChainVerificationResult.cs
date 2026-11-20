namespace HIS.Modules.Operations.Services;

public sealed record AuditChainVerificationResult(
    bool IsIntact,
    int VerifiedEventCount,
    string? BrokenAtEventId,
    string Message)
{
    public bool IsValid => IsIntact;
    public int CheckedEvents => VerifiedEventCount;
    public string BreakReason => Message;
    public bool AnchorTruncated => Message != null && Message.Contains("截断");
}
