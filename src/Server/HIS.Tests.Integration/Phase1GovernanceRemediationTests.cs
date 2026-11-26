using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Modules.Consumables.Controllers;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Pharmacy.Controllers;
using Xunit;

namespace HIS.Tests.Integration;

/// <summary>
/// 第一阶段架构止血与安全封口专项回归测试：
/// 1) 服务端 RBAC 角色授权校验（低权限越权拒绝 + ADMIN 超级管理员放行 + sys_permission_resources 种子非空）
/// 2) 达梦 DM8 / PostgreSQL 方言 SQL 翻译器回归验证
/// 3) 双轨分裂表跨模块写入闭环验证（UDI<->csm_stocks、ICU三管<->ipc_infection_reports、PIVAS<->phm_ward_pivas_batches）
/// </summary>
public sealed class Phase1GovernanceRemediationTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory($"Data Source=phase1_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task Rbac_Seed_Populates_PermissionResources_And_Authorizer_Blocks_Unauthorized_Role()
    {
        var db = await CreateFreshDbAsync();
        using var conn = db.CreateConnection();
        int permCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys_permission_resources");
        int bindCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys_role_permissions");
        Assert.True(permCount >= 25, $"Expected >= 25 permission resources, got {permCount}");
        Assert.True(bindCount >= 15, $"Expected >= 15 role permission bindings, got {bindCount}");

        var req = new[] { new RequireHisRoleAttribute("ADMIN", "IPD_ATTENDING") };

        // 收费员尝试访问住院主治/管理接口 -> 拒绝
        var cashier = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "CASHIER") }, "Bearer"));
        Assert.False(RequireHisRoleAttribute.IsAuthorized(cashier, req));

        // 住院主治医师访问 -> 放行
        var attending = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("role_code", "ROLE-IPD-ATTENDING") }, "Bearer"));
        Assert.True(RequireHisRoleAttribute.IsAuthorized(attending, req));

        // 超级管理员访问 -> 放行
        var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("role", "ADMIN") }, "Bearer"));
        Assert.True(RequireHisRoleAttribute.IsAuthorized(admin, req));
    }

    [Fact]
    public void MultiDb_DialectTranslator_Rewrites_Sqlite_Syntax_For_Dameng_And_Postgres()
    {
        string sql = "INSERT OR IGNORE INTO sys_roles (id, name) VALUES ('1', CAST(123 AS TEXT));";

        string dmSql = MultiDbConnectionFactory.TranslateSqlForDialect(sql, HisDbProviderKind.Dameng);
        Assert.Contains("IGNORE_ROW_ON_DUPKEY_INDEX", dmSql);
        Assert.Contains("VARCHAR(4000)", dmSql);
        Assert.DoesNotContain("INSERT OR IGNORE", dmSql);

        string pgSql = MultiDbConnectionFactory.TranslateSqlForDialect(sql, HisDbProviderKind.Postgres);
        Assert.Contains("ON CONFLICT DO NOTHING", pgSql);
        Assert.DoesNotContain("INSERT OR IGNORE", pgSql);
    }

    [Fact]
    public async Task DualTrack_Closure_Syncs_Udi_To_CsmStocks_Icu_To_Ipc_And_Pivas_To_WardBatches()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;

        // 1) UDI 消耗 -> 联动扣减 csm_stocks 并留痕 csm_stock_movements
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
INSERT INTO csm_consumables (id, consumable_code, consumable_name, category, spec, unit, unit_price, is_high_value, is_sterile, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('CSM-1', 'STENT-99', '冠脉支架', 'IMPLANT', '3.0x18', '个', 7500, 1, 1, 1, 1, @Now, @Now, 1);
INSERT INTO csm_stocks (id, consumable_id, warehouse_id, quantity, reserved_qty, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('STK-1', 'CSM-1', 'WH-OR-CONSIGN', 5, 0, 1, @Now, @Now, 1);", new { Now = now });
        }

        var udiCtrl = new UdiConsumablesController(db);
        string udi = "(01)06912345678909(17)291231(10)L01(21)S01";
        Assert.Equal(200, (await udiCtrl.ReceiveUdi(new UdiConsumablesController.ReceiveUdiDto(udi, "STENT-99", "冠脉支架", "3.0x18", "供应商A", true, 6500m, 7500m, null), default)).Code);
        Assert.Equal(200, (await udiCtrl.ConsumeAndBillUdi(new UdiConsumablesController.ConsumeUdiDto(udi, "ENC-1", "PAT-1", "S-1", "OR", "OR"), default)).Code);

        using (var conn = db.CreateConnection())
        {
            Assert.Equal(4, await conn.ExecuteScalarAsync<int>("SELECT quantity FROM csm_stocks WHERE id = 'STK-1'"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM csm_stock_movements WHERE movement_type = 'UDI_CONSUME'"));
        }

        // 2) ICU 三管确诊 -> 同步写入院感科 ipc_infection_reports
        var icuCtrl = new IcuCisController(db);
        Assert.Equal(200, (await icuCtrl.AdmitToIcu(new IcuCisController.AdmitIcuDto("ENC-ICU-9", "PAT-ICU-9", "危重患者", "ICU-09", "呼衰", "张主任"), default)).Code);
        using (var conn = db.CreateConnection())
        {
            string admId = await conn.ExecuteScalarAsync<string>("SELECT id FROM icu_admissions LIMIT 1") ?? string.Empty;
            Assert.Equal(200, (await icuCtrl.InsertCatheter(admId, new IcuCisController.InsertCatheterDto("CLABSI", "右锁骨下静脉"), default)).Code);
            string cathId = await conn.ExecuteScalarAsync<string>("SELECT id FROM icu_catheter_surveillance LIMIT 1") ?? string.Empty;
            Assert.Equal(200, (await icuCtrl.ConfirmCatheterInfection(cathId, new IcuCisController.ConfirmCatheterInfectionDto("血培养阳性"), default)).Code);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipc_infection_reports WHERE encounter_id = 'ENC-ICU-9'"));
        }

        // 3) PIVAS 出仓 -> 同步写入住院药房 phm_ward_pivas_batches
        var pivasCtrl = new PivasAndAntimicrobialController(db);
        Assert.Equal(200, (await pivasCtrl.CreatePivasOrder(new PivasAndAntimicrobialController.CreatePivasOrderDto("O-9", "ENC-P9", "PAT-P9", "张某", "心内科", "DRUG-MEROPENEM", "美罗培南", "0.9%NS", "0.5g", "B1"), default)).Code);
        using (var conn = db.CreateConnection())
        {
            string pid = await conn.ExecuteScalarAsync<string>("SELECT id FROM pivas_orders LIMIT 1") ?? string.Empty;
            Assert.Equal(200, (await pivasCtrl.ReviewPivasOrder(pid, new PivasAndAntimicrobialController.ReviewPivasDto(true, "B1", null, "审方"), default)).Code);
            Assert.Equal(200, (await pivasCtrl.PrintLabel(pid, default)).Code);
            Assert.Equal(200, (await pivasCtrl.CompoundOrder(pid, new PivasAndAntimicrobialController.CompoundPivasDto("甲"), default)).Code);
            Assert.Equal(200, (await pivasCtrl.DoubleCheckOrder(pid, new PivasAndAntimicrobialController.CheckPivasDto("乙"), default)).Code);
            Assert.Equal(200, (await pivasCtrl.DispatchOrder(pid, default)).Code);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM phm_ward_pivas_batches WHERE encounter_id = 'ENC-P9'"));
        }
    }
}
