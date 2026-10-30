using System.Net;
using Pos.Desktop.ViewModels;
using Pos.Infra.Api;

namespace Pos.Tests;

public class RefundLineViewModelTests
{
    [Fact]
    public void RemainingQty_subtracts_what_has_already_been_refunded()
    {
        var line = new RefundLineViewModel("item-1", "Pen", soldQty: 3, alreadyRefundedQty: 1, lineTotal: 3.75m);
        Assert.Equal(2, line.RemainingQty);
    }

    [Fact]
    public void RefundQty_is_clamped_to_RemainingQty()
    {
        var line = new RefundLineViewModel("item-1", "Pen", soldQty: 3, alreadyRefundedQty: 1, lineTotal: 3.75m);
        line.RefundQtyText = "5"; // more than the 2 remaining
        Assert.Equal(2, line.RefundQty);
    }

    [Fact]
    public void RefundQty_ignores_blank_or_non_positive_input()
    {
        var line = new RefundLineViewModel("item-1", "Pen", soldQty: 3, alreadyRefundedQty: 0, lineTotal: 3.75m);
        Assert.Equal(0, line.RefundQty);
        line.RefundQtyText = "0";
        Assert.Equal(0, line.RefundQty);
        line.RefundQtyText = "-1";
        Assert.Equal(0, line.RefundQty);
    }

    [Fact]
    public void RefundAmountPreview_is_proportional_to_the_original_line_total()
    {
        // 3 sold for a line total of 3.75 (1.25 each) — refunding 2 should preview 2.50,
        // same formula the web app's RefundModal.jsx already uses.
        var line = new RefundLineViewModel("item-1", "Pen", soldQty: 3, alreadyRefundedQty: 0, lineTotal: 3.75m);
        line.RefundQtyText = "2";
        Assert.Equal(2.50m, line.RefundAmountPreview);
    }
}

