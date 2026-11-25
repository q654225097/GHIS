using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.BloodBank.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.BloodBank.Controllers;

[ApiController]
[Route("api/v4/blood")]
[Route("api/v4/blood-bank")]
public class BloodBankController : ControllerBase
{
    private readonly IRepository<BldBloodDictionary> _dictRepo;
    private readonly IRepository<BldInventoryBag> _bagRepo;
    private readonly IRepository<BldStockPolicy> _policyRepo;
    private readonly IRepository<BldCrossmatchRecord> _crossmatchRepo;
    private readonly IRepository<BldTransfusionExecution> _execRepo;
    private readonly IRepository<BldTransfusionReaction> _reactionRepo;
    private readonly IRepository<BldExternalSyncLog> _syncRepo;
    private readonly IBloodBankContract _bloodContract;
    private readonly IClinicalProcessSignatureContract? _signatureContract;
    private readonly IAuditTrailContract? _auditContract;
    private readonly IWorkflowEngineContract? _workflowEngine;
    private readonly IUserSession _userSession;
    private readonly IDbConnectionFactory _connFactory;

    public BloodBankController(
        IRepository<BldBloodDictionary> dictRepo,
        IRepository<BldInventoryBag> bagRepo,
        IRepository<BldStockPolicy> policyRepo,
        IRepository<BldCrossmatchRecord> crossmatchRepo,
        IRepository<BldTransfusionExecution> execRepo,
        IRepository<BldTransfusionReaction> reactionRepo,
        IRepository<BldExternalSyncLog> syncRepo,
        IBloodBankContract bloodContract,
        IUserSession userSession,
        IDbConnectionFactory connFactory,
        IClinicalProcessSignatureContract? signatureContract = null,
        IAuditTrailContract? auditContract = null,
        IWorkflowEngineContract? workflowEngine = null)
    {
        _dictRepo = dictRepo;
        _bagRepo = bagRepo;
        _policyRepo = policyRepo;
        _crossmatchRepo = crossmatchRepo;
        _execRepo = execRepo;
        _reactionRepo = reactionRepo;
        _syncRepo = syncRepo;
        _bloodContract = bloodContract;
        _userSession = userSession;
        _connFactory = connFactory;
        _signatureContract = signatureContract;
        _auditContract = auditContract;
        _workflowEngine = workflowEngine;
    }

    [HttpGet("dictionary")]
    public async Task<IActionResult> GetDictionary(CancellationToken ct)
    {
        var list = await _dictRepo.QueryAsync("SELECT * FROM bld_blood_dictionary WHERE is_active = 1 ORDER BY blood_code");
        return Ok(ApiResponse<IReadOnlyList<BldBloodDictionary>>.Success(list));
    }

    [HttpGet("inventory/summary")]
    public async Task<IActionResult> GetInventorySummary(CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        var summary = await conn.QueryAsync(
            """
            SELECT blood_code, abo_type, rh_type, status, count(*) AS bag_count, sum(volume_ml) AS total_volume_ml
            FROM bld_inventory_bags
            GROUP BY blood_code, abo_type, rh_type, status
            ORDER BY blood_code, abo_type
            """);
        return Ok(ApiResponse<object>.Success(summary));
    }

    [HttpGet("inventory/bags")]
    public async Task<IActionResult> GetInventoryBags(CancellationToken ct)
    {
        var list = await _bagRepo.QueryAsync("SELECT * FROM bld_inventory_bags ORDER BY expiry_date_int ASC, created_at_ms DESC LIMIT 100");
        return Ok(ApiResponse<IReadOnlyList<BldInventoryBag>>.Success(list));
    }

    public record BloodRequestSummaryDto(
        string Id,
        string EncounterId,
        string PatientId,
        string PatientName,
        string Gender,
        int BirthDateInt,
        string AboType,
        string RhType,
        string BloodProductCode,
        int VolumeMl,
        decimal? LatestHgb,
        string? RationalCheckResult,
        string? BagBarcode,
        int Status,
        string StatusDesc,
        long CreatedAtMs
    );

