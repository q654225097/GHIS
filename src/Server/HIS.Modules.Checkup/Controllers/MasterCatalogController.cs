using HIS.BuildingBlocks.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Checkup.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Checkup.Controllers;

/// <summary>
/// 全院基础字典维护（药品基本字典、耗材基本字典、收费项目目录）与现代医疗组合套餐引擎
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "WAREHOUSE_KEEPER", "PHARMACIST", "CASHIER")]
[Route("api/v4/catalog")]
public sealed class MasterCatalogController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<CfgOrderBundle> _bundleRepo;
    private readonly IRepository<CfgOrderBundleItem> _bundleItemRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public MasterCatalogController(
        IDbConnectionFactory connFactory,
        IRepository<CfgOrderBundle> bundleRepo,
        IRepository<CfgOrderBundleItem> bundleItemRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _bundleRepo = bundleRepo;
        _bundleItemRepo = bundleItemRepo;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record SaveDrugDto(
        string? Id,
        string DrugCode,
        string DrugName,
        string Spec,
        string UnitName,
        decimal Price,
        decimal CostPrice,
        int AntibioticLevel = 0,
        bool IsNarcotic = false,
        bool IsSkinTestRequired = false,
        bool IsActive = true);

    public sealed record SaveConsumableDto(
        string? Id,
        string Code,
        string Name,
        string Spec,
        string Unit,
        string Category,
        decimal Price,
        bool IsSterile = false,
        bool IsHighValue = false,
        bool IsActive = true);

    public sealed record SavePriceItemDto(
        string? Id,
        string ItemCode,
        string? NationalInsCode,
        string ItemName,
        string? PinyinCode,
        string FeeCategory,
        string? Spec,
        string UnitName,
        decimal UnitPrice,
        string ReimburseType = "CLASS_A",
        bool IsActive = true);

    public sealed record BundleItemInput(
        string ItemType,
        string? TargetItemId,
        string ItemCode,
        string ItemName,
        string? Spec,
        string UnitName,
        decimal Quantity,
        decimal UnitPrice,
        string? ExecDeptName,
        int SortOrder = 10,
        bool IsRequired = true);

    public sealed record SaveBundleDto(
        string? Id,
        string BundleCode,
        string BundleName,
        string? PinyinCode,
        string BundleCategory,
        string ApplicableScene,
        string GenderLimit,
        string? DeptName,
        string? SpecimenType,
        decimal? PackagePrice,
        bool IsAllowSplit,
        bool IsActive,
        string? Remark,
        List<BundleItemInput> Items);

    // ===================== 1. 药品基本字典维护 =====================

    [HttpGet("drugs")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDrugs([FromQuery] string? keyword = null, [FromQuery] bool includeInactive = false)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT id AS Id, drug_code AS DrugCode, drug_name AS DrugName, spec AS Spec,
                           unit_name AS UnitName, price AS Price, cost_price AS CostPrice,
                           antibiotic_level AS AntibioticLevel, is_narcotic AS IsNarcotic,
                           is_skin_test_required AS IsSkinTestRequired, is_active AS IsActive
                    FROM inv_drugs WHERE 1=1";
        if (!includeInactive) sql += " AND is_active = 1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (drug_code LIKE @Kw OR drug_name LIKE @Kw OR spec LIKE @Kw)";
        sql += " ORDER BY drug_code ASC";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("drugs")]
    public async Task<ApiResponse<object>> SaveDrug([FromBody] SaveDrugDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DrugCode) || string.IsNullOrWhiteSpace(req.DrugName))
            return ApiResponse<object>.Fail(400, "药品编码和药品名称不能为空");
        if (req.Price < 0 || req.CostPrice < 0)
            return ApiResponse<object>.Fail(400, "零售价与进价不能为负数");

        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string code = req.DrugCode.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(req.Id))
        {
            if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inv_drugs WHERE drug_code = @Code", new { Code = code }) > 0)
                return ApiResponse<object>.Fail(409, $"药品编码 [{code}] 已存在");

            string id = "DRUG-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @Code, @Name, @Spec, @Unit, @Price, @Cost, @Abx, @Narc, @Skin, @Active, 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = id, Code = code, Name = req.DrugName.Trim(), Spec = req.Spec?.Trim() ?? "",
                    Unit = string.IsNullOrWhiteSpace(req.UnitName) ? "盒" : req.UnitName.Trim(),
                    req.Price, Cost = req.CostPrice, Abx = Math.Clamp(req.AntibioticLevel, 0, 3),
                    Narc = req.IsNarcotic ? 1 : 0, Skin = req.IsSkinTestRequired ? 1 : 0,
                    Active = req.IsActive ? 1 : 0, Now = now, Actor = ActorId
                });
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("CATALOG_DRUG_CREATE", "inv_drugs", id, $"新增药品字典：{req.DrugName}（{code}，¥{req.Price:0.00}）", ActorId, ActorName, null, ct);
            return ApiResponse<object>.Success(new { Id = id, DrugCode = code }, "药品字典已新增");
        }
        else
        {
            await conn.ExecuteAsync(@"
                UPDATE inv_drugs
                SET drug_code = @Code, drug_name = @Name, spec = @Spec, unit_name = @Unit,
                    price = @Price, cost_price = @Cost, antibiotic_level = @Abx,
                    is_narcotic = @Narc, is_skin_test_required = @Skin, is_active = @Active,
                    updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new
                {
                    Id = req.Id.Trim(), Code = code, Name = req.DrugName.Trim(), Spec = req.Spec?.Trim() ?? "",
                    Unit = string.IsNullOrWhiteSpace(req.UnitName) ? "盒" : req.UnitName.Trim(),
                    req.Price, Cost = req.CostPrice, Abx = Math.Clamp(req.AntibioticLevel, 0, 3),
                    Narc = req.IsNarcotic ? 1 : 0, Skin = req.IsSkinTestRequired ? 1 : 0,
                    Active = req.IsActive ? 1 : 0, Now = now, Actor = ActorId
                });
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("CATALOG_DRUG_UPDATE", "inv_drugs", req.Id, $"修改药品字典：{req.DrugName}（{code}，¥{req.Price:0.00}）", ActorId, ActorName, null, ct);
            return ApiResponse<object>.Success(new { Id = req.Id, DrugCode = code }, "药品字典已更新");
        }
    }

    [HttpDelete("drugs/{id}")]
    public async Task<ApiResponse<object>> DisableDrug(string id)
    {
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync("UPDATE inv_drugs SET is_active = 0, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Id = id, Now = SystemTime.UtcNowMs, Actor = ActorId });
        return ApiResponse<object>.Success(new { id }, "药品已停用");
    }

    // ===================== 2. 耗材基本字典维护 =====================

    [HttpGet("consumables")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetConsumables([FromQuery] string? keyword = null, [FromQuery] bool includeInactive = false)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT id AS Id, code AS Code, name AS Name, spec AS Spec, unit AS Unit,
                           category AS Category, is_sterile AS IsSterile, is_high_value AS IsHighValue,
                           price AS Price, is_active AS IsActive
                    FROM csm_consumables WHERE 1=1";
        if (!includeInactive) sql += " AND is_active = 1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (code LIKE @Kw OR name LIKE @Kw OR spec LIKE @Kw OR category LIKE @Kw)";
        sql += " ORDER BY code ASC";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("consumables")]
    public async Task<ApiResponse<object>> SaveConsumable([FromBody] SaveConsumableDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return ApiResponse<object>.Fail(400, "耗材编码与名称不能为空");
        if (req.Price < 0)
            return ApiResponse<object>.Fail(400, "耗材单价不能为负数");

        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string code = req.Code.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(req.Id))
        {
            if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM csm_consumables WHERE code = @Code", new { Code = code }) > 0)
                return ApiResponse<object>.Fail(409, $"耗材编码 [{code}] 已存在");

            string id = "CSM-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            await conn.ExecuteAsync(@"
                INSERT INTO csm_consumables (id, code, name, spec, unit, category, is_sterile, is_high_value, price, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @Code, @Name, @Spec, @Unit, @Cat, @Sterile, @HighVal, @Price, @Active, 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = id, Code = code, Name = req.Name.Trim(), Spec = req.Spec?.Trim() ?? "",
                    Unit = string.IsNullOrWhiteSpace(req.Unit) ? "支" : req.Unit.Trim(),
                    Cat = string.IsNullOrWhiteSpace(req.Category) ? "普通医用耗材" : req.Category.Trim(),
                    Sterile = req.IsSterile ? 1 : 0, HighVal = req.IsHighValue ? 1 : 0,
                    req.Price, Active = req.IsActive ? 1 : 0, Now = now, Actor = ActorId
                });
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("CATALOG_CONSUMABLE_CREATE", "csm_consumables", id, $"新增耗材字典：{req.Name}（{code}，¥{req.Price:0.00}）", ActorId, ActorName, null, ct);
            return ApiResponse<object>.Success(new { Id = id, Code = code }, "耗材字典已新增");
        }
        else
        {
            await conn.ExecuteAsync(@"
                UPDATE csm_consumables
                SET code = @Code, name = @Name, spec = @Spec, unit = @Unit, category = @Cat,
                    is_sterile = @Sterile, is_high_value = @HighVal, price = @Price, is_active = @Active,
                    updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new
                {
                    Id = req.Id.Trim(), Code = code, Name = req.Name.Trim(), Spec = req.Spec?.Trim() ?? "",
                    Unit = string.IsNullOrWhiteSpace(req.Unit) ? "支" : req.Unit.Trim(),
                    Cat = string.IsNullOrWhiteSpace(req.Category) ? "普通医用耗材" : req.Category.Trim(),
                    Sterile = req.IsSterile ? 1 : 0, HighVal = req.IsHighValue ? 1 : 0,
                    req.Price, Active = req.IsActive ? 1 : 0, Now = now, Actor = ActorId
                });
            if (_auditTrail != null)
                await _auditTrail.RecordAuditAsync("CATALOG_CONSUMABLE_UPDATE", "csm_consumables", req.Id, $"修改耗材字典：{req.Name}（{code}，¥{req.Price:0.00}）", ActorId, ActorName, null, ct);
            return ApiResponse<object>.Success(new { Id = req.Id, Code = code }, "耗材字典已更新");
        }
    }

    [HttpDelete("consumables/{id}")]
    public async Task<ApiResponse<object>> DisableConsumable(string id)
    {
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync("UPDATE csm_consumables SET is_active = 0, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Id = id, Now = SystemTime.UtcNowMs, Actor = ActorId });
        return ApiResponse<object>.Success(new { id }, "耗材已停用");
    }

    // ===================== 3. 医疗服务收费项目基础目录 =====================

    [HttpGet("price-items")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetPriceItems([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT id AS Id, item_code AS ItemCode, national_ins_code AS NationalInsCode,
                           item_name AS ItemName, pinyin_code AS PinyinCode, fee_category AS FeeCategory,
                           spec AS Spec, unit_name AS UnitName, unit_price AS UnitPrice,
                           reimburse_type AS ReimburseType, is_active AS IsActive
                    FROM cfg_price_items WHERE is_active = 1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (item_code LIKE @Kw OR item_name LIKE @Kw OR pinyin_code LIKE @Kw)";
        sql += " ORDER BY fee_category ASC, item_code ASC";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("price-items")]
    public async Task<ApiResponse<object>> SavePriceItem([FromBody] SavePriceItemDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ItemCode) || string.IsNullOrWhiteSpace(req.ItemName))
            return ApiResponse<object>.Fail(400, "收费项目编码和名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string code = req.ItemCode.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(req.Id))
        {
            string id = "PI-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_price_items (id, item_code, national_ins_code, item_name, pinyin_code, fee_category, spec, unit_name, unit_price, reimburse_type, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @Code, @Ins, @Name, @Py, @Cat, @Spec, @Unit, @Price, @Reimb, @Active, 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = id, Code = code, Ins = req.NationalInsCode, Name = req.ItemName.Trim(), Py = req.PinyinCode,
                    Cat = string.IsNullOrWhiteSpace(req.FeeCategory) ? "LAB" : req.FeeCategory.Trim().ToUpperInvariant(),
                    req.Spec, Unit = string.IsNullOrWhiteSpace(req.UnitName) ? "次" : req.UnitName.Trim(),
                    Price = req.UnitPrice, Reimb = req.ReimburseType, Active = req.IsActive ? 1 : 0, Now = now, Actor = ActorId
                });
            return ApiResponse<object>.Success(new { Id = id, ItemCode = code }, "收费项目已新增");
        }
        else
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_price_items SET item_code = @Code, national_ins_code = @Ins, item_name = @Name,
                    pinyin_code = @Py, fee_category = @Cat, spec = @Spec, unit_name = @Unit,
                    unit_price = @Price, reimburse_type = @Reimb, is_active = @Active,
                    updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new
                {
                    Id = req.Id.Trim(), Code = code, Ins = req.NationalInsCode, Name = req.ItemName.Trim(), Py = req.PinyinCode,
                    Cat = string.IsNullOrWhiteSpace(req.FeeCategory) ? "LAB" : req.FeeCategory.Trim().ToUpperInvariant(),
                    req.Spec, Unit = string.IsNullOrWhiteSpace(req.UnitName) ? "次" : req.UnitName.Trim(),
                    Price = req.UnitPrice, Reimb = req.ReimburseType, Active = req.IsActive ? 1 : 0, Now = now, Actor = ActorId
                });
            return ApiResponse<object>.Success(new { Id = req.Id, ItemCode = code }, "收费项目已更新");
        }
    }

    // ===================== 4. 现代医疗组合套餐（医嘱套/检验组合/体检套餐） =====================

    [HttpGet("bundles")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetBundles([FromQuery] string? category = null, [FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT b.*, (SELECT COUNT(*) FROM cfg_order_bundle_items i WHERE i.bundle_id = b.id) AS ItemCount
                    FROM cfg_order_bundles b WHERE b.is_active = 1";
        if (!string.IsNullOrWhiteSpace(category) && category != "ALL")
            sql += " AND b.bundle_category = @Cat";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (b.bundle_code LIKE @Kw OR b.bundle_name LIKE @Kw OR b.pinyin_code LIKE @Kw)";
        sql += " ORDER BY b.bundle_category ASC, b.bundle_code ASC";

        var rows = (await conn.QueryAsync<dynamic>(sql, new { Cat = category?.Trim(), Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpGet("bundles/{id}")]
    public async Task<ApiResponse<object>> GetBundleDetail(string id, CancellationToken ct)
    {
        var bundle = await _bundleRepo.GetByIdAsync(id, ct);
        if (bundle == null) return ApiResponse<object>.Fail(404, "组合套餐不存在");
        using var conn = _connFactory.CreateConnection();
        var items = (await conn.QueryAsync<CfgOrderBundleItem>(
            "SELECT * FROM cfg_order_bundle_items WHERE bundle_id = @Id ORDER BY sort_order ASC",
            new { Id = id })).ToList();
        return ApiResponse<object>.Success(new { Bundle = bundle, Items = items });
    }

    [HttpPost("bundles")]
    public async Task<ApiResponse<object>> SaveBundle([FromBody] SaveBundleDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.BundleName))
            return ApiResponse<object>.Fail(400, "套餐名称不能为空");
        if (req.Items == null || req.Items.Count == 0)
            return ApiResponse<object>.Fail(400, "组合套餐至少需要包含 1 个底层收费明细项目");

        long now = SystemTime.UtcNowMs;
        decimal sumPrice = req.Items.Sum(i => Math.Max(0, i.Quantity) * Math.Max(0, i.UnitPrice));
        decimal pkgPrice = req.PackagePrice.HasValue && req.PackagePrice.Value > 0 ? req.PackagePrice.Value : sumPrice;
        string code = string.IsNullOrWhiteSpace(req.BundleCode)
            ? "BND-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmm")
            : req.BundleCode.Trim().ToUpperInvariant();

        string bundleId = string.IsNullOrWhiteSpace(req.Id) ? "BND-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() : req.Id.Trim();
        var existing = string.IsNullOrWhiteSpace(req.Id) ? null : await _bundleRepo.GetByIdAsync(bundleId, ct);

        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            if (existing == null)
            {
                await _bundleRepo.InsertAsync(new CfgOrderBundle
                {
                    Id = bundleId,
                    BundleCode = code,
                    BundleName = req.BundleName.Trim(),
                    PinyinCode = req.PinyinCode?.Trim().ToUpperInvariant(),
                    BundleCategory = string.IsNullOrWhiteSpace(req.BundleCategory) ? "LAB_PANEL" : req.BundleCategory.Trim().ToUpperInvariant(),
                    ApplicableScene = string.IsNullOrWhiteSpace(req.ApplicableScene) ? "ALL" : req.ApplicableScene.Trim().ToUpperInvariant(),
                    GenderLimit = string.IsNullOrWhiteSpace(req.GenderLimit) ? "ALL" : req.GenderLimit.Trim().ToUpperInvariant(),
                    DeptName = req.DeptName?.Trim(),
                    SpecimenType = req.SpecimenType?.Trim(),
                    PackagePrice = pkgPrice,
                    SumItemPrice = sumPrice,
                    IsAllowSplit = (short)(req.IsAllowSplit ? 1 : 0),
                    IsActive = (short)(req.IsActive ? 1 : 0),
                    Remark = req.Remark?.Trim(),
                    SchemaVersion = 1,
                    CreatedAtMs = now,
                    CreatedBy = ActorId,
                    UpdatedAtMs = now,
                    UpdatedBy = ActorId,
                    VersionNo = 1
                }, token);
            }
            else
            {
                existing.BundleCode = code;
                existing.BundleName = req.BundleName.Trim();
                existing.PinyinCode = req.PinyinCode?.Trim().ToUpperInvariant();
                existing.BundleCategory = string.IsNullOrWhiteSpace(req.BundleCategory) ? existing.BundleCategory : req.BundleCategory.Trim().ToUpperInvariant();
                existing.ApplicableScene = string.IsNullOrWhiteSpace(req.ApplicableScene) ? existing.ApplicableScene : req.ApplicableScene.Trim().ToUpperInvariant();
                existing.GenderLimit = string.IsNullOrWhiteSpace(req.GenderLimit) ? existing.GenderLimit : req.GenderLimit.Trim().ToUpperInvariant();
                existing.DeptName = req.DeptName?.Trim();
                existing.SpecimenType = req.SpecimenType?.Trim();
                existing.PackagePrice = pkgPrice;
                existing.SumItemPrice = sumPrice;
                existing.IsAllowSplit = (short)(req.IsAllowSplit ? 1 : 0);
                existing.IsActive = (short)(req.IsActive ? 1 : 0);
                existing.Remark = req.Remark?.Trim();
                existing.UpdatedAtMs = now;
                existing.UpdatedBy = ActorId;
                await _bundleRepo.UpdateAsync(existing, token);
            }

            using var conn = _connFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM cfg_order_bundle_items WHERE bundle_id = @Bid", new { Bid = bundleId });

            int seq = 10;
            foreach (var it in req.Items)
            {
                decimal qty = it.Quantity <= 0 ? 1 : it.Quantity;
                await _bundleItemRepo.InsertAsync(new CfgOrderBundleItem
                {
                    Id = Guid.NewGuid().ToString("D"),
                    BundleId = bundleId,
                    ItemType = string.IsNullOrWhiteSpace(it.ItemType) ? "PRICE_ITEM" : it.ItemType.Trim().ToUpperInvariant(),
                    TargetItemId = it.TargetItemId,
                    ItemCode = it.ItemCode.Trim(),
                    ItemName = it.ItemName.Trim(),
                    Spec = it.Spec,
                    UnitName = string.IsNullOrWhiteSpace(it.UnitName) ? "次" : it.UnitName.Trim(),
                    Quantity = qty,
                    UnitPrice = it.UnitPrice,
                    SubtotalAmount = qty * it.UnitPrice,
                    ExecDeptName = it.ExecDeptName,
                    SortOrder = it.SortOrder > 0 ? it.SortOrder : seq,
                    IsRequired = (short)(it.IsRequired ? 1 : 0),
                    SchemaVersion = 1,
                    CreatedAtMs = now,
                    CreatedBy = ActorId,
                    UpdatedAtMs = now,
                    UpdatedBy = ActorId,
                    VersionNo = 1
                }, token);
                seq += 10;
            }
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("CATALOG_BUNDLE_SAVE", "cfg_order_bundles", bundleId, $"保存组合套餐：{req.BundleName}（含 {req.Items.Count} 项明细，套餐价 ¥{pkgPrice:0.00}）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = bundleId, BundleCode = code, SumItemPrice = sumPrice, PackagePrice = pkgPrice }, "组合套餐及明细已保存");
    }

    [HttpDelete("bundles/{id}")]
    public async Task<ApiResponse<object>> DisableBundle(string id)
    {
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync("UPDATE cfg_order_bundles SET is_active = 0, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Id = id, Now = SystemTime.UtcNowMs, Actor = ActorId });
        return ApiResponse<object>.Success(new { id }, "组合套餐已停用");
    }

    /// <summary>
    /// 供前台医生开立组合项目（如「肾功能全套」）时，实时拆解为底层具体收费明细列表与优惠折算价
    /// </summary>
    [HttpPost("bundles/{id}/expand")]
    public async Task<ApiResponse<object>> ExpandBundle(string id, CancellationToken ct)
    {
        var bundle = await _bundleRepo.GetByIdAsync(id, ct);
        if (bundle == null || bundle.IsActive != 1)
            return ApiResponse<object>.Fail(404, "组合套餐不存在或已停用");

        using var conn = _connFactory.CreateConnection();
        var items = (await conn.QueryAsync<CfgOrderBundleItem>(
            "SELECT * FROM cfg_order_bundle_items WHERE bundle_id = @Id ORDER BY sort_order ASC",
            new { Id = id })).ToList();

        return ApiResponse<object>.Success(new
        {
            bundle.Id,
            bundle.BundleCode,
            bundle.BundleName,
            bundle.BundleCategory,
            bundle.SpecimenType,
            bundle.PackagePrice,
            bundle.SumItemPrice,
            DiscountAmount = Math.Max(0, bundle.SumItemPrice - bundle.PackagePrice),
            ExpandedItems = items
        }, $"已拆解 [{bundle.BundleName}] 为 {items.Count} 项具体收费明细");
    }
}
