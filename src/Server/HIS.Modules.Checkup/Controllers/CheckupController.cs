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
using HIS.Modules.Checkup.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Checkup.Controllers;

/// <summary>
/// 健康体检管理模块（个人/团体体检登记、套餐自动拆解分科项目、分科结果录入、主检总检与报告发布）
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "MEDTECH", "CASHIER", "WARD_NURSE")]
[Route("api/v4/checkup")]
public sealed class CheckupController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<ChkOrder> _orderRepo;
    private readonly IRepository<ChkOrderItem> _itemRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public CheckupController(
        IDbConnectionFactory connFactory,
        IRepository<ChkOrder> orderRepo,
        IRepository<ChkOrderItem> itemRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _orderRepo = orderRepo;
        _itemRepo = itemRepo;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record RegisterCheckupDto(
        string FullName,
        string Gender,
        int Age,
        string? IdCardNo,
        string? Phone,
        string CheckupType, // PERSONAL, GROUP
        string? CompanyName,
        string PackageId,
        bool AutoPay = false);

    public sealed record UpdateItemResultDto(
        string ResultValue,
        string? ReferenceRange,
        string AbnormalFlag, // NORMAL, HIGH, LOW, POSITIVE
        string? ExamDoctorName = null);

    public sealed record CompleteSummaryDto(
        string OverallConclusion,
        string HealthAdvice);

    [HttpGet("summary")]
    public async Task<ApiResponse<object>> GetSummary()
    {
        using var conn = _connFactory.CreateConnection();
        int total = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_orders");
        int inProgress = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_orders WHERE status IN ('REGISTERED','IN_PROGRESS')");
        int pendingSummary = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_orders WHERE status = 'PENDING_SUMMARY'");
        int completed = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_orders WHERE status = 'COMPLETED'");
        int abnormalTotal = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(abnormal_count), 0) FROM chk_orders");
        return ApiResponse<object>.Success(new
        {
            TotalOrders = total,
            InProgressCount = inProgress,
            PendingSummaryCount = pendingSummary,
            CompletedCount = completed,
            TotalAbnormalFindings = abnormalTotal
        });
    }

    [HttpGet("packages")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetCheckupPackages()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT b.id AS Id, b.bundle_code AS BundleCode, b.bundle_name AS BundleName,
                   b.gender_limit AS GenderLimit, b.package_price AS PackagePrice,
                   b.sum_item_price AS SumItemPrice, b.remark AS Remark,
                   (SELECT COUNT(*) FROM cfg_order_bundle_items i WHERE i.bundle_id = b.id) AS ItemCount
            FROM cfg_order_bundles b
            WHERE b.is_active = 1 AND b.bundle_category = 'HEALTH_CHECKUP'
            ORDER BY b.package_price ASC")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpGet("orders")]
    public async Task<ApiResponse<IReadOnlyList<ChkOrder>>> GetOrders([FromQuery] string? keyword = null, [FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = "SELECT * FROM chk_orders WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            sql += " AND status = @Status";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (checkup_no LIKE @Kw OR full_name LIKE @Kw OR company_name LIKE @Kw OR guide_barcode LIKE @Kw)";
        sql += " ORDER BY registered_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<ChkOrder>(sql, new { Status = status?.Trim(), Kw = $"%{keyword?.Trim()}%" })).ToList();
        return ApiResponse<IReadOnlyList<ChkOrder>>.Success(rows);
    }

    [HttpGet("orders/{id}/items")]
    public async Task<ApiResponse<IReadOnlyList<ChkOrderItem>>> GetOrderItems(string id)
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<ChkOrderItem>(
            "SELECT * FROM chk_order_items WHERE order_id = @Id ORDER BY sort_order ASC",
            new { Id = id })).ToList();
        return ApiResponse<IReadOnlyList<ChkOrderItem>>.Success(rows);
    }

    /// <summary>
    /// 体检登记开单：校验信息并自动将所选体检套餐拆解为各分科检查/检验执行明细
    /// </summary>
    [HttpPost("orders/register")]
    public async Task<ApiResponse<object>> Register([FromBody] RegisterCheckupDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FullName))
            return ApiResponse<object>.Fail(400, "体检人姓名不能为空");
        if (string.IsNullOrWhiteSpace(req.PackageId))
            return ApiResponse<object>.Fail(400, "请选择体检套餐");

        string? idCardMasked = null;
        if (!string.IsNullOrWhiteSpace(req.IdCardNo))
        {
            var chk = IdCardValidator.Validate(req.IdCardNo.Trim());
            if (!chk.IsValid)
                return ApiResponse<object>.Fail(400, chk.ErrorMessage ?? "身份证号格式无效");
            idCardMasked = CryptoHelper.MaskIdCard(req.IdCardNo.Trim());
        }
        string? phoneMasked = string.IsNullOrWhiteSpace(req.Phone) ? null : CryptoHelper.MaskPhone(req.Phone.Trim());

        using var conn = _connFactory.CreateConnection();
        var pkg = await conn.QueryFirstOrDefaultAsync<CfgOrderBundle>(
            "SELECT * FROM cfg_order_bundles WHERE id = @Id AND is_active = 1", new { Id = req.PackageId.Trim() });
        if (pkg == null)
            return ApiResponse<object>.Fail(404, "所选体检套餐不存在或已停用");

        var pkgItems = (await conn.QueryAsync<CfgOrderBundleItem>(
            "SELECT * FROM cfg_order_bundle_items WHERE bundle_id = @Id ORDER BY sort_order ASC",
            new { Id = pkg.Id })).ToList();
        if (pkgItems.Count == 0)
            return ApiResponse<object>.Fail(400, "所选体检套餐尚未配置检查项目明细");

        long now = SystemTime.UtcNowMs;
        string orderId = Guid.NewGuid().ToString("D");
        string checkupNo = "TJ" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);
        string barcode = "PE" + SystemTime.ShanghaiNow.ToString("yyMMdd") + Random.Shared.Next(100000, 999999);

        var order = new ChkOrder
        {
            Id = orderId,
            CheckupNo = checkupNo,
            GuideBarcode = barcode,
            FullName = req.FullName.Trim(),
            Gender = string.IsNullOrWhiteSpace(req.Gender) ? "男" : req.Gender.Trim(),
            Age = Math.Clamp(req.Age, 1, 120),
            IdCardMasked = idCardMasked,
            PhoneMasked = phoneMasked,
            CheckupType = string.IsNullOrWhiteSpace(req.CheckupType) ? "PERSONAL" : req.CheckupType.Trim().ToUpperInvariant(),
            CompanyName = req.CompanyName?.Trim(),
            PackageId = pkg.Id,
            PackageName = pkg.BundleName,
            TotalAmount = pkg.PackagePrice,
            PaymentStatus = req.AutoPay ? "PAID" : "UNPAID",
            Status = req.AutoPay ? "IN_PROGRESS" : "REGISTERED",
            TotalItemCount = pkgItems.Count,
            DoneItemCount = 0,
            AbnormalCount = 0,
            RegisteredAtMs = now,
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };

        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            await _orderRepo.InsertAsync(order, token);
            foreach (var it in pkgItems)
            {
                await _itemRepo.InsertAsync(new ChkOrderItem
                {
                    Id = Guid.NewGuid().ToString("D"),
                    OrderId = orderId,
                    BundleItemId = it.Id,
                    DeptName = string.IsNullOrWhiteSpace(it.ExecDeptName) ? "综合体检科" : it.ExecDeptName,
                    ItemCode = it.ItemCode,
                    ItemName = it.ItemName,
                    UnitPrice = it.UnitPrice,
                    Status = "PENDING",
                    AbnormalFlag = "NORMAL",
                    IsAbnormal = 0,
                    SortOrder = it.SortOrder,
                    SchemaVersion = 1,
                    CreatedAtMs = now,
                    CreatedBy = ActorId,
                    UpdatedAtMs = now,
                    UpdatedBy = ActorId,
                    VersionNo = 1
                }, token);
            }
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("CHECKUP_REGISTER", "chk_orders", orderId, $"体检登记：{order.FullName}（单号 {checkupNo}，套餐 {pkg.BundleName}，共拆解 {pkgItems.Count} 项）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Order = order, ItemCount = pkgItems.Count }, $"体检登记成功，已自动拆解 {pkgItems.Count} 项分科项目");
    }

    [HttpPost("orders/{id}/pay")]
    public async Task<ApiResponse<object>> ConfirmPayment(string id, CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(id, ct);
        if (order == null) return ApiResponse<object>.Fail(404, "体检单不存在");
        order.PaymentStatus = "PAID";
        if (order.Status == "REGISTERED") order.Status = "IN_PROGRESS";
        order.UpdatedAtMs = SystemTime.UtcNowMs;
        order.UpdatedBy = ActorId;
        await _orderRepo.UpdateAsync(order, ct);
        return ApiResponse<object>.Success(order, "体检缴费已确认，可开始分科检查");
    }

    [HttpPut("items/{itemId}/result")]
    public async Task<ApiResponse<object>> UpdateItemResult(string itemId, [FromBody] UpdateItemResultDto req, CancellationToken ct)
    {
        var item = await _itemRepo.GetByIdAsync(itemId, ct);
        if (item == null) return ApiResponse<object>.Fail(404, "体检分科项目不存在");

        long now = SystemTime.UtcNowMs;
        string flag = string.IsNullOrWhiteSpace(req.AbnormalFlag) ? "NORMAL" : req.AbnormalFlag.Trim().ToUpperInvariant();
        item.ResultValue = req.ResultValue?.Trim() ?? string.Empty;
        item.ReferenceRange = req.ReferenceRange?.Trim();
        item.AbnormalFlag = flag;
        item.IsAbnormal = (short)(flag == "NORMAL" ? 0 : 1);
        item.ExamDoctorName = string.IsNullOrWhiteSpace(req.ExamDoctorName) ? ActorName : req.ExamDoctorName.Trim();
        item.Status = "DONE";
        item.CheckedAtMs = now;
        item.UpdatedAtMs = now;
        item.UpdatedBy = ActorId;
        await _itemRepo.UpdateAsync(item, ct);

        using var conn = _connFactory.CreateConnection();
        int doneCnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid AND status = 'DONE'", new { Oid = item.OrderId });
        int totalCnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid", new { Oid = item.OrderId });
        int abnCnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid AND is_abnormal = 1", new { Oid = item.OrderId });

        string nextStatus = (totalCnt > 0 && doneCnt >= totalCnt) ? "PENDING_SUMMARY" : "IN_PROGRESS";
        await conn.ExecuteAsync(@"
            UPDATE chk_orders
            SET done_item_count = @Done, abnormal_count = @Abn,
                status = CASE WHEN status = 'COMPLETED' THEN 'COMPLETED' ELSE @Status END,
                updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Oid",
            new { Done = doneCnt, Abn = abnCnt, Status = nextStatus, Now = now, Actor = ActorId, Oid = item.OrderId });

        return ApiResponse<object>.Success(new { Item = item, DoneCount = doneCnt, TotalCount = totalCnt, AbnormalCount = abnCnt, OrderStatus = nextStatus }, "分科检查结果已保存");
    }

    [HttpPost("orders/{id}/complete-summary")]
    public async Task<ApiResponse<ChkOrder>> CompleteSummary(string id, [FromBody] CompleteSummaryDto req, CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(id, ct);
        if (order == null) return ApiResponse<ChkOrder>.Fail(404, "体检单不存在");
        if (string.IsNullOrWhiteSpace(req.OverallConclusion))
            return ApiResponse<ChkOrder>.Fail(400, "主检总检结论不能为空");

        long now = SystemTime.UtcNowMs;
        order.OverallConclusion = req.OverallConclusion.Trim();
        order.HealthAdvice = req.HealthAdvice?.Trim() ?? string.Empty;
        order.SummaryDoctorId = ActorId;
        order.SummaryDoctorName = ActorName;
        order.Status = "COMPLETED";
        order.CompletedAtMs = now;
        order.UpdatedAtMs = now;
        order.UpdatedBy = ActorId;
        await _orderRepo.UpdateAsync(order, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("CHECKUP_SUMMARY_COMPLETE", "chk_orders", order.Id, $"主检总检发布报告：{order.FullName}（{order.CheckupNo}，异常项 {order.AbnormalCount} 项）", ActorId, ActorName, null, ct);

        return ApiResponse<ChkOrder>.Success(order, "主检总检报告已审核发布");
    }
}
