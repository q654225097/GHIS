namespace HIS.Modules.Operations.Services;

public sealed record AuditChainVerificationResult(bool IsIntact, int VerifiedEventCount, string? BrokenAtEventId, string Message);
