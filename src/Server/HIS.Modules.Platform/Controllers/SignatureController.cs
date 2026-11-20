using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Platform.Controllers;

[ApiController]
[Route("api/v4/security/signatures")]
public class SignatureController : ControllerBase
{
    private readonly IClinicalProcessSignatureContract _signatureService;

    public SignatureController(IClinicalProcessSignatureContract signatureService)
    {
        _signatureService = signatureService;
    }

    [HttpGet("{entityType}/{entityId}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SecDigitalSignatureDto>>>> GetSignatures(
        string entityType,
        string entityId,
        CancellationToken ct)
    {
        var result = await _signatureService.GetSignaturesAsync(entityType, entityId, ct);
        return Ok(ApiResponse<IReadOnlyList<SecDigitalSignatureDto>>.Success(result, "获取过程电子签名成功"));
    }

    [HttpPost("{signatureId}/verify")]
    public async Task<ActionResult<ApiResponse<SignatureVerificationReportDto>>> VerifySignature(
        string signatureId,
        CancellationToken ct)
    {
        var report = await _signatureService.VerifySignatureAsync(signatureId, ct);
        return Ok(ApiResponse<SignatureVerificationReportDto>.Success(report, report.OverallValid ? "签名验真通过" : "签名验真失败"));
    }
}
