using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    /// <summary>
    /// 幂等初始化全院 RBAC 权限资源树 (sys_permission_resources) 与默认岗位授权映射 (sys_role_permissions)，
    /// 消除权限点空表隐患，为服务端细粒度授权与前端菜单动态鉴权提供唯一事实源。
    /// </summary>
    private static async Task InitializeRbacPermissionSeedAsync(DbConnection conn)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var resources = new (string Id, string Key, string Parent, string Type, string Name, string Path)[]
        {
            ("PERM-CLINICAL", "clinical", "ROOT", "MODULE", "临床诊疗域", "/api/v4/opd"),
            ("PERM-OPD", "clinical.opd", "clinical", "API", "门诊接诊与处方", "POST:/api/v4/opd/*"),
            ("PERM-ER-FIVE", "clinical.five_centers", "clinical", "API", "急诊五大中心质控", "POST:/api/v4/five-centers/*"),
            ("PERM-IPD", "clinical.ipd", "clinical", "API", "住院诊疗与医嘱", "POST:/api/v4/ipd/*"),
            ("PERM-IPD-PRO", "clinical.ipd_pro", "clinical", "API", "住院增强与会诊管理", "POST:/api/v4/ipd-pro/*"),
            ("PERM-ICU", "clinical.icu_cis", "clinical", "API", "ICU重症监护CIS", "POST:/api/v4/icu-cis/*"),
            ("PERM-AIMS", "clinical.aims", "clinical", "API", "手术麻醉管理AIMS", "POST:/api/v4/aims/*"),
            ("PERM-CPW", "clinical.cpw", "clinical", "API", "临床路径管理", "POST:/api/v4/cpw/*"),
            ("PERM-EMR", "clinical.emr", "clinical", "API", "电子病历书写与签章", "POST:/api/v4/emr/*"),
            ("PERM-EMR-QC", "clinical.emr_qc", "clinical", "API", "电子病历质控", "POST:/api/v4/emr-qc/*"),
            ("PERM-MRMS", "clinical.mrms", "clinical", "API", "病案编码与归档封存", "POST:/api/v4/mrms/*"),
            ("PERM-NURSING", "nursing.ward", "ROOT", "API", "病区护理与排班管理", "POST:/api/v4/nursing-mgmt/*"),
            ("PERM-NUTRITION", "clinical.nutrition", "clinical", "API", "临床营养NRS-2002与配餐", "POST:/api/v4/nutrition/*"),
            ("PERM-FOLLOWUP", "clinical.followup", "clinical", "API", "院后随访与30天再入院", "POST:/api/v4/followup/*"),
            ("PERM-PHARMACY", "pharmacy.dispense", "ROOT", "API", "药房发药与审方", "POST:/api/v4/pharmacy/*"),
            ("PERM-PIVAS", "pharmacy.pivas_abx", "pharmacy.dispense", "API", "抗菌药DDDs与PIVAS静配", "POST:/api/v4/pivas-abx/*"),
            ("PERM-INVENTORY", "pharmacy.inventory", "pharmacy.dispense", "API", "药库采购调拨与预测", "POST:/api/v4/inventory/*"),
            ("PERM-CSM-UDI", "supply.udi", "ROOT", "API", "高值耗材UDI消耗与代销对账", "POST:/api/v4/udi/*"),
            ("PERM-MEDTECH", "medtech.exec", "ROOT", "API", "医技检验检查与报告审签", "POST:/api/v4/medtech/*"),
            ("PERM-BLOOD", "medtech.blood", "medtech.exec", "API", "输血科配血与发血", "POST:/api/v4/blood/*"),
            ("PERM-CHECKUP", "checkup.manage", "ROOT", "API", "健康体检与团检结算", "POST:/api/v4/checkup-pro/*"),
            ("PERM-FINANCE", "finance.cashier", "ROOT", "API", "收费结算退费与日结", "POST:/api/v4/finance/*"),
            ("PERM-DRG", "finance.drg_cost", "finance.cashier", "API", "DRG/DIP事中实时控费", "POST:/api/v4/drg-cost/*"),
            ("PERM-PH", "ops.public_health", "ROOT", "API", "公共卫生多报卡强触发", "POST:/api/v4/public-health/*"),
            ("PERM-MED-AFFAIRS", "ops.medical_affairs", "ROOT", "API", "医务动态授权与纠纷封存", "POST:/api/v4/medical-affairs/*"),
            ("PERM-IPC", "ops.ipc", "ROOT", "API", "院感监测与隔离控制", "POST:/api/v4/ipc/*"),
            ("PERM-CONFIG", "sys.config", "ROOT", "API", "主数据字典与动态表单配置", "POST:/api/v4/config/*"),
            ("PERM-MDM", "sys.mdm", "sys.config", "API", "全院主数据治理MDM", "POST:/api/v4/mdm/*"),
            ("PERM-HR", "sys.hr", "ROOT", "API", "人力资源与资质档案", "POST:/api/v4/hr/*"),
            ("PERM-PLATFORM", "sys.platform", "ROOT", "API", "用户角色与安全审计管理", "POST:/api/v4/platform/*")
        };

        foreach (var r in resources)
        {
            int exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sys_permission_resources WHERE id = @Id OR resource_key = @Key",
                new { r.Id, r.Key });
            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
