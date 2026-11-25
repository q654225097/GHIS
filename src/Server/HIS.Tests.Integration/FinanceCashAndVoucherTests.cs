using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Finance.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HIS.Tests.Integration;

public sealed class FinanceCashAndVoucherTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var factory = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(factory);
        return factory;
    }

    private static FinanceController CreateController(IDbConnectionFactory factory, string roleCode, string userId = "U-TEST-01")
    {
        var session = new UserSession
        {
            UserId = userId,
            UserName = "tester",
            DisplayName = "测试员",
            RoleCode = roleCode,
            Roles = new[] { roleCode }
        };
        return new FinanceController(
            new DapperRepository<FinPaymentOrder>(factory, "fin_payment_orders"),
            new DapperRepository<FinRefundOrder>(factory, "fin_refund_orders"),
            new DapperRepository<FinChargeLedger>(factory, "fin_charge_ledgers"),
            new DapperRepository<FinDailyClosing>(factory, "fin_daily_closings"),
            factory,
            userSession: session);
    }

    [Fact]
    public async Task SettlePayment_Rejects_NonCashier_Rbac_Violation()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = CreateController(db, RoleCode.OpdDoctor, "DOC-01");

        var res = await ctrl.SettlePayment(new FinanceController.SettlePaymentRequestDto(
            Scene: "OPD",
            PatientId: "P-01",
            VisitOrEncounterId: "V-01",
            TotalAmount: 100m,
            InsuranceAmount: 0m,
            PrepaymentDeduct: 0m,
            SelfPayAmount: 100m,
            PaymentChannel: "CASH",
            ChargeItems: null,
            CashTendered: 100m));

        Assert.Equal(403, res.Code);
    }

    [Fact]
    public async Task SettlePayment_Rejects_NonCash_PaymentChannels()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = CreateController(db, RoleCode.Cashier, "CASHIER-01");

        var res = await ctrl.SettlePayment(new FinanceController.SettlePaymentRequestDto(
            Scene: "OPD",
            PatientId: "P-01",
            VisitOrEncounterId: "V-01",
            TotalAmount: 100m,
            InsuranceAmount: 0m,
            PrepaymentDeduct: 0m,
            SelfPayAmount: 100m,
            PaymentChannel: "CRYPTO",
            ChargeItems: null,
            CashTendered: 100m));

        Assert.Equal(400, res.Code);
        Assert.Contains("不支持渠道", res.Message);
    }

    [Fact]
    public async Task SettlePayment_Rejects_Insufficient_CashTendered()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = CreateController(db, RoleCode.Cashier, "CASHIER-01");

        var res = await ctrl.SettlePayment(new FinanceController.SettlePaymentRequestDto(
            Scene: "OPD",
            PatientId: "P-01",
            VisitOrEncounterId: "V-01",
            TotalAmount: 100m,
            InsuranceAmount: 0m,
            PrepaymentDeduct: 0m,
            SelfPayAmount: 100m,
            PaymentChannel: "CASH",
            ChargeItems: null,
            CashTendered: 50m));

        Assert.Equal(400, res.Code);
        Assert.Contains("现金实收金额", res.Message);
    }

    [Fact]
    public async Task ExportDailyClosingVoucher_Returns_Balanced_DoubleEntry()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        int todayInt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO fin_payment_orders
                (id, payment_no, scene, patient_id, visit_or_encounter_id, total_amount, insurance_amount, prepayment_deduct, self_pay_amount, payment_channel, status, paid_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES
                ('PAY-1', 'PAY20260001', 'OPD', 'P1', 'V1', 150.00, 0, 0, 150.00, 'CASH', 1, @Now, 1, @Now, @Now, 1)",
                new { Now = now });
        }

        var ctrl = CreateController(db, RoleCode.Cashier, "CASHIER-01");
        var action = await ctrl.ExportDailyClosingVoucher(todayInt, "csv");
        var fileResult = Assert.IsType<FileContentResult>(action);
        string csv = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);

        Assert.Contains("1001,库存现金", csv);
        Assert.Contains("4001,医疗收入-门诊", csv);
        Assert.Contains("150.00", csv);
    }
}