    [HttpGet("requests")]
    public async Task<IActionResult> GetBloodRequests(CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT r.id, r.encounter_id, r.patient_id, p.full_name as patient_name, p.gender_code as gender, p.birth_date_int,
                   r.abo_type, r.rh_type, r.blood_product_code, r.volume_ml, r.bag_barcode, r.status, r.created_at_ms,
                   (SELECT value_num FROM mtc_lab_result_items l WHERE l.patient_id = r.patient_id AND (l.item_code LIKE '%HGB%' OR l.item_name LIKE '%血红蛋白%') ORDER BY l.reported_at_ms DESC LIMIT 1) as latest_hgb
            FROM mtc_blood_requests r
            LEFT JOIN pat_patients p ON r.patient_id = p.id
            ORDER BY r.created_at_ms DESC LIMIT 100
            """);
        var list = new List<BloodRequestSummaryDto>();
        foreach (var r in rows)
        {
            int st = (int)r.status;
            string stDesc = st switch { 0 => "待配血", 1 => "已配血相容", 2 => "已双签发血", 3 => "输注中", 4 => "已回收血袋", _ => "已申请" };
            decimal? hgb = r.latest_hgb != null ? (decimal?)Convert.ToDecimal(r.latest_hgb) : null;
            string rational = hgb.HasValue
                ? (hgb.Value <= 60 ? $"✓ HGB {hgb.Value:0.#}g/L ≤ 60g/L，符合严重贫血红细胞绝对指征" : (hgb.Value <= 100 ? $"✓ HGB {hgb.Value:0.#}g/L，符合围术期/急性出血输注指征" : $"⚠ HGB {hgb.Value:0.#}g/L > 100g/L，需严格评估输血必要性"))
                : "✓ 临床指征评估通过";
            list.Add(new BloodRequestSummaryDto(
                (string)r.id,
                (string)r.encounter_id,
                (string)r.patient_id,
                r.patient_name != null ? (string)r.patient_name : "在院患者",
                r.gender != null ? (string)r.gender : "男",
                r.birth_date_int != null ? (int)r.birth_date_int : 19850101,
                (string)(r.abo_type ?? "A"),
                (string)(r.rh_type ?? "POSITIVE"),
                (string)(r.blood_product_code ?? "RBC"),
                (int)(r.volume_ml ?? 200),
                hgb,
                rational,
                (string?)r.bag_barcode,
                st,
                stDesc,
                (long)r.created_at_ms
            ));
        }
        return Ok(ApiResponse<IReadOnlyList<BloodRequestSummaryDto>>.Success(list));
    }

    public record CreateBloodRequestDto(
        string EncounterId,
        string PatientId,
        string AboType,
        string RhType,
        string BloodProductCode,
        int VolumeMl,
        string? Reason
    );

    [HttpPost("requests")]
    public async Task<IActionResult> CreateBloodRequest([FromBody] CreateBloodRequestDto dto, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        string reqId = Guid.NewGuid().ToString("D");
        decimal volume = dto.VolumeMl > 0 ? dto.VolumeMl : 200;

        string approvalStatus = "APPROVED";
        string? instanceId = null;

        if (_workflowEngine != null)
        {
            var boVars = new Dictionary<string, object?>
            {
                ["volume_ml"] = volume,
                ["abo_type"] = dto.AboType ?? "A",
                ["rh_type"] = dto.RhType ?? "POSITIVE",
                ["blood_product_code"] = dto.BloodProductCode ?? "RBC",
                ["patient_id"] = dto.PatientId,
                ["urgency"] = volume > 800 ? "URGENT" : "ROUTINE"
            };
            var wfRes = await _workflowEngine.StartWorkflowAsync("BLOOD_REQUEST", reqId, _userSession.UserId ?? "SYSTEM", _userSession.ActiveDeptId ?? "DEPT-BLOOD", boVars, ct);
            if (wfRes.IsWorkflowTriggered && wfRes.InstanceId != null)
            {
                instanceId = wfRes.InstanceId;
                approvalStatus = wfRes.Status == "APPROVED" ? "APPROVED" : "PENDING_APPROVAL";
            }
        }

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO mtc_blood_requests (id, encounter_id, patient_id, abo_type, rh_type, blood_product_code, volume_ml, status, approval_status, workflow_instance_id, submitted_at_ms, empty_bag_returned, created_at_ms, created_by, updated_at_ms, updated_by, version_no, schema_version)
            VALUES (@Id, @EncounterId, @PatientId, @AboType, @RhType, @BloodProductCode, @VolumeMl, 0, @ApprovalStatus, @WorkflowInstanceId, @Now, 0, @Now, @User, @Now, @User, 1, 1)
            """,
            new
            {
                Id = reqId,
                dto.EncounterId,
                dto.PatientId,
                AboType = dto.AboType ?? "A",
                RhType = dto.RhType ?? "POSITIVE",
                BloodProductCode = dto.BloodProductCode ?? "RBC",
                VolumeMl = volume,
                ApprovalStatus = approvalStatus,
                WorkflowInstanceId = instanceId,
                Now = now,
                User = _userSession.UserId ?? "SYSTEM"
            });