INSERT INTO sys_permission_resources (
    id, resource_key, parent_key, resource_type, resource_name, ursa_control_type, api_method_path,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @Key, @Parent, @Type, @Name, 'RouteGuard', @Path,
    1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1
);", new { r.Id, r.Key, r.Parent, r.Type, r.Name, r.Path, Now = now });
            }
        }

        var roleBindings = new (string Id, string RoleId, string ResourceKey)[]
        {
            ("RP-OPD-1", "ROLE-OPD-DOC", "clinical.opd"),
            ("RP-OPD-2", "ROLE-OPD-DOC", "clinical.five_centers"),
            ("RP-OPD-3", "ROLE-OPD-DOC", "ops.public_health"),
            ("RP-IPD-1", "ROLE-IPD-ATTENDING", "clinical.ipd"),
            ("RP-IPD-2", "ROLE-IPD-ATTENDING", "clinical.icu_cis"),
            ("RP-IPD-3", "ROLE-IPD-ATTENDING", "clinical.aims"),
            ("RP-IPD-4", "ROLE-IPD-ATTENDING", "finance.drg_cost"),
            ("RP-IPD-5", "ROLE-IPD-ATTENDING", "clinical.nutrition"),
            ("RP-IPD-6", "ROLE-IPD-ATTENDING", "clinical.followup"),
            ("RP-NUR-1", "ROLE-WARD-NURSE", "nursing.ward"),
            ("RP-NUR-2", "ROLE-WARD-NURSE", "clinical.icu_cis"),
            ("RP-NUR-3", "ROLE-WARD-NURSE", "clinical.nutrition"),
            ("RP-PHM-1", "ROLE-PHARMACIST", "pharmacy.dispense"),
            ("RP-PHM-2", "ROLE-PHARMACIST", "pharmacy.pivas_abx"),
            ("RP-WH-1", "ROLE-WAREHOUSE", "pharmacy.inventory"),
            ("RP-WH-2", "ROLE-WAREHOUSE", "supply.udi"),
            ("RP-FIN-1", "ROLE-CASHIER", "finance.cashier"),
            ("RP-FIN-2", "ROLE-CASHIER", "finance.drg_cost"),
            ("RP-MTC-1", "ROLE-MEDTECH", "medtech.exec"),
            ("RP-MTC-2", "ROLE-MEDTECH", "medtech.blood")
        };

        foreach (var b in roleBindings)
        {
            int exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sys_role_permissions WHERE id = @Id OR (role_id = @RoleId AND resource_key = @ResourceKey)",
                new { b.Id, b.RoleId, b.ResourceKey });
            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
INSERT INTO sys_role_permissions (
    id, role_id, resource_key, access_policy,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @RoleId, @ResourceKey, 'ALLOW',
    1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1
);", new { b.Id, b.RoleId, b.ResourceKey, Now = now });
            }
        }
    }
}
