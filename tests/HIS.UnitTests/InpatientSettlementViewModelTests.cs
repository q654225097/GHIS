using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.Client.UrsaShell.ViewModels;
using Xunit;
using static HIS.Client.UrsaShell.ViewModels.InpatientSettlementViewModel;

namespace HIS.UnitTests;

public class InpatientSettlementViewModelTests
{
    [Theory]
    [InlineData(100, false)]
    [InlineData(150, true)]
    public async Task SwitchingPatientsDiscardsLatePreviewAndSubmitsOnlyCurrentSnapshot(int secondTotal, bool oldRequestFails)
    {
        var old = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        int firstLoads = 0;
        string? postedPath = null;
        string? postedVersion = null;
        decimal? postedTotal = null;
        var model = CreateModel(async request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                postedPath = path;
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                postedVersion = body.RootElement.GetProperty("PreviewVersion").GetString();
                postedTotal = body.RootElement.GetProperty("TotalAmount").GetDecimal();
                return Success(Row("B", "SETTLED", "PAY-B"));
            }
            if (path.EndsWith("/A/settlement-preview", StringComparison.Ordinal))
                return ++firstLoads == 1 ? Success(PreviewFor("A", 100)) : await old.Task;
            if (path.EndsWith("/B/settlement-preview", StringComparison.Ordinal))
                return Success(PreviewFor("B", secondTotal));
            return Success(new[] { Row("A"), Row("B") });
        });

        await model.LoadAsync();
        Assert.True(model.CanSettle);
        model.SettleCommand.Execute(null);
        Assert.Contains("IN-A", model.ConfirmationText);
        Task previousLoad = model.LoadPreviewAsync(model.Requests[0]);
        Assert.False(model.IsConfirmationOpen);
        Assert.False(model.CanSettle);
        Assert.Empty(model.Charges);
        Assert.Equal("0.00", model.SelfPayAmountText);

        model.SelectedRequest = model.Requests[1];
        Assert.True(model.CanSettle);
        Assert.Equal("B", model.Preview?.RequestId);
        old.SetResult(oldRequestFails ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Success(PreviewFor("A", 100)));
        await previousLoad;
        Assert.Equal("B", model.Preview?.RequestId);
        Assert.Equal("ITEM-B", Assert.Single(model.Charges).ItemCode);
        Assert.False(model.HasError);

        model.SettleCommand.Execute(null);
        Assert.Contains("IN-B", model.ConfirmationText);
        model.CancelSettlementCommand.Execute(null);
        Assert.Null(postedPath);
        await model.SettleAsync();
        Assert.EndsWith("/B/settle", postedPath);
        Assert.Equal("version-B", postedVersion);
        Assert.Equal((decimal)secondTotal, postedTotal);
        Assert.True(model.IsSettled);
        Assert.False(model.CanSettle);
        Assert.Contains("PAY-B", model.Notice);
    }

    [Theory]
    [InlineData("OTHER", "ENC-A", "PAT-A")]
    [InlineData("A", "OTHER", "PAT-A")]
    [InlineData("A", "ENC-A", "OTHER")]
    public async Task WrongRequestEncounterOrPatientNeverBecomesPayable(string id, string encounter, string patient)
    {
        int posts = 0;
        var model = CreateModel(request =>
        {
            if (request.Method == HttpMethod.Post)
                posts++;
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("settlement-preview", StringComparison.Ordinal)
                ? Success(PreviewFor("A", 100) with { RequestId = id, EncounterId = encounter, PatientId = patient })
                : Success(new[] { Row("A") }));
        });
        await model.LoadAsync();
        Assert.Null(model.Preview);
        Assert.True(model.HasError);
        await model.SettleAsync();
        Assert.Equal(0, posts);
    }

    [Fact]
    public async Task ClearedSelectionCannotReceiveLatePreview()
    {
        var old = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        int loads = 0;
        var model = CreateModel(request => request.RequestUri!.AbsolutePath.EndsWith("settlement-preview", StringComparison.Ordinal)
            ? (++loads == 1 ? Task.FromResult(Success(PreviewFor("A", 100))) : old.Task)
            : Task.FromResult(Success(new[] { Row("A") })));
        await model.LoadAsync();
        Task previousLoad = model.LoadPreviewAsync(model.Requests[0]);
        model.SelectedRequest = null;
        old.SetResult(Success(PreviewFor("A", 100)));
        await previousLoad;
        Assert.Null(model.Preview);
        Assert.False(model.CanSettle);
        Assert.Empty(model.Charges);
    }

    [Fact]
    public async Task ChangingAmountsOrChannelRequiresFreshConfirmation()
    {
        int posts = 0;
        var model = CreateModel(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                return Task.FromResult(Success(Row("A", "SETTLED", "PAY-A")));
            }
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("settlement-preview", StringComparison.Ordinal)
                ? Success(PreviewFor("A", 100) with { PrepaymentBalance = 40m }) : Success(new[] { Row("A") }));
        });
        await model.LoadAsync();

        Action[] edits = [
            () => model.InsuranceAmountText = "10.00",
            () => model.PrepaymentDeductText = "30.00",
            () => model.SelectedPaymentChannel = model.PaymentChannels[1]
        ];
        foreach (Action edit in edits)
        {
            model.SettleCommand.Execute(null);
            Assert.True(model.ConfirmSettlementCommand.CanExecute(null));
            edit();
            Assert.False(model.IsConfirmationOpen);
            Assert.False(model.ConfirmSettlementCommand.CanExecute(null));
            model.ConfirmSettlementCommand.Execute(null);
        }
        Assert.Equal(0, posts);

        model.SettleCommand.Execute(null);
        Assert.Contains("10.00", model.ConfirmationText);
        Assert.Contains("30.00", model.ConfirmationText);
        Assert.Contains(model.PaymentChannels[1].Name, model.ConfirmationText);
        model.ConfirmSettlementCommand.Execute(null);
        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task MismatchedSettlementReceiptDoesNotMarkPatientSettled()
    {
        var model = CreateModel(request => Task.FromResult(request.Method == HttpMethod.Post
            ? Success(Row("B", "SETTLED", "PAY-B"))
            : request.RequestUri!.AbsolutePath.EndsWith("settlement-preview", StringComparison.Ordinal)
                ? Success(PreviewFor("A", 100)) : Success(new[] { Row("A") })));
        await model.LoadAsync();
        await model.SettleAsync();
        Assert.False(model.IsSettled);
        Assert.False(model.CanSettle);
        Assert.Equal("PENDING", model.SelectedRequest!.Status);
        Assert.Contains("查询", model.ErrorMessage);
    }

    private static InpatientSettlementViewModel CreateModel(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(() => new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://localhost") });

    private static HttpResponseMessage Success(object data) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { Code = 200, Data = data })
    };

    private static IpdDischargeRequestDto Row(string id, string status = "PENDING", string? paymentNo = null) => new()
    {
        Id = id, EncounterId = "ENC-" + id, PatientId = "PAT-" + id, PatientName = "Patient " + id,
        AdmissionNo = "IN-" + id, RequestNo = "REQ-" + id, Status = status, PaymentNo = paymentNo
    };

    private static IpdSettlementPreviewDto PreviewFor(string id, decimal total) => new(
        id, "ENC-" + id, "PAT-" + id, "Patient " + id, "MR-" + id, "IN-" + id, "DEPT", "WARD", "BED", "PENDING",
        0m, total, [new IpdSettlementChargeDto { ItemCode = "ITEM-" + id, TotalAmount = total }], [], "version-" + id);

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
