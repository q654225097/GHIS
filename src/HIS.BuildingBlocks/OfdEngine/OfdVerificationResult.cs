namespace HIS.BuildingBlocks.OfdEngine;

public sealed record OfdVerificationResult(bool IsValid, string DocId, string DocTitle, int PageCount, bool HasSemanticXml, bool HasSignature, string ContentSha256, string? ErrorMessage);