public class RefundLookupDialogViewModelTests
{
    private static ApiClient FakeApi(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://fake-server/api/v1/") });

    [Fact]
    public void CanRefund_requires_both_permission_and_connectivity()
    {
        var api = FakeApi(new ThrowingHandler());

        Assert.False(new RefundLookupDialogViewModel(api, "tok", "loc-1", "user-1", hasPermission: false, isOnline: true).CanRefund);
        Assert.False(new RefundLookupDialogViewModel(api, "tok", "loc-1", "user-1", hasPermission: true, isOnline: false).CanRefund);
        Assert.True(new RefundLookupDialogViewModel(api, "tok", "loc-1", "user-1", hasPermission: true, isOnline: true).CanRefund);
    }

    [Fact]
    public void ShowOfflineMessage_only_fires_once_permission_is_fine()
    {
        var api = FakeApi(new ThrowingHandler());
        var missingPermission = new RefundLookupDialogViewModel(api, "tok", "loc-1", "user-1", hasPermission: false, isOnline: false);
        Assert.False(missingPermission.ShowOfflineMessage, "the permission message should be the one shown, not both");

        var offlineOnly = new RefundLookupDialogViewModel(api, "tok", "loc-1", "user-1", hasPermission: true, isOnline: false);
        Assert.True(offlineOnly.ShowOfflineMessage);
    }

    [Fact]
    public async Task SelectOrderAsync_computes_remaining_quantity_from_the_live_order()
    {
        var handler = new SequencedHandler(
            (HttpMethod.Get, "orders/order-1", """
                {
                  "id": "order-1", "orderNumber": 51, "status": "COMPLETED", "total": 3.75,
                  "items": [{ "id": "item-1", "quantity": 3, "lineTotal": 3.75, "variant": { "sku": "PEN", "product": { "name": "Pen" } } }],
                  "payments": [],
                  "refunds": [{ "id": "r1", "totalAmount": 1.25, "items": [{ "orderItemId": "item-1", "quantity": 1 }] }]
                }
                """));
        var vm = new RefundLookupDialogViewModel(FakeApi(handler), "tok", "loc-1", "user-1", hasPermission: true, isOnline: true);

        await vm.SelectOrderCommand.ExecuteAsync(new OrderSummaryDto { Id = "order-1", OrderNumber = 51 });

        Assert.False(vm.IsSearchStep);
        Assert.Single(vm.RefundLines);
        Assert.Equal("Pen", vm.RefundLines[0].ProductName);
        Assert.Equal(2, vm.RefundLines[0].RemainingQty); // 3 sold - 1 already refunded
    }

    [Fact]
    public async Task CanSubmitRefund_is_false_until_a_line_has_a_positive_quantity()
    {
        var handler = new SequencedHandler(
            (HttpMethod.Get, "orders/order-1", """
                {
                  "id": "order-1", "orderNumber": 51, "status": "COMPLETED", "total": 3.75,
                  "items": [{ "id": "item-1", "quantity": 3, "lineTotal": 3.75, "variant": { "sku": "PEN", "product": { "name": "Pen" } } }],
                  "payments": [], "refunds": []
                }
                """));
        var vm = new RefundLookupDialogViewModel(FakeApi(handler), "tok", "loc-1", "user-1", hasPermission: true, isOnline: true);
        await vm.SelectOrderCommand.ExecuteAsync(new OrderSummaryDto { Id = "order-1", OrderNumber = 51 });

        Assert.False(vm.CanSubmitRefund);
        vm.RefundLines[0].RefundQtyText = "1";
        Assert.True(vm.CanSubmitRefund);
    }

    [Fact]
    public async Task CanSubmitRefund_requires_a_gift_card_code_when_that_method_is_selected()
    {
        var handler = new SequencedHandler(
            (HttpMethod.Get, "orders/order-1", """
                {
                  "id": "order-1", "orderNumber": 51, "status": "COMPLETED", "total": 3.75,
                  "items": [{ "id": "item-1", "quantity": 3, "lineTotal": 3.75, "variant": { "sku": "PEN", "product": { "name": "Pen" } } }],
                  "payments": [], "refunds": []
                }
                """));
        var vm = new RefundLookupDialogViewModel(FakeApi(handler), "tok", "loc-1", "user-1", hasPermission: true, isOnline: true);
        await vm.SelectOrderCommand.ExecuteAsync(new OrderSummaryDto { Id = "order-1", OrderNumber = 51 });
        vm.RefundLines[0].RefundQtyText = "1";
        Assert.True(vm.CanSubmitRefund);

        vm.RefundMethod = "GIFT_CARD";
        Assert.False(vm.CanSubmitRefund);
        vm.GiftCardCode = "GC123";
        Assert.True(vm.CanSubmitRefund);
    }

    [Fact]
    public async Task SubmitRefundAsync_sends_the_signed_in_cashier_as_processedByUserId()
    {
        RefundRequest? sent = null;
        var handler = new RecordingHandler(
            (HttpMethod.Get, "orders/order-1", """
                {
                  "id": "order-1", "orderNumber": 51, "status": "COMPLETED", "total": 3.75,
                  "items": [{ "id": "item-1", "quantity": 3, "lineTotal": 3.75, "variant": { "sku": "PEN", "product": { "name": "Pen" } } }],
                  "payments": [], "refunds": []
                }
                """, null),
            (HttpMethod.Post, "orders/order-1/refund", """{"id":"order-1","orderNumber":51,"status":"PARTIALLY_REFUNDED","total":3.75}""",
                body => sent = System.Text.Json.JsonSerializer.Deserialize<RefundRequest>(body, Pos.Infra.Json.SyncJson.Options)));
        var vm = new RefundLookupDialogViewModel(FakeApi(handler), "tok", "loc-1", "user-42", hasPermission: true, isOnline: true);
        await vm.SelectOrderCommand.ExecuteAsync(new OrderSummaryDto { Id = "order-1", OrderNumber = 51 });
        vm.RefundLines[0].RefundQtyText = "1";

        await vm.SubmitRefundCommand.ExecuteAsync(null);

        Assert.True(vm.RefundSucceeded);
        Assert.NotNull(sent);
        Assert.Equal("user-42", sent!.ProcessedByUserId);
        Assert.Equal("item-1", sent.Items[0].OrderItemId);
        Assert.Equal(1, sent.Items[0].Quantity);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("No HTTP call was expected");
    }

    private sealed class SequencedHandler(params (HttpMethod Method, string PathSuffix, string Json)[] steps) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_index >= steps.Length) throw new InvalidOperationException("No more responses queued");
            var (method, suffix, json) = steps[_index++];
            if (request.Method != method || !request.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected {method} .../{suffix}, got {request.Method} {request.RequestUri}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class RecordingHandler(params (HttpMethod Method, string PathSuffix, string Json, Action<string>? OnBody)[] steps) : HttpMessageHandler
    {
        private int _index;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_index >= steps.Length) throw new InvalidOperationException("No more responses queued");
            var (method, suffix, json, onBody) = steps[_index++];
            if (request.Method != method || !request.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected {method} .../{suffix}, got {request.Method} {request.RequestUri}");

            if (onBody is not null && request.Content is not null) onBody(await request.Content.ReadAsStringAsync(ct));

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
