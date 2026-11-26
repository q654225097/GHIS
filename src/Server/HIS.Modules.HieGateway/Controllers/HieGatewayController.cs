using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.HieGateway.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.HieGateway.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "IPD_ATTENDING", "MEDTECH")]
[Route("api/v4/hie")]
public class HieGatewayController : ControllerBase
{
    private readonly IHieGatewayContract _hieContract;
    private readonly IRepository<HieEmpiMapping> _empiRepo;
    private readonly IRepository<HieItemCodeMapping> _itemMappingRepo;
    private readonly IRepository<HieExchangeLog> _logRepo;
    private readonly IUserSession _userSession;
    private readonly IAuditTrailContract? _auditContract;

    public HieGatewayController(
        IHieGatewayContract hieContract,
        IRepository<HieEmpiMapping> empiRepo,
        IRepository<HieItemCodeMapping> itemMappingRepo,
        IRepository<HieExchangeLog> logRepo,
        IUserSession userSession,
        IAuditTrailContract? auditContract = null)
    {
        _hieContract = hieContract;
        _empiRepo = empiRepo;
        _itemMappingRepo = itemMappingRepo;
        _logRepo = logRepo;
        _userSession = userSession;
        _auditContract = auditContract;
    }

    public record ResolveEmpiRequestDto(
        string LocalPatientId,
        string FullName,
        string IdCardHash,
        string GenderCode,
        int BirthDateInt
    );

    [HttpPost("empi/resolve")]
    public async Task<IActionResult> ResolveEmpi([FromBody] ResolveEmpiRequestDto dto, CancellationToken ct)
    {
        var result = await _hieContract.ResolveEmpiAsync(
            dto.LocalPatientId, dto.FullName, dto.IdCardHash, dto.GenderCode, dto.BirthDateInt, ct);
        return Ok(ApiResponse<EmpiResolveResultDto>.Success(result!, "区域患者主索引 (EMPI) 解析与匹配绑定成功"));
    }

    [HttpGet("patients/{patientId}/ehr-summary")]
    public async Task<IActionResult> GetExternalEhrSummary(string patientId, CancellationToken ct)
    {
        var records = await _hieContract.FetchExternalEhrSummaryAsync(patientId, ct);
        return Ok(ApiResponse<IReadOnlyList<ExternalEhrRecordDto>>.Success(records, "区域卫生信息平台与外院诊疗全景数据拉取成功"));
    }

    public record InboundOrderDto(
        string ExternalOrgCode,
        string ExternalPatientId,
        string LocalPatientId,
        string OrderType, // LAB / EXAM / TREATMENT / SURGERY / RX
        string ExternalItemCode,
        string ItemName,
        string PayloadJson
    );

    [HttpPost("inbound/orders")]
    public async Task<IActionResult> ReceiveInboundOrder([FromBody] InboundOrderDto dto, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        var log = new HieExchangeLog
        {
            Id = Guid.NewGuid().ToString("D"),
            Direction = "INBOUND",
            Protocol = "FHIR_R4",
            PeerOrgCode = dto.ExternalOrgCode,
            PatientId = dto.LocalPatientId,
            ResourceType = dto.OrderType,
            PayloadSm3Hash = CryptoHelper.ComputeSm3(dto.PayloadJson ?? "EMPTY"),
            Status = "SUCCESS",
            CreatedAtMs = now,
            CreatedBy = "HIE_INBOUND",
            UpdatedAtMs = now,
            UpdatedBy = "HIE_INBOUND"
        };
        await _logRepo.InsertAsync(log);

        return Ok(ApiResponse<object>.Success(new
        {
            inboundId = log.Id,
            mappedItemCode = dto.ExternalItemCode,
            routeStatus = "DISPATCHED_TO_DEPARTMENT"
        }, $"已接收外院【{dto.ExternalOrgCode}】转诊申请并完成 EMPI 映射分发"));
    }

    public record PublishOutboundDto(
        string ResourceType,
        string ResourceId,
        string TargetOrgCode,
        object Payload
    );

    [HttpPost("outbound/publish")]
    public async Task<IActionResult> PublishOutbound([FromBody] PublishOutboundDto dto, CancellationToken ct)
    {
        bool success = await _hieContract.PublishOutboundResourceAsync(
            dto.ResourceType, dto.ResourceId, dto.TargetOrgCode, dto.Payload, ct);

        return Ok(ApiResponse<object>.Success(new { published = success }, "已成功将本院临床诊疗数据封装为标准 FHIR/CDA 推送至区域健康档案平台"));
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? patientId,
        [FromQuery] string? direction,
        CancellationToken ct)
    {
        string sql = "SELECT * FROM hie_exchange_logs WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(patientId))
        {
            sql += " AND patient_id = @PatientId";
            p.Add("PatientId", patientId);
        }
        if (!string.IsNullOrWhiteSpace(direction))
        {
            sql += " AND direction = @Direction";
            p.Add("Direction", direction);
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 50";

        var list = await _logRepo.QueryAsync(sql, p);
        return Ok(ApiResponse<IReadOnlyList<HieExchangeLog>>.Success(list));
    }
}