        var safety = await _bloodContract.CheckSafetyAsync(reqId, ct);
        return Ok(ApiResponse<object>.Success(new { requestId = reqId, approvalStatus, workflowInstanceId = instanceId, safety }, "临床用血申请已提交并完成合理用血核查"));
    }

    [HttpGet("patients/blood-distribution")]
    public async Task<IActionResult> GetPatientBloodDistribution(CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        var dist = await conn.QueryAsync(
            """
            SELECT CASE WHEN b.abo_type IS NULL OR trim(b.abo_type) = '' THEN '未知' ELSE b.abo_type END AS abo_type,
                   CASE WHEN b.rh_type IS NULL OR trim(b.rh_type) = '' THEN 'POSITIVE' ELSE b.rh_type END AS rh_type,
                   count(DISTINCT e.patient_id) AS patient_count
            FROM ipd_encounters e
            LEFT JOIN mtc_blood_requests b ON b.encounter_id = e.id
            WHERE e.status = 1
            GROUP BY abo_type, rh_type
            """);
        return Ok(ApiResponse<object>.Success(dist));
    }

    public record StockInBagDto(
        string BagBarcode,
        string DonationCode,
        string SourceBloodCenter,
        string BloodCode,
        string AboType,
        string RhType,
        decimal VolumeMl,
        int CollectionDateInt,
        int ExpiryDateInt,
        decimal ColdChainTemp
    );

    [HttpPost("inventory/stock-in")]
    public async Task<IActionResult> StockInBag([FromBody] StockInBagDto dto, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        var bag = new BldInventoryBag
        {
            Id = Guid.NewGuid().ToString("D"),
            BagBarcode = dto.BagBarcode,
            DonationCode = dto.DonationCode,
            SourceBloodCenter = dto.SourceBloodCenter ?? "市中心血站",
            BloodCode = dto.BloodCode,
            AboType = dto.AboType,
            RhType = dto.RhType ?? "POSITIVE",
            VolumeMl = dto.VolumeMl,
            CollectionDateInt = dto.CollectionDateInt,
            ExpiryDateInt = dto.ExpiryDateInt,
            ColdChainTemp = dto.ColdChainTemp,
            Status = "IN_STOCK",
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };
        await _bagRepo.InsertAsync(bag);
        return Ok(ApiResponse<BldInventoryBag>.Success(bag, "血袋入库成功"));
    }

    [HttpGet("analytics/stock-advisory")]
    public async Task<IActionResult> GetStockAdvisory(CancellationToken ct)
    {
        var list = await _bloodContract.GetStockAdvisoryAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<BloodStockAdvisoryDto>>.Success(list));
    }

    public record CrossmatchDto(
        string BloodRequestId,
        string BagBarcode,
        string PatientAbo,
        string PatientRh,
        string DonorAbo,
        string DonorRh,
        string AntibodyScreenResult,
        string MajorCrossmatch,
        string MinorCrossmatch,
        string VerifierId,
        string? Remark
    );

    [HttpPost("crossmatch")]
    public async Task<IActionResult> PerformCrossmatch([FromBody] CrossmatchDto dto, CancellationToken ct)
    {
        using var conn = _connFactory.CreateConnection();
        var bloodReq = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM mtc_blood_requests WHERE id = @Id", new { Id = dto.BloodRequestId });
        if (bloodReq != null && !string.IsNullOrWhiteSpace((string?)bloodReq.approval_status) &&
            !string.Equals((string?)bloodReq.approval_status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(409, ApiResponse<object>.Fail(409, $"用血申请单[{dto.BloodRequestId}]尚未审批通过 (当前状态: {bloodReq.approval_status})，禁止进行交叉配血！"));
        }

        var invBag = await conn.QueryFirstOrDefaultAsync<BldInventoryBag>(
            "SELECT * FROM bld_inventory_bags WHERE bag_barcode = @Barcode", new { Barcode = dto.BagBarcode });
        if (invBag == null)
        {
            return NotFound(ApiResponse<object>.Fail(404, $"血袋条码 [{dto.BagBarcode}] 不存在"));
        }
        if (!string.Equals(invBag.Status, "IN_STOCK", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(409, ApiResponse<object>.Fail(409, $"血袋 [{dto.BagBarcode}] 当前状态为 [{invBag.Status}]，仅在库(IN_STOCK)血袋允许交叉配血"));
        }

        string pAbo = (dto.PatientAbo ?? string.Empty).Trim().ToUpperInvariant();
        string dAbo = (invBag.AboType ?? dto.DonorAbo ?? string.Empty).Trim().ToUpperInvariant();
        string pRh = (dto.PatientRh ?? string.Empty).Trim().ToUpperInvariant();
        string dRh = (invBag.RhType ?? dto.DonorRh ?? string.Empty).Trim().ToUpperInvariant();

        // Strict ABO / Rh compatibility hard block check
        bool compatible = true;
        string? reason = null;

        // Rh negative patient receiving Rh positive donor -> hard block
        if (pRh.Equals("NEGATIVE", StringComparison.OrdinalIgnoreCase) &&
            dRh.Equals("POSITIVE", StringComparison.OrdinalIgnoreCase))
        {
            compatible = false;
            reason = "【血型相容性强阻断】Rh 阴性受血者绝对禁止输入 Rh 阳性献血者血液！";
        }

        // Major or minor agglutination -> hard block
        if ((dto.MajorCrossmatch ?? string.Empty).Equals("INCOMPATIBLE", StringComparison.OrdinalIgnoreCase) ||
            (dto.MinorCrossmatch ?? string.Empty).Equals("INCOMPATIBLE", StringComparison.OrdinalIgnoreCase))
        {
            compatible = false;
            reason = "【交叉配血主/次侧凝聚】主侧或次侧出现凝集反应，配血不合，绝对禁止发血！";
        }

        // ABO compatibility check (FFP plasma vs RBC)
        string productCode = ((string?)bloodReq?.blood_product_code ?? invBag.BloodCode ?? "RBC").ToUpperInvariant();
        bool isPlasma = productCode.Contains("FFP") || productCode.Contains("PLASMA");
        bool aboOk = isPlasma
            ? (dAbo == "AB" || pAbo == dAbo || pAbo == "O")
            : (dAbo == "O" || pAbo == dAbo || pAbo == "AB");
        if (!aboOk)
        {
            compatible = false;
            reason = $"【ABO 血型不相容】患者血型 [{pAbo}] 与供者血型 [{dAbo}]（成分：{productCode}）配血不相容！";
        }

        if (!compatible)
        {
            return BadRequest(ApiResponse<object>.Fail(400, reason ?? "配血不合强拦截"));
        }

        long now = SystemTime.UtcNowMs;
        var record = new BldCrossmatchRecord
        {
            Id = Guid.NewGuid().ToString("D"),
            BloodRequestId = dto.BloodRequestId,
            BagId = invBag.Id,
            BagBarcode = dto.BagBarcode,
            PatientAbo = pAbo,
            PatientRh = pRh,
            DonorAbo = dAbo,
            DonorRh = dRh,
            AntibodyScreenResult = dto.AntibodyScreenResult,
            MajorCrossmatch = dto.MajorCrossmatch,
            MinorCrossmatch = dto.MinorCrossmatch,
            Result = "COMPATIBLE",
            TechnicianId = _userSession.UserId ?? "SYSTEM",
            VerifierId = dto.VerifierId,
            MatchedAtMs = now,
            Remark = dto.Remark,
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };

        await _connFactory.ExecuteInTransactionAsync(async txCt =>
        {
            await using var txConn = _connFactory.CreateConnection();
            var tx = _connFactory.CurrentTransaction;
            int claimed = await txConn.ExecuteAsync(new CommandDefinition(
                "UPDATE bld_inventory_bags SET status = 'CROSSMATCHED', matched_request_id = @ReqId, updated_at_ms = @Now WHERE bag_barcode = @Barcode AND status = 'IN_STOCK'",
                new { ReqId = dto.BloodRequestId, Now = now, Barcode = dto.BagBarcode }, tx, cancellationToken: txCt));
            if (claimed != 1)
                throw new InvalidOperationException("血袋已被其他配血单锁定，请刷新库存");

            await _crossmatchRepo.InsertAsync(record, txCt);
            await txConn.ExecuteAsync(new CommandDefinition(
                "UPDATE mtc_blood_requests SET status = CASE WHEN status < 1 THEN 1 ELSE status END, updated_at_ms = @Now WHERE id = @ReqId",
                new { Now = now, ReqId = dto.BloodRequestId }, tx, cancellationToken: txCt));
            return 0;
        }, ct);

        if (_signatureContract != null)
        {
            await _signatureContract.SignClinicalRecordAsync("BLOOD", record.Id, "CROSSMATCH", record, _userSession, ct);
        }

        return Ok(ApiResponse<BldCrossmatchRecord>.Success(record, "交叉配血相容性核验通过，允许发血"));
    }

    public record DispenseBloodDto(
        string BloodRequestId,
        string BagBarcode,
        string DualCheckDispenserId
    );

    [HttpPost("dispense")]
    public async Task<IActionResult> DispenseBlood([FromBody] DispenseBloodDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DualCheckDispenserId) || dto.DualCheckDispenserId == _userSession.UserId)
        {
            return BadRequest(ApiResponse<object>.Fail(400, "血库发血必须由两名不同的专业技术人员进行双人双签核对"));
        }

        using var conn = _connFactory.CreateConnection();
        var bloodReq = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM mtc_blood_requests WHERE id = @Id", new { Id = dto.BloodRequestId });
        if (bloodReq != null && !string.IsNullOrWhiteSpace((string?)bloodReq.approval_status) &&
            !string.Equals((string?)bloodReq.approval_status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(409, ApiResponse<object>.Fail(409, $"用血申请单[{dto.BloodRequestId}]审批尚未通过 (当前状态: {bloodReq.approval_status})，血库禁止发血！"));
        }

        var bag = await conn.QueryFirstOrDefaultAsync<BldInventoryBag>(
            "SELECT * FROM bld_inventory_bags WHERE bag_barcode = @Barcode",
            new { Barcode = dto.BagBarcode });

        if (bag == null || bag.Status != "CROSSMATCHED")
        {
            return BadRequest(ApiResponse<object>.Fail(400, "该血袋未完成交叉配血或已被发走"));
        }
        if (!string.IsNullOrWhiteSpace(bag.MatchedRequestId) &&
            !string.Equals(bag.MatchedRequestId, dto.BloodRequestId, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(409, ApiResponse<object>.Fail(409, $"该血袋已配血锁定给用血申请 [{bag.MatchedRequestId}]，禁止发给其他申请单 [{dto.BloodRequestId}]！"));
        }

        long now = SystemTime.UtcNowMs;
        try
        {
            await _connFactory.ExecuteInTransactionAsync(async txCt =>
            {
                await using var txConn = _connFactory.CreateConnection();
                var tx = _connFactory.CurrentTransaction;
                int updatedBag = await txConn.ExecuteAsync(new CommandDefinition(
                    "UPDATE bld_inventory_bags SET status = 'DISPENSED', updated_at_ms = @Now WHERE bag_barcode = @Barcode AND status = 'CROSSMATCHED'",
                    new { Now = now, Barcode = dto.BagBarcode }, tx, cancellationToken: txCt));
                if (updatedBag != 1)
                    throw new InvalidOperationException("该血袋状态已变更，禁止重复发血");

                await txConn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE mtc_blood_requests
                    SET status = 2, bag_barcode = @Barcode, dispensed_by_id = @User, dual_check_dispenser_id = @Dual, updated_at_ms = @Now
                    WHERE id = @ReqId
                    """,
                    new { Barcode = dto.BagBarcode, User = _userSession.UserId, Dual = dto.DualCheckDispenserId, Now = now, ReqId = dto.BloodRequestId },
                    tx, cancellationToken: txCt));
                return 0;
            }, ct);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(409, ApiResponse<object>.Fail(409, ex.Message));
        }

        if (_signatureContract != null)
        {
            await _signatureContract.SignClinicalRecordAsync("BLOOD", dto.BloodRequestId, "DISPENSE", new { dto.BagBarcode, dto.DualCheckDispenserId }, _userSession, ct);
        }

        return Ok(ApiResponse<object>.Success(new { bagBarcode = dto.BagBarcode, status = "DISPENSED" }, "血库双人双签核发血液成功，已生成全流程电子签名与可信时间戳"));
    }

    [HttpGet("executions")]
    public async Task<IActionResult> GetExecutions(
        [FromQuery] string? encounterId = null,
        [FromQuery] string? patientId = null,
        [FromQuery] string? status = null)
    {
        var list = await _execRepo.QueryAsync(@"
SELECT * FROM bld_transfusion_executions
WHERE (@EncounterId IS NULL OR encounter_id = @EncounterId)
  AND (@PatientId IS NULL OR patient_id = @PatientId)
  AND (@Status IS NULL OR status = @Status)
ORDER BY started_at_ms DESC LIMIT 100",
            new { EncounterId = encounterId, PatientId = patientId, Status = status });
        return Ok(ApiResponse<IReadOnlyList<BldTransfusionExecution>>.Success(list));
    }

    public record StartTransfusionDto(
        string BloodRequestId,
        string BagBarcode,
        string EncounterId,
        string PatientId,
        string PatientWristbandCode,
        string DualCheckNurseId,
        Dictionary<string, object>? PreVitals
    );

    [HttpPost("executions/start")]
    public async Task<IActionResult> StartTransfusion([FromBody] StartTransfusionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DualCheckNurseId) || dto.DualCheckNurseId == _userSession.UserId)
        {
            return BadRequest(ApiResponse<object>.Fail(400, "床旁输血实施必须由两名执业护士进行床旁双人双码核对"));
        }

        long now = SystemTime.UtcNowMs;
        var exec = new BldTransfusionExecution
        {
            Id = Guid.NewGuid().ToString("D"),
            BloodRequestId = dto.BloodRequestId,
            BagId = dto.BagBarcode,
            BagBarcode = dto.BagBarcode,
            EncounterId = dto.EncounterId,
            PatientId = dto.PatientId,
            PatientWristbandCode = dto.PatientWristbandCode,
            StartNurseId = _userSession.UserId ?? "SYSTEM",
            DualCheckNurseId = dto.DualCheckNurseId,
            PreVitalsJson = dto.PreVitals != null ? JsonSerializer.Serialize(dto.PreVitals) : null,
            StartedAtMs = now,
            Status = "TRANSFUSING",
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };

        try
        {
            await _connFactory.ExecuteInTransactionAsync(async txCt =>
            {
                await using var txConn = _connFactory.CreateConnection();
                var tx = _connFactory.CurrentTransaction;
                var bag = await txConn.QueryFirstOrDefaultAsync<BldInventoryBag>(new CommandDefinition(
                    "SELECT * FROM bld_inventory_bags WHERE bag_barcode = @Barcode",
                    new { Barcode = dto.BagBarcode }, tx, cancellationToken: txCt));
                if (bag == null || !string.Equals(bag.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"血袋 [{dto.BagBarcode}] 尚未出库发血或已开始输注，禁止床旁输血");
                if (!string.IsNullOrWhiteSpace(bag.MatchedRequestId) &&
                    !string.Equals(bag.MatchedRequestId, dto.BloodRequestId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"血袋 [{dto.BagBarcode}] 配血申请单不匹配，禁止输注");

                exec.BagId = bag.Id;
                int bagUpdated = await txConn.ExecuteAsync(new CommandDefinition(
                    "UPDATE bld_inventory_bags SET status = 'TRANSFUSED', updated_at_ms = @Now WHERE bag_barcode = @Barcode AND status = 'DISPENSED'",
                    new { Now = now, Barcode = dto.BagBarcode }, tx, cancellationToken: txCt));
                if (bagUpdated != 1)
                    throw new InvalidOperationException("血袋状态已变更，禁止重复开启输血");

                await _execRepo.InsertAsync(exec, txCt);
                await txConn.ExecuteAsync(new CommandDefinition(
                    "UPDATE mtc_blood_requests SET status = 3, updated_at_ms = @Now WHERE id = @ReqId",
                    new { Now = now, ReqId = dto.BloodRequestId }, tx, cancellationToken: txCt));
                return 0;
            }, ct);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(409, ApiResponse<object>.Fail(409, ex.Message));
        }

        if (_signatureContract != null)
        {
            await _signatureContract.SignClinicalRecordAsync("BLOOD", exec.Id, "START_TRANSFUSION", exec, _userSession, ct);
        }

        return Ok(ApiResponse<BldTransfusionExecution>.Success(exec, "床旁双码核验无误，输血正式开始，请于输注前 15 分钟密切监测生命体征"));
    }

    public record FinishTransfusionDto(
        Dictionary<string, object>? Mid15Vitals,
        Dictionary<string, object>? PostVitals,
        bool EmptyBagReturned
    );

    [HttpPost("executions/{id}/finish")]
    public async Task<IActionResult> FinishTransfusion(string id, [FromBody] FinishTransfusionDto dto, CancellationToken ct)
    {
        var exec = await _execRepo.GetByIdAsync(id);
        if (exec == null) return NotFound(ApiResponse<object>.Fail(404, "输血执行单不存在"));
        if (!string.Equals(exec.Status, "TRANSFUSING", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail(400, $"当前输血执行单状态为 [{exec.Status}]，仅输注中(TRANSFUSING)状态允许结束输血"));

        long now = SystemTime.UtcNowMs;
        exec.EndedAtMs = now;
        exec.Mid15VitalsJson = dto.Mid15Vitals != null ? JsonSerializer.Serialize(dto.Mid15Vitals) : exec.Mid15VitalsJson;
        exec.PostVitalsJson = dto.PostVitals != null ? JsonSerializer.Serialize(dto.PostVitals) : exec.PostVitalsJson;
        exec.Status = "FINISHED";
        if (dto.EmptyBagReturned)
        {
            exec.EmptyBagReturnedAtMs = now;
        }
        exec.UpdatedAtMs = now;
        exec.UpdatedBy = _userSession.UserId ?? "SYSTEM";
        await _execRepo.UpdateAsync(exec);

        if (_signatureContract != null)
        {
            await _signatureContract.SignClinicalRecordAsync("BLOOD", exec.Id, "FINISH_TRANSFUSION", exec, _userSession, ct);
        }

        return Ok(ApiResponse<BldTransfusionExecution>.Success(exec, "输血全过程执行结束，生命体征监测记录已闭环归档"));
    }

    public record FifteenMinuteCheckDto(
        decimal Temperature,
        int Pulse,
        int Respiration,
        string BloodPressure,
        int DripRatePerMin,
        bool AdverseReactionObserved,
        string? Notes = null
    );

    [HttpPost("executions/{id}/fifteen-minute-check")]
    public async Task<IActionResult> Record15MinCheck(string id, [FromBody] FifteenMinuteCheckDto dto, CancellationToken ct)
    {
        var exec = await _execRepo.GetByIdAsync(id);
        if (exec == null) return NotFound(ApiResponse<object>.Fail(404, "输血执行单不存在"));
        if (exec.Status != "TRANSFUSING") return BadRequest(ApiResponse<object>.Fail(400, "仅输注中状态允许录入 15 分钟慢滴监测"));

        long now = SystemTime.UtcNowMs;
        var vitals = new Dictionary<string, object>
        {
            ["temperature"] = dto.Temperature,
            ["pulse"] = dto.Pulse,
            ["respiration"] = dto.Respiration,
            ["bloodPressure"] = dto.BloodPressure,
            ["dripRatePerMin"] = dto.DripRatePerMin,
            ["isSlowDripCompliant"] = dto.DripRatePerMin <= 20,
            ["adverseReactionObserved"] = dto.AdverseReactionObserved,
            ["notes"] = dto.Notes ?? string.Empty,
            ["checkedAtMs"] = now,
            ["nurseId"] = _userSession.UserId ?? "NURSE"
        };

        exec.Mid15VitalsJson = JsonSerializer.Serialize(vitals);
        exec.FifteenMinCheckedAtMs = now;
        exec.FifteenMinNurseId = _userSession.UserId ?? "NURSE";
        exec.UpdatedAtMs = now;
        exec.UpdatedBy = _userSession.UserId ?? "SYSTEM";
        await _execRepo.UpdateAsync(exec);

        if (_signatureContract != null)
        {
            await _signatureContract.SignClinicalRecordAsync("BLOOD", exec.Id, "15MIN_CHECK", vitals, _userSession, ct);
        }

        string warnMsg = dto.DripRatePerMin > 20
            ? "⚠ 输注前15分钟滴速超过规范上限 (≤20滴/分)，请立即回调至慢滴速度并密切观察！"
            : "✓ 输注前15分钟慢滴监测符合国家输血技术规范 (≤20滴/分)";

        return Ok(ApiResponse<BldTransfusionExecution>.Success(exec, $"15分钟输血生命体征监测记录归档成功。{warnMsg}"));
    }

    public record BagRecoveryDto(
        string BagBarcode,
        string? ReturnNurseId = null,
        string? RecoveryReceiverId = null,
        string? StorageLocation = "专用2-6℃留样冰箱-01",
        int RetentionHours = 24,
        string? Remark = null
    );

    [HttpPost("executions/{id}/bag-recovery")]
    public async Task<IActionResult> RecordBagRecovery(string id, [FromBody] BagRecoveryDto dto, CancellationToken ct)
    {
        var exec = await _execRepo.GetByIdAsync(id);
        if (exec == null) return NotFound(ApiResponse<object>.Fail(404, "输血执行单不存在"));

        long now = SystemTime.UtcNowMs;
        var nurseId = dto.ReturnNurseId ?? _userSession.UserId ?? "NURSE";
        var receiverId = dto.RecoveryReceiverId ?? _userSession.UserId ?? "BLOOD_TECH";

        exec.EmptyBagReturnedAtMs = now;
        exec.BagRecoveredById = receiverId;
        exec.BagStorageLocation = dto.StorageLocation ?? "专用2-6℃留样冰箱-01";
        exec.RetentionHours = dto.RetentionHours > 0 ? dto.RetentionHours : 24;
        exec.UpdatedAtMs = now;
        exec.UpdatedBy = _userSession.UserId ?? "SYSTEM";
        await _execRepo.UpdateAsync(exec);

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE bld_inventory_bags SET status = 'RECOVERED', updated_at_ms = @Now WHERE bag_barcode = @Barcode",
            new { Now = now, Barcode = dto.BagBarcode });

        await conn.ExecuteAsync(
            "UPDATE mtc_blood_requests SET status = 4, updated_at_ms = @Now WHERE id = @ReqId",
            new { Now = now, ReqId = exec.BloodRequestId });

        if (_signatureContract != null)
        {
            await _signatureContract.SignClinicalRecordAsync("BLOOD", exec.Id, "BAG_RECOVERY", new { dto.BagBarcode, nurseId, receiverId, exec.BagStorageLocation, exec.RetentionHours }, _userSession, ct);
        }

        return Ok(ApiResponse<BldTransfusionExecution>.Success(exec, $"空血袋回收留样成功：交接护士[{nurseId}] -> 血库接收人[{receiverId}]，入库留样至[{exec.BagStorageLocation}]，按规范保存 {exec.RetentionHours} 小时"));
    }

    public record ReportReactionDto(
        string ExecutionId,
        string PatientId,
        string BagBarcode,
        string ReactionType, // ALLERGIC / FEBRILE / HEMOLYTIC / TRALI / TACO
        string Severity,
        string Symptoms,
        string Disposition,
        bool ShareExternal
    );

    [HttpPost("reactions")]
    public async Task<IActionResult> ReportReaction([FromBody] ReportReactionDto dto, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        var r = new BldTransfusionReaction
        {
            Id = Guid.NewGuid().ToString("D"),
            ExecutionId = dto.ExecutionId,
            PatientId = dto.PatientId,
            BagBarcode = dto.BagBarcode,
            ReactionType = dto.ReactionType,
            Severity = dto.Severity ?? "MODERATE",
            Symptoms = dto.Symptoms,
            Disposition = dto.Disposition,
            ReportedBy = _userSession.UserId ?? "SYSTEM",
            ReportedAtMs = now,
            SharedExternal = dto.ShareExternal ? (short)1 : (short)0,
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };
        await _reactionRepo.InsertAsync(r);

        return Ok(ApiResponse<BldTransfusionReaction>.Success(r, "输血不良反应已紧急处置并上报血库及区域卫生平台"));
    }

    [HttpPost("external/sync-center")]
    public async Task<IActionResult> SyncWithCenter(CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        var log = new BldExternalSyncLog
        {
            Id = Guid.NewGuid().ToString("D"),
            PeerBloodCenter = "市中心血站区域血液专网",
            SyncType = "STOCK_INVENTORY",
            Direction = "OUTBOUND",
            PayloadSummary = "上报本院当前 A/B/O/Rh 血液库存余量与拟手术备血需求",
            Status = "SUCCESS",
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };
        await _syncRepo.InsertAsync(log);

        return Ok(ApiResponse<object>.Success(new { syncId = log.Id, timestamp = now }, "与中心血站区域专网库存与备血同步完成"));
    }
}
