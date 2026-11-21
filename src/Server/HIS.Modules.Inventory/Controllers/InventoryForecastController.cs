using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inventory.Controllers;

/// <summary>
/// 药库/药房智能补货预测与缺货天数预警引擎（对标 Open Hospital 二级药房缺货天数 + MedCore 30/60/90 天采购预测）
/// 基于近 90 天真实 inv_stock_ledgers 出库消耗流水与当前 inv_drug_batches 有效未过期库存实时计算
/// </summary>
[ApiController]
[Route("api/v4/inventory/forecast")]
public sealed class InventoryForecastController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public InventoryForecastController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private sealed class StockRow
    {
        public string DrugId { get; set; } = string.Empty;
        public int Qty { get; set; }
    }

    private sealed class UsageRow
    {
        public string DrugId { get; set; } = string.Empty;
        public int Out30 { get; set; }
        public int Out90 { get; set; }
    }

    public sealed record DrugForecastRow(
        string DrugId,
        string DrugCode,
        string DrugName,
        string Spec,
        string UnitName,
        int CurrentValidStock,
        int OutboundLast30Days,
        int OutboundLast90Days,
        decimal WeightedDailyUsage,
        int DaysOutOfStock,
        int SuggestedOrder30d,
        int SuggestedOrder60d,
        int SuggestedOrder90d,
        decimal EstimatedCost30d,
        string RiskLevel);

    [HttpGet("replenishment")]
    public async Task<ApiResponse<IReadOnlyList<DrugForecastRow>>> GetReplenishmentForecast(
        [FromQuery] string? warehouseId = null,
        [FromQuery] int safetyDays = 14)
    {
        safetyDays = Math.Clamp(safetyDays, 3, 90);
        long nowMs = SystemTime.UtcNowMs;
        long ms30dAgo = nowMs - 30L * 24 * 3600 * 1000;
        long ms90dAgo = nowMs - 90L * 24 * 3600 * 1000;
        int todayInt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));

        using var conn = _connFactory.CreateConnection();
        var drugs = (await conn.QueryAsync<dynamic>(
            "SELECT id AS Id, drug_code AS Code, drug_name AS Name, spec AS Spec, unit_name AS Unit, cost_price AS CostPrice FROM inv_drugs WHERE is_active = 1")).ToList();

        string whFilter = !string.IsNullOrWhiteSpace(warehouseId) && warehouseId != "ALL"
            ? " AND warehouse_or_pharmacy_id = @WhId"
            : string.Empty;

        // 1. 当前未冻结且未过期有效库存
        var stockList = await conn.QueryAsync<StockRow>($@"
            SELECT drug_id AS DrugId, COALESCE(SUM(quantity), 0) AS Qty
            FROM inv_drug_batches
            WHERE is_frozen = 0 AND expiry_date_int >= @Today {whFilter}
            GROUP BY drug_id",
            new { Today = todayInt, WhId = warehouseId?.Trim() });
        var stockRows = stockList.ToDictionary(r => r.DrugId, r => r.Qty);

        // 2. 近 90 天与近 30 天真实出库消耗量（change_quantity < 0）
        var usageList = await conn.QueryAsync<UsageRow>($@"
            SELECT drug_id AS DrugId,
                   COALESCE(SUM(CASE WHEN created_at_ms >= @Ms30 THEN ABS(change_quantity) ELSE 0 END), 0) AS Out30,
                   COALESCE(SUM(ABS(change_quantity)), 0) AS Out90
            FROM inv_stock_ledgers
            WHERE change_quantity < 0 AND created_at_ms >= @Ms90 {whFilter}
            GROUP BY drug_id",
            new { Ms30 = ms30dAgo, Ms90 = ms90dAgo, WhId = warehouseId?.Trim() });
        var usageRows = usageList.ToDictionary(r => r.DrugId, r => (r.Out30, r.Out90));

        var result = new List<DrugForecastRow>(drugs.Count);
        foreach (var d in drugs)
        {
            string drugId = (string)d.Id;
            int currentStock = stockRows.TryGetValue(drugId, out int s) ? s : 0;
            var (out30, out90) = usageRows.TryGetValue(drugId, out var u) ? u : (0, 0);

            // 加权日均消耗：近30天权重60% + 近90天均值权重40%（应对季节波动）
            decimal avg30 = out30 / 30m;
            decimal avg90 = out90 / 90m;
            decimal weightedDaily = Math.Round(avg30 * 0.6m + avg90 * 0.4m, 2);

            int daysLeft = weightedDaily > 0m
                ? (int)Math.Floor(currentStock / weightedDaily)
                : (currentStock > 0 ? 999 : 0);

            int sug30 = Math.Max(0, (int)Math.Ceiling(weightedDaily * (30 + safetyDays) - currentStock));
            int sug60 = Math.Max(0, (int)Math.Ceiling(weightedDaily * (60 + safetyDays) - currentStock));
            int sug90 = Math.Max(0, (int)Math.Ceiling(weightedDaily * (90 + safetyDays) - currentStock));
            decimal costPrice = Convert.ToDecimal(d.CostPrice);

            string risk = currentStock <= 0
                ? "CRITICAL_OUT_OF_STOCK"
                : daysLeft <= safetyDays ? "HIGH_SHORTAGE_RISK"
                : daysLeft <= 30 ? "MEDIUM_REORDER" : "HEALTHY";

            result.Add(new DrugForecastRow(
                DrugId: drugId,
                DrugCode: (string)d.Code,
                DrugName: (string)d.Name,
                Spec: (string)d.Spec,
                UnitName: (string)d.Unit,
                CurrentValidStock: currentStock,
                OutboundLast30Days: out30,
                OutboundLast90Days: out90,
                WeightedDailyUsage: weightedDaily,
                DaysOutOfStock: daysLeft,
                SuggestedOrder30d: sug30,
                SuggestedOrder60d: sug60,
                SuggestedOrder90d: sug90,
                EstimatedCost30d: Math.Round(sug30 * costPrice, 2),
                RiskLevel: risk));
        }

        var ordered = result
            .OrderBy(r => r.DaysOutOfStock)
            .ThenByDescending(r => r.SuggestedOrder30d)
            .ToList();

        return ApiResponse<IReadOnlyList<DrugForecastRow>>.Success(ordered, $"已完成 {ordered.Count} 种药品的 30/60/90 天智能补货预测与缺货天数评估");
    }
}
