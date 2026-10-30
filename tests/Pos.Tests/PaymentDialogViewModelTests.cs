using System.Net;
using Pos.Core.Sync;
using Pos.Desktop.ViewModels;
using Pos.Infra.Api;

namespace Pos.Tests;

public class PaymentDialogViewModelTests
{
    [Fact]
    public void MPESA_cannot_submit_until_a_transaction_ref_is_entered()
    {
        var vm = new PaymentDialogViewModel(10m, "TILL", "174379");
        vm.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        vm.Lines[0].AmountText = "10.00";

        Assert.False(vm.CanSubmit);

        vm.Lines[0].TransactionRef = "QCX7Y8ZABC";
        Assert.True(vm.CanSubmit);
    }

    [Fact]
    public void Cash_and_card_lines_never_require_a_transaction_ref()
    {
        var vm = new PaymentDialogViewModel(10m);
        vm.Lines[0].AmountText = "10.00"; // defaults to CASH
        Assert.True(vm.CanSubmit);

        vm.Lines[0].Method = OfflinePaymentMethod.Card;
        Assert.True(vm.CanSubmit);
    }

    [Fact]
    public void Hint_names_the_shortcode_when_one_is_configured()
    {
        var withTill = new PaymentDialogViewModel(10m, "TILL", "174379");
        Assert.Contains("Till", withTill.MpesaHint);
        Assert.Contains("174379", withTill.MpesaHint);

        var withPaybill = new PaymentDialogViewModel(10m, "PAYBILL", "888999");
        Assert.Contains("Paybill", withPaybill.MpesaHint);

        var unset = new PaymentDialogViewModel(10m, null, null);
        Assert.DoesNotContain("Till", unset.MpesaHint);
        Assert.DoesNotContain("Paybill", unset.MpesaHint);
    }

    [Fact]
    public void HasMpesaLine_tracks_the_selected_methods()
    {
        var vm = new PaymentDialogViewModel(10m);
        Assert.False(vm.HasMpesaLine);

        vm.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        Assert.True(vm.HasMpesaLine);
    }

    [Fact]
    public void IsMpesaWithStk_requires_both_a_configured_credential_and_being_online()
    {
        var offline = new PaymentDialogViewModel(10m, "TILL", "174379", mpesaStkConfigured: true, isOnline: false);
        offline.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        Assert.False(offline.Lines[0].IsMpesaWithStk);

        var notConfigured = new PaymentDialogViewModel(10m, "TILL", "174379", mpesaStkConfigured: false, isOnline: true);
        notConfigured.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        Assert.False(notConfigured.Lines[0].IsMpesaWithStk);

        var ready = new PaymentDialogViewModel(10m, "TILL", "174379", mpesaStkConfigured: true, isOnline: true);
        ready.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        Assert.True(ready.Lines[0].IsMpesaWithStk);
    }

    [Fact]
    public async Task SendStkPushAsync_rejects_an_invalid_phone_number_without_calling_the_server()
    {
        var api = new ApiClient(new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://fake-server/api/v1/") });
        var vm = new PaymentDialogViewModel(10m, "TILL", "174379", mpesaStkConfigured: true, api: api, deviceToken: "dev-token", isOnline: true);
        vm.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        vm.Lines[0].StkPhoneNumber = "0712345678"; // not the 2547/2541-prefixed format Daraja requires

        await vm.SendStkPushCommand.ExecuteAsync(vm.Lines[0]);

        Assert.Contains("2547", vm.Lines[0].StkStatusText);
        Assert.Equal("", vm.Lines[0].TransactionRef);
    }

    [Fact]
    public async Task SendStkPushAsync_fills_TransactionRef_once_the_intent_completes()
    {
        var handler = new SequencedHandler(
            (HttpMethod.Post, "payment-intents", """{"id":"intent-1","status":"PENDING"}"""),
            (HttpMethod.Get, "payment-intents/intent-1", """{"id":"intent-1","status":"COMPLETED","transactionRef":"QCX7Y8ZABC"}"""));
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://fake-server/api/v1/") });
        var vm = new PaymentDialogViewModel(10m, "TILL", "174379", mpesaStkConfigured: true, api: api, deviceToken: "dev-token", isOnline: true);
        vm.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        vm.Lines[0].AmountText = "10.00";
        vm.Lines[0].StkPhoneNumber = "254708374149";

        await vm.SendStkPushCommand.ExecuteAsync(vm.Lines[0]);

        Assert.Equal("QCX7Y8ZABC", vm.Lines[0].TransactionRef);
        Assert.True(vm.CanSubmit);
        Assert.False(vm.Lines[0].IsSendingStk);
    }

    [Fact]
    public async Task SendStkPushAsync_surfaces_the_result_description_when_the_customer_cancels()
    {
        var handler = new SequencedHandler(
            (HttpMethod.Post, "payment-intents", """{"id":"intent-2","status":"PENDING"}"""),
            (HttpMethod.Get, "payment-intents/intent-2", """{"id":"intent-2","status":"CANCELLED","resultDesc":"Request cancelled by user"}"""));
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://fake-server/api/v1/") });
        var vm = new PaymentDialogViewModel(10m, "TILL", "174379", mpesaStkConfigured: true, api: api, deviceToken: "dev-token", isOnline: true);
        vm.Lines[0].Method = OfflinePaymentMethod.Mpesa;
        vm.Lines[0].AmountText = "10.00";
        vm.Lines[0].StkPhoneNumber = "254708374149";

        await vm.SendStkPushCommand.ExecuteAsync(vm.Lines[0]);

        Assert.Equal("Request cancelled by user", vm.Lines[0].StkStatusText);
        Assert.Equal("", vm.Lines[0].TransactionRef);
        Assert.False(vm.CanSubmit);
    }

    /// <summary>Throws if called at all — proves client-side validation (an invalid
    /// phone number) never reaches the network.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("No HTTP call was expected for this input");
    }

    /// <summary>Answers a fixed, ordered sequence of (method, path-suffix) -> JSON
    /// responses — asserts the dialog called the right endpoint in the right order
    /// rather than silently returning whatever is next in the queue.</summary>
    private sealed class SequencedHandler(params (HttpMethod Method, string PathSuffix, string Json)[] steps) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_index >= steps.Length) throw new InvalidOperationException("No more responses queued");
            var (method, suffix, json) = steps[_index++];
            if (request.Method != method || !request.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected {method} .../{suffix}, got {request.Method} {request.RequestUri}");

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            return Task.FromResult(response);
        }
    }
}
