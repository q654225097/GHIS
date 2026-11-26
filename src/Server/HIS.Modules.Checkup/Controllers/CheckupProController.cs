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
using HIS.Modules.Checkup.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Checkup.Controllers;

/// <summary>
/// 医院级全功能 PEIS 扩展控制器：
/// 覆盖预约签到与智能导检排队看板、体检单位与团检全流程（分组/批量导入/现场加项/弃检/团检汇总报告）、
/// 医生问诊问卷、总检知识库智能匹配+初审/终审双签+历次对比、重大阳性(A/B类)随访与一键转预住院、
/// 职业病体检（接害因素/结论规范/省平台网报导出）与从业健康证管理。
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "MEDTECH", "CASHIER", "WARD_NURSE")]
[Route("api/v4/checkup-pro")]
public sealed class CheckupProController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<ChkAppointment> _apptRepo;
    private readonly IRepository<ChkCompany> _companyRepo;
    private readonly IRepository<ChkGroupOrder> _groupRepo;
    private readonly IRepository<ChkKnowledgeBase> _kbRepo;
    private readonly IRepository<ChkFollowup> _followupRepo;
    private readonly IRepository<ChkOccupationalRecord> _occRepo;
    private readonly IRepository<ChkOrder> _orderRepo;
    private readonly IRepository<ChkOrderItem> _itemRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public CheckupProController(
        IDbConnectionFactory connFactory,
        IRepository<ChkAppointment> apptRepo,
        IRepository<ChkCompany> companyRepo,
        IRepository<ChkGroupOrder> groupRepo,
        IRepository<ChkKnowledgeBase> kbRepo,
        IRepository<ChkFollowup> followupRepo,
        IRepository<ChkOccupationalRecord> occRepo,
        IRepository<ChkOrder> orderRepo,
        IRepository<ChkOrderItem> itemRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _apptRepo = apptRepo;
        _companyRepo = companyRepo;
        _groupRepo = groupRepo;
        _kbRepo = kbRepo;
        _followupRepo = followupRepo;
        _occRepo = occRepo;
        _orderRepo = orderRepo;
        _itemRepo = itemRepo;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record CreateAppointmentDto(int ApptDateInt, string TimeSlot, string FullName, string Gender, int Age, string? Phone, string ExamCategory, string PackageId, string? CompanyName, string? Remark);
    public sealed record SaveCompanyDto(string? Id, string CompanyCode, string CompanyName, string? CreditCode, string? ContactName, string? ContactPhone, decimal DiscountRate, string SettlementMode, string? Remark);
    public sealed record CreateGroupOrderDto(string CompanyId, string GroupName, string GenderLimit, string PackageId, int PlannedCount, string? Remark);
    public sealed record BatchImportMembersDto(string MembersLines); // 每行: 姓名,性别,年龄,手机号
    public sealed record AddonItemDto(string DeptName, string ItemCode, string ItemName, decimal UnitPrice);
    public sealed record WaiveItemDto(string Reason);
    public sealed record SaveQuestionnaireDto(string PastHistory, string FamilyHistory, string SmokingDrinking, string AllergyHistory, string ChiefComplaint);
    public sealed record SaveKbDto(string? Id, string KbCode, string DiseaseName, string MatchKeywords, string SeverityLevel, string ConclusionTemplate, string AdviceTemplate, string? SuggestDept);
    public sealed record FinalReviewDto(bool Approved, string? ReviewNote);
    public sealed record UpdateFollowupDto(string FollowupStatus, string FollowupNote);
    public sealed record ReferPreAdmissionDto(string TargetDeptName, string PrimaryDiagName, string? PlannedSurgeryName);
    public sealed record SaveOccupationalDto(string OrderId, string OccCategory, string HazardFactors, int WorkYears, string OccConclusion, string? ConclusionDetail, bool IssueHealthCert);

    // ===================== 1. 预约号源签到 & 智能导检排队看板 =====================

    [HttpGet("appointments")]
    public async Task<ApiResponse<IReadOnlyList<ChkAppointment>>> GetAppointments([FromQuery] int? dateInt = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = "SELECT * FROM chk_appointments WHERE 1=1";
        if (dateInt.HasValue && dateInt.Value > 0) sql += " AND appt_date_int = @D";
        sql += " ORDER BY appt_date_int DESC, created_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<ChkAppointment>(sql, new { D = dateInt })).ToList();
        return ApiResponse<IReadOnlyList<ChkAppointment>>.Success(rows);
    }

    [HttpPost("appointments")]
    public async Task<ApiResponse<ChkAppointment>> CreateAppointment([FromBody] CreateAppointmentDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.PackageId))
            return ApiResponse<ChkAppointment>.Fail(400, "预约人姓名与套餐不能为空");

        using var conn = _connFactory.CreateConnection();
        var pkg = await conn.QueryFirstOrDefaultAsync<CfgOrderBundle>("SELECT * FROM cfg_order_bundles WHERE id = @Id", new { Id = req.PackageId.Trim() });
        if (pkg == null) return ApiResponse<ChkAppointment>.Fail(404, "体检套餐不存在");

        long now = SystemTime.UtcNowMs;
        var appt = new ChkAppointment
        {
            Id = Guid.NewGuid().ToString("D"),
            ApptNo = "AP" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            ApptDateInt = req.ApptDateInt > 20000101 ? req.ApptDateInt : int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd")),
            TimeSlot = string.IsNullOrWhiteSpace(req.TimeSlot) ? "08:00-10:00" : req.TimeSlot.Trim(),
            FullName = req.FullName.Trim(),
            Gender = string.IsNullOrWhiteSpace(req.Gender) ? "男" : req.Gender.Trim(),
            Age = Math.Clamp(req.Age, 1, 120),
            PhoneMasked = string.IsNullOrWhiteSpace(req.Phone) ? null : CryptoHelper.MaskPhone(req.Phone.Trim()),
            ExamCategory = string.IsNullOrWhiteSpace(req.ExamCategory) ? "HEALTH" : req.ExamCategory.Trim().ToUpperInvariant(),
            CompanyName = req.CompanyName?.Trim(),
            PackageId = pkg.Id,
            PackageName = pkg.BundleName,
            Status = "BOOKED",
            Remark = req.Remark?.Trim(),
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        await _apptRepo.InsertAsync(appt, ct);
        return ApiResponse<ChkAppointment>.Success(appt, "体检预约创建成功");
    }

    /// <summary>
    /// 预约现场签到：自动转为正式体检单、拆解分科项目并同步推送医技检验/检查待书写队列
    /// </summary>
    [HttpPost("appointments/{id}/checkin")]
    public async Task<ApiResponse<object>> CheckInAppointment(string id, CancellationToken ct)
    {
        var appt = await _apptRepo.GetByIdAsync(id, ct);
        if (appt == null) return ApiResponse<object>.Fail(404, "预约记录不存在");
        if (appt.Status == "CHECKED_IN") return ApiResponse<object>.Fail(409, "该预约已签到");

        using var conn = _connFactory.CreateConnection();
        var pkg = await conn.QueryFirstOrDefaultAsync<CfgOrderBundle>("SELECT * FROM cfg_order_bundles WHERE id = @Id", new { Id = appt.PackageId });
        var pkgItems = (await conn.QueryAsync<CfgOrderBundleItem>("SELECT * FROM cfg_order_bundle_items WHERE bundle_id = @Id ORDER BY sort_order ASC", new { Id = appt.PackageId })).ToList();
        if (pkg == null || pkgItems.Count == 0) return ApiResponse<object>.Fail(400, "套餐或明细为空");

        long now = SystemTime.UtcNowMs;
        string orderId = Guid.NewGuid().ToString("D");
        string checkupNo = "TJ" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);
        string barcode = "PE" + SystemTime.ShanghaiNow.ToString("yyMMdd") + Random.Shared.Next(100000, 999999);

        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var txConn = _connFactory.CreateConnection();
            await _orderRepo.InsertAsync(new ChkOrder
            {
                Id = orderId,
                CheckupNo = checkupNo,
                GuideBarcode = barcode,
                FullName = appt.FullName,
                Gender = appt.Gender,
                Age = appt.Age,
                PhoneMasked = appt.PhoneMasked,
                CheckupType = string.IsNullOrWhiteSpace(appt.CompanyName) ? "PERSONAL" : "GROUP",
                CompanyName = appt.CompanyName,
                PackageId = pkg.Id,
                PackageName = pkg.BundleName,
                TotalAmount = pkg.PackagePrice,
                PaymentStatus = "PAID",
                Status = "IN_PROGRESS",
                TotalItemCount = pkgItems.Count,
                RegisteredAtMs = now,
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            }, token);

            await txConn.ExecuteAsync("UPDATE chk_orders SET exam_category = @Cat WHERE id = @Id", new { Cat = appt.ExamCategory, Id = orderId });

            foreach (var it in pkgItems)
            {
                string itemId = Guid.NewGuid().ToString("D");
                string deptName = string.IsNullOrWhiteSpace(it.ExecDeptName) ? "综合体检科" : it.ExecDeptName;
                await _itemRepo.InsertAsync(new ChkOrderItem
                {
                    Id = itemId,
                    OrderId = orderId,
                    BundleItemId = it.Id,
                    DeptName = deptName,
                    ItemCode = it.ItemCode,
                    ItemName = it.ItemName,
                    UnitPrice = it.UnitPrice,
                    Status = "PENDING",
                    AbnormalFlag = "NORMAL",
                    SortOrder = it.SortOrder,
                    SchemaVersion = 1,
                    CreatedAtMs = now,
                    CreatedBy = ActorId,
                    UpdatedAtMs = now,
                    UpdatedBy = ActorId,
                    VersionNo = 1
                }, token);

                // 同步联动医技报告工作站（检验/放射/超声/心电双签后自动回写体检分科结果）
                if (deptName.Contains("检验") || deptName.Contains("放射") || deptName.Contains("超声") || deptName.Contains("心电"))
                {
                    string dCode = deptName.Contains("检验") ? "LAB" : deptName.Contains("放射") ? "RAD" : deptName.Contains("超声") ? "US" : "ECG";
                    await txConn.ExecuteAsync(@"
                        INSERT INTO mtc_diagnostic_reports
                        (id, report_no, request_no, patient_name, gender, age, order_dept_name, medtech_dept_code, medtech_dept_name, exam_item_code, exam_item_name, result_source, status, content_json, chk_order_item_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                        VALUES
                        (@Id, @RptNo, @ReqNo, @PName, @Gender, @Age, '健康体检中心', @DCode, @DName, @ICode, @IName, 'MANUAL', 'PENDING_WRITE', '{}', @ChkItemId, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new
                        {
                            Id = Guid.NewGuid().ToString("D"),
                            RptNo = "RPT-" + dCode + "-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
                            ReqNo = checkupNo,
                            PName = appt.FullName,
                            appt.Gender,
                            appt.Age,
                            DCode = dCode,
                            DName = deptName,
                            ICode = it.ItemCode,
                            IName = it.ItemName,
                            ChkItemId = itemId,
                            Now = now,
                            Actor = ActorId
                        });
                }
            }

            appt.Status = "CHECKED_IN";
            appt.ConvertedOrderId = orderId;
            appt.UpdatedAtMs = now;
            await _apptRepo.UpdateAsync(appt, token);
        }, ct);

        return ApiResponse<object>.Success(new { OrderId = orderId, CheckupNo = checkupNo, GuideBarcode = barcode }, "签到成功，已自动拆解分科并推送医技工作站");
    }

    /// <summary>
    /// 智能导检与各体检分科实时排队看板（按待检人数从少到多排序推荐）
    /// </summary>
    [HttpGet("triage/queue")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDepartmentQueueBoard()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT dept_name AS DeptName,
                   SUM(CASE WHEN status = 'PENDING' THEN 1 ELSE 0 END) AS WaitingCount,
                   SUM(CASE WHEN status = 'DONE' THEN 1 ELSE 0 END) AS DoneCount,
                   SUM(CASE WHEN status = 'WAIVED' THEN 1 ELSE 0 END) AS WaivedCount
            FROM chk_order_items
            GROUP BY dept_name
            ORDER BY WaitingCount ASC, DeptName ASC")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    // ===================== 2. 体检单位、团检订单、批量导入、加减项与团检汇总报告 =====================

    [HttpGet("companies")]
    public async Task<ApiResponse<IReadOnlyList<ChkCompany>>> GetCompanies()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<ChkCompany>("SELECT * FROM chk_companies WHERE is_active = 1 ORDER BY created_at_ms DESC")).ToList();
        return ApiResponse<IReadOnlyList<ChkCompany>>.Success(rows);
    }

    [HttpPost("companies")]
    public async Task<ApiResponse<ChkCompany>> SaveCompany([FromBody] SaveCompanyDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyName)) return ApiResponse<ChkCompany>.Fail(400, "体检单位名称不能为空");
        long now = SystemTime.UtcNowMs;
        var entity = new ChkCompany
        {
            Id = string.IsNullOrWhiteSpace(req.Id) ? "CMP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() : req.Id.Trim(),
            CompanyCode = string.IsNullOrWhiteSpace(req.CompanyCode) ? "ORG-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmm") : req.CompanyCode.Trim().ToUpperInvariant(),
            CompanyName = req.CompanyName.Trim(),
            CreditCode = req.CreditCode?.Trim(),
            ContactName = req.ContactName?.Trim(),
            ContactPhone = req.ContactPhone?.Trim(),
            DiscountRate = req.DiscountRate <= 0 || req.DiscountRate > 1 ? 0.90m : req.DiscountRate,
            SettlementMode = string.IsNullOrWhiteSpace(req.SettlementMode) ? "POST_SETTLE" : req.SettlementMode.Trim().ToUpperInvariant(),
            IsActive = 1,
            Remark = req.Remark?.Trim(),
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        if (string.IsNullOrWhiteSpace(req.Id)) await _companyRepo.InsertAsync(entity, ct);
        else await _companyRepo.UpdateAsync(entity, ct);
        return ApiResponse<ChkCompany>.Success(entity, "体检单位档案已保存");
    }

    [HttpGet("groups")]
    public async Task<ApiResponse<IReadOnlyList<ChkGroupOrder>>> GetGroupOrders()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<ChkGroupOrder>("SELECT * FROM chk_group_orders ORDER BY created_at_ms DESC LIMIT 100")).ToList();
        return ApiResponse<IReadOnlyList<ChkGroupOrder>>.Success(rows);
    }

    [HttpPost("groups")]
    public async Task<ApiResponse<ChkGroupOrder>> CreateGroupOrder([FromBody] CreateGroupOrderDto req, CancellationToken ct)
    {
        var cmp = await _companyRepo.GetByIdAsync(req.CompanyId, ct);
        if (cmp == null) return ApiResponse<ChkGroupOrder>.Fail(404, "体检单位不存在");
        using var conn = _connFactory.CreateConnection();
        var pkg = await conn.QueryFirstOrDefaultAsync<CfgOrderBundle>("SELECT * FROM cfg_order_bundles WHERE id = @Id", new { Id = req.PackageId });
        if (pkg == null) return ApiResponse<ChkGroupOrder>.Fail(404, "体检套餐不存在");

        long now = SystemTime.UtcNowMs;
        decimal unitPrice = Math.Round(pkg.PackagePrice * cmp.DiscountRate, 2);
        var grp = new ChkGroupOrder
        {
            Id = "GRP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            GroupNo = "TJG" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss"),
            CompanyId = cmp.Id,
            CompanyName = cmp.CompanyName,
            GroupName = string.IsNullOrWhiteSpace(req.GroupName) ? "全员标准组" : req.GroupName.Trim(),
            GenderLimit = string.IsNullOrWhiteSpace(req.GenderLimit) ? "ALL" : req.GenderLimit.Trim().ToUpperInvariant(),
            PackageId = pkg.Id,
            PackageName = pkg.BundleName,
            PlannedCount = Math.Max(1, req.PlannedCount),
            UnitPrice = unitPrice,
            TotalAmount = 0m,
            SettlementStatus = "UNSETTLED",
            Remark = req.Remark?.Trim(),
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        await _groupRepo.InsertAsync(grp, ct);
        return ApiResponse<ChkGroupOrder>.Success(grp, "团检分组订单已创建");
    }

    /// <summary>
    /// 团检人员批量导入（每行格式：姓名,性别,年龄,手机号），批量建单并自动拆解分科项目
    /// </summary>
    [HttpPost("groups/{groupId}/batch-import")]
    public async Task<ApiResponse<object>> BatchImportMembers(string groupId, [FromBody] BatchImportMembersDto req, CancellationToken ct)
    {
        var grp = await _groupRepo.GetByIdAsync(groupId, ct);
        if (grp == null) return ApiResponse<object>.Fail(404, "团检分组不存在");
        if (string.IsNullOrWhiteSpace(req.MembersLines)) return ApiResponse<object>.Fail(400, "请输入要导入的人员清单");

        using var conn = _connFactory.CreateConnection();
        var pkgItems = (await conn.QueryAsync<CfgOrderBundleItem>("SELECT * FROM cfg_order_bundle_items WHERE bundle_id = @Id ORDER BY sort_order ASC", new { Id = grp.PackageId })).ToList();
        var lines = req.MembersLines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int imported = 0;
        long now = SystemTime.UtcNowMs;
        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var txConn = _connFactory.CreateConnection();
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ',', '，', '\t' }, StringSplitOptions.TrimEntries);
                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;
                string name = parts[0];
                string gender = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : "男";
                int age = parts.Length > 2 && int.TryParse(parts[2], out int a) ? Math.Clamp(a, 1, 120) : 32;
                string? phoneMasked = parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? CryptoHelper.MaskPhone(parts[3]) : null;

                string orderId = Guid.NewGuid().ToString("D");
                string checkupNo = "TJ" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(100, 999);
                await _orderRepo.InsertAsync(new ChkOrder
                {
                    Id = orderId,
                    CheckupNo = checkupNo,
                    GuideBarcode = "PE" + SystemTime.ShanghaiNow.ToString("yyMMdd") + Random.Shared.Next(100000, 999999),
                    FullName = name,
                    Gender = gender,
                    Age = age,
                    PhoneMasked = phoneMasked,
                    CheckupType = "GROUP",
                    CompanyName = grp.CompanyName,
                    PackageId = grp.PackageId,
                    PackageName = grp.PackageName,
                    TotalAmount = grp.UnitPrice,
                    PaymentStatus = "PAID",
                    Status = "IN_PROGRESS",
                    TotalItemCount = pkgItems.Count,
                    RegisteredAtMs = now,
                    SchemaVersion = 1,
                    CreatedAtMs = now,
                    CreatedBy = ActorId,
                    UpdatedAtMs = now,
                    UpdatedBy = ActorId,
                    VersionNo = 1
                }, token);

                await txConn.ExecuteAsync("UPDATE chk_orders SET group_order_id = @Gid, company_id = @Cid WHERE id = @Id",
                    new { Gid = grp.Id, Cid = grp.CompanyId, Id = orderId });

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
                        SortOrder = it.SortOrder,
                        SchemaVersion = 1,
                        CreatedAtMs = now,
                        CreatedBy = ActorId,
                        UpdatedAtMs = now,
                        UpdatedBy = ActorId,
                        VersionNo = 1
                    }, token);
                }
                imported++;
            }

            grp.RegisteredCount += imported;
            grp.TotalAmount += imported * grp.UnitPrice;
            grp.UpdatedAtMs = now;
            await _groupRepo.UpdateAsync(grp, token);
        }, ct);

        return ApiResponse<object>.Success(new { ImportedCount = imported, GroupTotalAmount = grp.TotalAmount }, $"已批量导入 {imported} 名团检人员并自动拆解分科项目");
    }

    /// <summary>
    /// 团检单位汇总健康分析报告（参检率、异常率、Top阳性项目检出排行）
    /// </summary>
    [HttpGet("groups/{groupId}/report")]
    public async Task<ApiResponse<object>> GetGroupSummaryReport(string groupId, CancellationToken ct)
    {
        var grp = await _groupRepo.GetByIdAsync(groupId, ct);
        if (grp == null) return ApiResponse<object>.Fail(404, "团检订单不存在");

        using var conn = _connFactory.CreateConnection();
        int regCnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_orders WHERE group_order_id = @Gid", new { Gid = groupId });
        int doneCnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_orders WHERE group_order_id = @Gid AND status = 'COMPLETED'", new { Gid = groupId });
        int abnPeople = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_orders WHERE group_order_id = @Gid AND abnormal_count > 0", new { Gid = groupId });
        var topFindings = (await conn.QueryAsync<dynamic>(@"
            SELECT i.item_name AS ItemName, i.dept_name AS DeptName, COUNT(*) AS AbnormalCount
            FROM chk_order_items i
            INNER JOIN chk_orders o ON o.id = i.order_id
            WHERE o.group_order_id = @Gid AND i.is_abnormal = 1
            GROUP BY i.item_name, i.dept_name
            ORDER BY AbnormalCount DESC LIMIT 15", new { Gid = groupId })).ToList<object>();

        return ApiResponse<object>.Success(new
        {
            Group = grp,
            RegisteredCount = regCnt,
            CompletedCount = doneCnt,
            AbnormalPeopleCount = abnPeople,
            AbnormalRatePct = regCnt > 0 ? Math.Round(abnPeople * 100.0 / regCnt, 1) : 0,
            TopAbnormalItems = topFindings
        });
    }

    // ===================== 3. 现场加项、弃检/延期、医生问诊问卷 =====================

    [HttpPost("orders/{orderId}/add-item")]
    public async Task<ApiResponse<ChkOrderItem>> AddonItem(string orderId, [FromBody] AddonItemDto req, CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order == null) return ApiResponse<ChkOrderItem>.Fail(404, "体检单不存在");
        long now = SystemTime.UtcNowMs;
        var item = new ChkOrderItem
        {
            Id = Guid.NewGuid().ToString("D"),
            OrderId = orderId,
            DeptName = string.IsNullOrWhiteSpace(req.DeptName) ? "检验科" : req.DeptName.Trim(),
            ItemCode = string.IsNullOrWhiteSpace(req.ItemCode) ? "ADD-" + SystemTime.ShanghaiNow.ToString("HHmmss") : req.ItemCode.Trim(),
            ItemName = req.ItemName.Trim(),
            UnitPrice = Math.Max(0, req.UnitPrice),
            Status = "PENDING",
            AbnormalFlag = "NORMAL",
            SortOrder = 99,
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        await _itemRepo.InsertAsync(item, ct);
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync("UPDATE chk_order_items SET is_addon = 1 WHERE id = @Id", new { item.Id });
        order.TotalItemCount++;
        order.TotalAmount += item.UnitPrice;
        order.UpdatedAtMs = now;
        await _orderRepo.UpdateAsync(order, ct);
        return ApiResponse<ChkOrderItem>.Success(item, $"已现场加项 [{item.ItemName}]（+¥{item.UnitPrice:0.00}）");
    }

    [HttpPut("items/{itemId}/waive")]
    public async Task<ApiResponse<object>> WaiveItem(string itemId, [FromBody] WaiveItemDto req, CancellationToken ct)
    {
        var item = await _itemRepo.GetByIdAsync(itemId, ct);
        if (item == null) return ApiResponse<object>.Fail(404, "分科项目不存在");
        long now = SystemTime.UtcNowMs;
        item.Status = "WAIVED";
        item.ResultValue = "弃检/延期：" + (string.IsNullOrWhiteSpace(req.Reason) ? "个人自愿放弃" : req.Reason.Trim());
        item.UpdatedAtMs = now;
        await _itemRepo.UpdateAsync(item, ct);
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync("UPDATE chk_order_items SET waive_reason = @R WHERE id = @Id", new { R = req.Reason, Id = itemId });
        int doneOrWaived = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid AND status IN ('DONE','WAIVED')", new { Oid = item.OrderId });
        int totalCnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chk_order_items WHERE order_id = @Oid", new { Oid = item.OrderId });
        string nextSt = totalCnt > 0 && doneOrWaived >= totalCnt ? "PENDING_SUMMARY" : "IN_PROGRESS";
        await conn.ExecuteAsync("UPDATE chk_orders SET done_item_count = @Done, status = CASE WHEN status = 'COMPLETED' THEN 'COMPLETED' ELSE @St END WHERE id = @Oid",
            new { Done = doneOrWaived, St = nextSt, Oid = item.OrderId });
        return ApiResponse<object>.Success(new { itemId, OrderStatus = nextSt }, "已标记弃检并更新总检进度");
    }

    [HttpPut("orders/{orderId}/questionnaire")]
    public async Task<ApiResponse<object>> SaveQuestionnaire(string orderId, [FromBody] SaveQuestionnaireDto req)
    {
        using var conn = _connFactory.CreateConnection();
        string json = JsonSerializer.Serialize(req);
        await conn.ExecuteAsync("UPDATE chk_orders SET questionnaire_json = @J, updated_at_ms = @Now WHERE id = @Id",
            new { J = json, Now = SystemTime.UtcNowMs, Id = orderId });
        return ApiResponse<object>.Success(new { orderId }, "医生问诊健康问卷已保存");
    }

    // ===================== 4. 总检疾病知识库匹配、双级审签与历次对比 =====================

    [HttpGet("knowledge")]
    public async Task<ApiResponse<IReadOnlyList<ChkKnowledgeBase>>> GetKnowledgeBase()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<ChkKnowledgeBase>("SELECT * FROM chk_knowledge_base WHERE is_active = 1 ORDER BY severity_level ASC, kb_code ASC")).ToList();
        return ApiResponse<IReadOnlyList<ChkKnowledgeBase>>.Success(rows);
    }

    [HttpPost("knowledge")]
    public async Task<ApiResponse<ChkKnowledgeBase>> SaveKnowledge([FromBody] SaveKbDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DiseaseName) || string.IsNullOrWhiteSpace(req.MatchKeywords))
            return ApiResponse<ChkKnowledgeBase>.Fail(400, "疾病名称和匹配关键词不能为空");
        long now = SystemTime.UtcNowMs;
        var kb = new ChkKnowledgeBase
        {
            Id = string.IsNullOrWhiteSpace(req.Id) ? "KB-CHK-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant() : req.Id.Trim(),
            KbCode = string.IsNullOrWhiteSpace(req.KbCode) ? "KB-" + SystemTime.ShanghaiNow.ToString("HHmmss") : req.KbCode.Trim().ToUpperInvariant(),
            DiseaseName = req.DiseaseName.Trim(),
            MatchKeywords = req.MatchKeywords.Trim(),
            SeverityLevel = string.IsNullOrWhiteSpace(req.SeverityLevel) ? "B_IMPORTANT" : req.SeverityLevel.Trim().ToUpperInvariant(),
            ConclusionTemplate = req.ConclusionTemplate.Trim(),
            AdviceTemplate = req.AdviceTemplate.Trim(),
            SuggestDept = req.SuggestDept?.Trim(),
            IsActive = 1,
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId,
            VersionNo = 1
        };
        if (string.IsNullOrWhiteSpace(req.Id)) await _kbRepo.InsertAsync(kb, ct);
        else await _kbRepo.UpdateAsync(kb, ct);
        return ApiResponse<ChkKnowledgeBase>.Success(kb, "总检知识库条目已保存");
    }

    /// <summary>
    /// 基于总检疾病知识库自动匹配异常项目，生成专业总检结论、健康建议，并自动登记 A/B 类重大阳性随访！
    /// </summary>
    [HttpPost("orders/{orderId}/auto-match-kb")]
    public async Task<ApiResponse<object>> AutoMatchKnowledgeBase(string orderId, CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order == null) return ApiResponse<object>.Fail(404, "体检单不存在");

        using var conn = _connFactory.CreateConnection();
        var abnormals = (await conn.QueryAsync<ChkOrderItem>("SELECT * FROM chk_order_items WHERE order_id = @Oid AND is_abnormal = 1", new { Oid = orderId })).ToList();
        var kbList = (await conn.QueryAsync<ChkKnowledgeBase>("SELECT * FROM chk_knowledge_base WHERE is_active = 1")).ToList();

        if (abnormals.Count == 0)
        {
            return ApiResponse<object>.Success(new
            {
                Conclusion = "本次健康体检各分科项目均未见明显异常。",
                Advice = "请保持规律作息、均衡饮食与适度运动，建议每年定期复查。",
                MatchedFollowups = 0
            }, "无异常发现，已生成标准正常结论");
        }

        var conclusions = new List<string>();
        var advices = new List<string>();
        int createdFollowups = 0;
        long now = SystemTime.UtcNowMs;

        foreach (var abn in abnormals)
        {
            string probe = $"{abn.ItemName} {abn.ResultValue}";
            var matched = kbList.FirstOrDefault(k => k.MatchKeywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(kw => probe.Contains(kw, StringComparison.OrdinalIgnoreCase)));

            if (matched != null)
            {
                conclusions.Add($"【{matched.DiseaseName}】{matched.ConclusionTemplate}（{abn.ItemName}：{abn.ResultValue}）");
                advices.Add($"• {matched.DiseaseName}（建议{matched.SuggestDept ?? "专科"}）：{matched.AdviceTemplate}");

                // 自动写入重大阳性 (A/B类) 检后随访台账（防重复）
                int exists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM chk_followups WHERE order_id = @Oid AND finding_summary LIKE @Kw",
                    new { Oid = orderId, Kw = $"%{matched.DiseaseName}%" });
                if (exists == 0 && matched.SeverityLevel is "A_CRITICAL" or "B_IMPORTANT")
                {
                    await _followupRepo.InsertAsync(new ChkFollowup
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        OrderId = orderId,
                        CheckupNo = order.CheckupNo,
                        FullName = order.FullName,
                        PhoneMasked = order.PhoneMasked,
                        PositiveGrade = matched.SeverityLevel,
                        FindingSummary = $"{matched.DiseaseName}：{abn.ItemName}={abn.ResultValue}（{abn.AbnormalFlag}）",
                        SuggestDept = matched.SuggestDept,
                        FollowupStatus = "PENDING",
                        SchemaVersion = 1,
                        CreatedAtMs = now,
                        CreatedBy = ActorId,
                        UpdatedAtMs = now,
                        UpdatedBy = ActorId,
                        VersionNo = 1
                    }, ct);
                    createdFollowups++;
                }
            }
            else
            {
                conclusions.Add($"【{abn.DeptName}异常】{abn.ItemName}：{abn.ResultValue}（参考：{abn.ReferenceRange ?? "-"}，标记：{abn.AbnormalFlag}）");
                advices.Add($"• 建议携本报告至相应专科复查 {abn.ItemName}。");
            }
        }

        return ApiResponse<object>.Success(new
        {
            Conclusion = string.Join("\n", conclusions.Select((c, i) => $"{i + 1}. {c}")),
            Advice = string.Join("\n", advices),
            MatchedFollowups = createdFollowups
        }, $"已智能匹配知识库生成总检草稿，并自动登记 {createdFollowups} 条 A/B 类重大阳性随访");
    }

    /// <summary>
    /// 主检终审双签复核
    /// </summary>
    [HttpPost("orders/{orderId}/final-review")]
    public async Task<ApiResponse<object>> FinalReview(string orderId, [FromBody] FinalReviewDto req)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        if (req.Approved)
        {
            await conn.ExecuteAsync(
                "UPDATE chk_orders SET status = 'COMPLETED', final_reviewer_name = @Rev, final_reviewed_at_ms = @Now, updated_at_ms = @Now WHERE id = @Id",
                new { Rev = ActorName, Now = now, Id = orderId });
            return ApiResponse<object>.Success(new { orderId, Status = "COMPLETED" }, "终审复核通过，个人体检报告已签发");
        }
        else
        {
            await conn.ExecuteAsync(
                "UPDATE chk_orders SET status = 'PENDING_SUMMARY', updated_at_ms = @Now WHERE id = @Id",
                new { Now = now, Id = orderId });
            return ApiResponse<object>.Success(new { orderId, Status = "PENDING_SUMMARY" }, "已退回主检医生修改总检结论");
        }
    }

    /// <summary>
    /// 历次体检指标对比（按同一受检者姓名+性别拉取历史检查同项对比）
    /// </summary>
    [HttpGet("orders/{orderId}/history-compare")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetHistoryCompare(string orderId, CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order == null) return ApiResponse<IReadOnlyList<object>>.Fail(404, "体检单不存在");

        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT o.checkup_no AS CheckupNo, o.registered_at_ms AS RegisteredAtMs,
                   i.item_name AS ItemName, i.result_value AS ResultValue,
                   i.reference_range AS ReferenceRange, i.abnormal_flag AS AbnormalFlag
            FROM chk_order_items i
            INNER JOIN chk_orders o ON o.id = i.order_id
            WHERE o.full_name = @Name AND o.gender = @Gender AND i.status = 'DONE'
            ORDER BY i.item_name ASC, o.registered_at_ms DESC LIMIT 100",
            new { Name = order.FullName, order.Gender })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    // ===================== 5. 重大阳性(A/B类)随访 & 一键转本院预住院 =====================

    [HttpGet("followups")]
    public async Task<ApiResponse<IReadOnlyList<ChkFollowup>>> GetFollowups([FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = "SELECT * FROM chk_followups WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL") sql += " AND followup_status = @St";
        sql += " ORDER BY CASE positive_grade WHEN 'A_CRITICAL' THEN 1 ELSE 2 END, created_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<ChkFollowup>(sql, new { St = status?.Trim() })).ToList();
        return ApiResponse<IReadOnlyList<ChkFollowup>>.Success(rows);
    }

    [HttpPut("followups/{id}")]
    public async Task<ApiResponse<ChkFollowup>> UpdateFollowup(string id, [FromBody] UpdateFollowupDto req, CancellationToken ct)
    {
        var fu = await _followupRepo.GetByIdAsync(id, ct);
        if (fu == null) return ApiResponse<ChkFollowup>.Fail(404, "随访记录不存在");
        long now = SystemTime.UtcNowMs;
        fu.FollowupStatus = string.IsNullOrWhiteSpace(req.FollowupStatus) ? "NOTIFIED" : req.FollowupStatus.Trim().ToUpperInvariant();
        fu.FollowupNote = req.FollowupNote?.Trim();
        fu.HandledBy = ActorName;
        fu.HandledAtMs = now;
        fu.UpdatedAtMs = now;
        await _followupRepo.UpdateAsync(fu, ct);
        return ApiResponse<ChkFollowup>.Success(fu, "检后随访记录已更新");
    }

    /// <summary>
    /// 重大阳性一键转入本院医保「预住院」闭环（直接生成 ipd_pre_admissions 主单实现检治无缝衔接）
    /// </summary>
    [HttpPost("followups/{id}/refer-pre-admission")]
    public async Task<ApiResponse<object>> ReferToPreAdmission(string id, [FromBody] ReferPreAdmissionDto req, CancellationToken ct)
    {
        var fu = await _followupRepo.GetByIdAsync(id, ct);
        if (fu == null) return ApiResponse<object>.Fail(404, "随访记录不存在");
        var order = await _orderRepo.GetByIdAsync(fu.OrderId, ct);

        long now = SystemTime.UtcNowMs;
        string preId = Guid.NewGuid().ToString("D");
        string preNo = "PA" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);

        using var conn = _connFactory.CreateConnection();
        int validDays = await conn.ExecuteScalarAsync<int?>("SELECT default_valid_days FROM ipd_pre_admission_config WHERE id = 'DEFAULT'") ?? 7;

        await conn.ExecuteAsync(@"
            INSERT INTO ipd_pre_admissions
            (id, pre_admit_no, patient_name, gender, age, target_dept_name, primary_diag_name, planned_surgery_name, consent_signed, valid_days, registered_at_ms, valid_until_ms, status, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Name, @Gender, @Age, @Dept, @Diag, @Surg, 1, @Days, @Now, @Until, 'REGISTERED', @Remark, 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = preId,
                No = preNo,
                Name = fu.FullName,
                Gender = order?.Gender ?? "男",
                Age = order?.Age ?? 40,
                Dept = string.IsNullOrWhiteSpace(req.TargetDeptName) ? (fu.SuggestDept ?? "专科病房") : req.TargetDeptName.Trim(),
                Diag = string.IsNullOrWhiteSpace(req.PrimaryDiagName) ? fu.FindingSummary : req.PrimaryDiagName.Trim(),
                Surg = req.PlannedSurgeryName?.Trim(),
                Days = validDays,
                Now = now,
                Until = now + validDays * 86_400_000L,
                Remark = $"由体检重大阳性随访一键转诊（原体检号 {fu.CheckupNo}）",
                Actor = ActorId
            });

        fu.FollowupStatus = "REFERRED";
        fu.ReferredPreAdmissionId = preId;
        fu.FollowupNote = (fu.FollowupNote ?? "") + $" [已转预住院 {preNo}]";
        fu.HandledBy = ActorName;
        fu.HandledAtMs = now;
        fu.UpdatedAtMs = now;
        await _followupRepo.UpdateAsync(fu, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("CHECKUP_REFER_PREADMIT", "ipd_pre_admissions", preId, $"体检重大阳性一键转预住院：{fu.FullName}（体检号 {fu.CheckupNo} → 预住院号 {preNo}）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { PreAdmissionId = preId, PreAdmitNo = preNo }, $"已一键转入本院预住院管理中心（预住院号 {preNo}）");
    }

    // ===================== 6. 职业病体检（接害/禁忌/省平台网报）与从业健康证 =====================

    [HttpGet("occupational")]
    public async Task<ApiResponse<IReadOnlyList<ChkOccupationalRecord>>> GetOccupationalRecords()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<ChkOccupationalRecord>("SELECT * FROM chk_occupational_records ORDER BY updated_at_ms DESC LIMIT 200")).ToList();
        return ApiResponse<IReadOnlyList<ChkOccupationalRecord>>.Success(rows);
    }

    [HttpPost("occupational")]
    public async Task<ApiResponse<ChkOccupationalRecord>> SaveOccupational([FromBody] SaveOccupationalDto req, CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(req.OrderId, ct);
        if (order == null) return ApiResponse<ChkOccupationalRecord>.Fail(404, "关联体检单不存在");

        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        var existing = await conn.QueryFirstOrDefaultAsync<ChkOccupationalRecord>(
            "SELECT * FROM chk_occupational_records WHERE order_id = @Oid", new { Oid = req.OrderId });

        string? certNo = req.IssueHealthCert
            ? (existing?.HealthCertNo ?? "JKZ" + SystemTime.ShanghaiNow.ToString("yyyyMMdd") + Random.Shared.Next(1000, 9999))
            : existing?.HealthCertNo;
        int? certValidUntil = req.IssueHealthCert
            ? int.Parse(SystemTime.ShanghaiNow.AddYears(1).ToString("yyyyMMdd"))
            : existing?.HealthCertValidUntil;

        if (existing == null)
        {
            var rec = new ChkOccupationalRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                OrderId = order.Id,
                CheckupNo = order.CheckupNo,
                FullName = order.FullName,
                CompanyName = order.CompanyName,
                OccCategory = string.IsNullOrWhiteSpace(req.OccCategory) ? "IN_JOB" : req.OccCategory.Trim().ToUpperInvariant(),
                HazardFactors = string.IsNullOrWhiteSpace(req.HazardFactors) ? "噪声" : req.HazardFactors.Trim(),
                WorkYears = Math.Max(0, req.WorkYears),
                OccConclusion = string.IsNullOrWhiteSpace(req.OccConclusion) ? "NO_ABNORMAL" : req.OccConclusion.Trim().ToUpperInvariant(),
                ConclusionDetail = req.ConclusionDetail?.Trim(),
                HealthCertNo = certNo,
                HealthCertValidUntil = certValidUntil,
                ReportUploadStatus = "PENDING",
                SchemaVersion = 1,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId,
                VersionNo = 1
            };
            await _occRepo.InsertAsync(rec, ct);
            return ApiResponse<ChkOccupationalRecord>.Success(rec, req.IssueHealthCert ? $"职业/从业体检已评定并签发健康证 {certNo}" : "职业病体检结论已保存");
        }
        else
        {
            existing.OccCategory = req.OccCategory.Trim().ToUpperInvariant();
            existing.HazardFactors = req.HazardFactors.Trim();
            existing.WorkYears = Math.Max(0, req.WorkYears);
            existing.OccConclusion = req.OccConclusion.Trim().ToUpperInvariant();
            existing.ConclusionDetail = req.ConclusionDetail?.Trim();
            existing.HealthCertNo = certNo;
            existing.HealthCertValidUntil = certValidUntil;
            existing.UpdatedAtMs = now;
            existing.UpdatedBy = ActorId;
            await _occRepo.UpdateAsync(existing, ct);
            return ApiResponse<ChkOccupationalRecord>.Success(existing, "职业/从业体检记录已更新");
        }
    }

    /// <summary>
    /// 生成省级职业病监测平台标准网报 JSON 数据包并标记已网报
    /// </summary>
    [HttpPost("occupational/{id}/export-report")]
    public async Task<ApiResponse<object>> ExportOccupationalNetReport(string id, CancellationToken ct)
    {
        var rec = await _occRepo.GetByIdAsync(id, ct);
        if (rec == null) return ApiResponse<object>.Fail(404, "职业体检记录不存在");

        long now = SystemTime.UtcNowMs;
        var payload = new
        {
            OrgCode = "GHIS-PEIS-001",
            ReportBatch = "OCC-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss"),
            rec.CheckupNo,
            rec.FullName,
            rec.CompanyName,
            rec.OccCategory,
            rec.HazardFactors,
            rec.WorkYears,
            rec.OccConclusion,
            rec.ConclusionDetail,
            ReportedBy = ActorName,
            ReportedAt = SystemTime.ShanghaiNow.ToString("yyyy-MM-dd HH:mm:ss")
        };
        string json = JsonSerializer.Serialize(payload);
        rec.UploadPayloadJson = json;
        rec.ReportUploadStatus = "REPORTED";
        rec.UploadedAtMs = now;
        rec.UpdatedAtMs = now;
        await _occRepo.UpdateAsync(rec, ct);

        return ApiResponse<object>.Success(new { rec.Id, rec.ReportUploadStatus, NetReportJson = json }, "已生成职业病平台网报数据包并标记上报完成");
    }

    public sealed record SettleCheckupCashDto(decimal CashTendered);

    /// <summary>
    /// 闭环：团检分组订单现金统一结算，同步写入全院 fin_payment_orders 与 fin_charge_ledgers 并纳入财务日结与总账凭证
    /// </summary>
    [HttpPost("groups/{groupId}/settle-cash")]
    public async Task<ApiResponse<object>> SettleGroupOrderCash(string groupId, [FromBody] SettleCheckupCashDto req, CancellationToken ct)
    {
        var grp = await _groupRepo.GetByIdAsync(groupId, ct);
        if (grp == null) return ApiResponse<object>.Fail(404, "团检分组订单不存在");
        if (grp.SettlementStatus == "SETTLED") return ApiResponse<object>.Fail(400, "该团检分组订单已完成结算");

        decimal amount = grp.TotalAmount;
        if (req.CashTendered < amount)
            return ApiResponse<object>.Fail(400, $"现金实收金额 (¥{req.CashTendered:F2}) 小于团检应收金额 (¥{amount:F2})");

        long now = SystemTime.UtcNowMs;
        string payId = Guid.NewGuid().ToString("D");
        string payNo = "PEIS-PAY-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        decimal change = Math.Round(req.CashTendered - amount, 2);

        using var conn = _connFactory.CreateConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            INSERT INTO fin_payment_orders
            (id, payment_no, scene, patient_id, visit_or_encounter_id, total_amount, insurance_amount, prepayment_deduct, self_pay_amount, payment_channel, status, paid_at_ms, ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, 'PEIS', @Pat, @Enc, @Amt, 0, 0, @Amt, 'CASH', 1, @Now, @Ext, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = payId, No = payNo, Pat = grp.CompanyId, Enc = grp.Id, Amt = amount, Now = now, Ext = $"{{\"cashTendered\":{req.CashTendered:F2},\"changeAmount\":{change:F2},\"groupName\":\"{grp.GroupName}\"}}", Actor = ActorId }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO fin_charge_ledgers
            (id, patient_id, visit_or_encounter_id, order_dept_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Pat, @Enc, 'PEIS_CENTER', 'PEIS_CENTER', 'PEIS_GROUP_FEE', @Name, @Qty, @Unit, @Amt, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = Guid.NewGuid().ToString("D"), Pat = grp.CompanyId, Enc = grp.Id, Name = $"团检现金结算：{grp.GroupName}", Qty = Math.Max(1, grp.RegisteredCount), Unit = Math.Round(amount / Math.Max(1, grp.RegisteredCount), 2), Amt = amount, Now = now, Actor = ActorId }, tx);

        await conn.ExecuteAsync("UPDATE chk_group_orders SET settlement_status = 'SETTLED', updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id", new { Now = now, Actor = ActorId, Id = grp.Id }, tx);
        tx.Commit();

        return ApiResponse<object>.Success(new { PaymentId = payId, PaymentNo = payNo, Amount = amount, CashTendered = req.CashTendered, ChangeAmount = change }, $"团检现金结算完成 [{payNo}]，找零 ¥{change:F2}，已计入全院财务日结");
    }
}
