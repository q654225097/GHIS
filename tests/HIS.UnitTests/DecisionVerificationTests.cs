using System;
using System.Collections.Generic;
using System.Linq;
using HIS.Client.Core;
using HIS.Contracts;
using Xunit;

namespace HIS.UnitTests;

/// <summary>
/// Unit tests verifying business decisions D1 through D8.
/// </summary>
public class DecisionVerificationTests
{
    [Fact]
    public void D2_MedTechRole_ShouldNormalizeAndGrantMedTechCapability()
    {
        // 1. RoleCode constants & normalization
        Assert.Equal("MEDTECH", RoleCode.MedTech);
        Assert.Equal(RoleCode.MedTech, RoleCode.Normalize("ROLE-MEDTECH"));
        Assert.Equal(RoleCode.MedTech, RoleCode.Normalize("ROLE_MEDTECH"));
        Assert.Equal(RoleCode.MedTech, RoleCode.Normalize("MEDTECH-STAFF"));
        Assert.True(RoleCode.IsMedTech("ROLE-MEDTECH"));
        Assert.True(RoleCode.HasRole(new[] { "ROLE-MEDTECH" }, RoleCode.MedTech));

        // 2. SessionContext evaluation
        var ctx = new SessionContext();
        ctx.SetFromLogin(
            userId: "USER-MEDTECH-01",
            userName: "tech_user",
            displayName: "李建",
            roleCode: RoleCode.MedTech,
            roleName: "医技技师",
            accessToken: "token_mtc",
            activeDeptId: "DEPT-MTC-LAB",
            activeDeptName: "医学检验科",
            activeDeptCategory: "MEDTECH",
            activeWardId: null,
            prescribeLevel: 0,
            surgeryLevel: 0,
            emrSignLevel: 0);

        Assert.True(ctx.IsMedTech);
        Assert.False(ctx.IsOpdDoctor);
        Assert.False(ctx.IsPharmacist);
        Assert.True(ctx.CanSeeMedTech);
        Assert.False(ctx.CanSeeOpdStation);
        Assert.False(ctx.CanSeeIpdStation);
        Assert.False(ctx.CanSeePharmacy);
    }

    [Fact]
    public void D6_WarehouseKeeperRole_ShouldNormalizeAndDifferentiateWindowDispensing()
    {
        // 1. RoleCode constants & normalization
        Assert.Equal("WAREHOUSE-KEEPER", RoleCode.WarehouseKeeper);
        Assert.Equal(RoleCode.WarehouseKeeper, RoleCode.Normalize("ROLE-WAREHOUSE"));
        Assert.Equal(RoleCode.WarehouseKeeper, RoleCode.Normalize("WAREHOUSE_KEEPER"));
        Assert.Equal(RoleCode.WarehouseKeeper, RoleCode.Normalize("PHARM-WAREHOUSE"));
        Assert.True(RoleCode.IsWarehouseKeeper("ROLE-WAREHOUSE"));

        // 2. SessionContext evaluation
        var ctx = new SessionContext();
        ctx.SetFromLogin(
            userId: "USER-WH-01",
            userName: "warehouse_01",
            displayName: "周强",
            roleCode: RoleCode.WarehouseKeeper,
            roleName: "药库管理员",
            accessToken: "token_wh",
            activeDeptId: "DEPT-WH-01",
            activeDeptName: "全院中心药库",
            activeDeptCategory: "WAREHOUSE",
            activeWardId: null,
            prescribeLevel: 0,
            surgeryLevel: 0,
            emrSignLevel: 0);

        Assert.True(ctx.IsWarehouseKeeper);
        Assert.False(ctx.IsPharmacist);
        Assert.False(ctx.IsOpdDoctor);
        Assert.True(ctx.CanSeeWarehouse);
        Assert.False(ctx.CanSeePharmacyWindow); // Dedicated warehouse manager does not dispense at clinical window
        Assert.True(ctx.CanSeePharmacy);        // Has pharmacy group access
    }

    [Fact]
    public void D7_AdminSupervision_AdminHasFullAccess_AndDifferentiatesFromPractitioner()
    {
        var ctx = new SessionContext();
        ctx.SetFromLogin(
            userId: "USER-ADMIN",
            userName: "admin",
            displayName: "系统管理员",
            roleCode: RoleCode.Admin,
            roleName: "超级管理员",
            accessToken: "token_adm",
            activeDeptId: "DEPT-ADMIN-01",
            activeDeptName: "信息管理中心",
            activeDeptCategory: "ADMIN",
            activeWardId: null,
            prescribeLevel: 3,
            surgeryLevel: 4,
            emrSignLevel: 3);

        Assert.True(ctx.IsAdmin);
        // Admin has supervisory access to all stations
        Assert.True(ctx.CanSeeOpdStation);
        Assert.True(ctx.CanSeeIpdStation);
        Assert.True(ctx.CanSeeWardStation);
        Assert.True(ctx.CanSeeMedTech);
        Assert.True(ctx.CanSeePharmacy);
        Assert.True(ctx.CanSeeWarehouse);
        Assert.True(ctx.CanSeePharmacyWindow);
        Assert.True(ctx.CanSeeFinance);
        Assert.True(ctx.CanSeeAdminPanel);
        Assert.True(ctx.CanSeeDynFormConfig);
    }

    [Fact]
    public void D1_Cashier_HasFinanceCapability_AndNoClinicalPrescribeAuthority()
    {
        var ctx = new SessionContext();
        ctx.SetFromLogin(
            userId: "USER-CASHIER-01",
            userName: "cashier_01",
            displayName: "刘洋",
            roleCode: RoleCode.Cashier,
            roleName: "财务收费员",
            accessToken: "token_cash",
            activeDeptId: "DEPT-FIN-01",
            activeDeptName: "门急诊财务处",
            activeDeptCategory: "FINANCE",
            activeWardId: null,
            prescribeLevel: 0,
            surgeryLevel: 0,
            emrSignLevel: 0);

        Assert.True(ctx.IsCashier);
        Assert.True(ctx.CanSeeFinance);
        Assert.False(ctx.CanPrescribe);
        Assert.False(ctx.CanSignEmr);
        Assert.False(ctx.CanSeeOpdStation);
        Assert.False(ctx.CanSeeIpdStation);
        Assert.False(ctx.CanSeeAdminPanel);
    }
}
