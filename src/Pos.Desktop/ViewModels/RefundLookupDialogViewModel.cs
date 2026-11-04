using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Pricing;
using Pos.Infra.Api;

namespace Pos.Desktop.ViewModels;

public sealed partial class RefundLineViewModel : ViewModelBase
{
    private readonly int _decimalDigits;
    private readonly string _moneyFormat;

    public RefundLineViewModel(string orderItemId, string productName, decimal soldQty, decimal alreadyRefundedQty, decimal lineTotal, string currency = "USD")
    {
        OrderItemId = orderItemId;
        ProductName = productName;
        SoldQty = soldQty;
        AlreadyRefundedQty = alreadyRefundedQty;
        LineTotal = lineTotal;
        _decimalDigits = CurrencyInfo.GetDecimalDigits(currency);
        _moneyFormat = _decimalDigits == 0 ? "0" : "0." + new string('0', _decimalDigits);
    }

    public string OrderItemId { get; }
    public string ProductName { get; }
    public decimal SoldQty { get; }
    public decimal AlreadyRefundedQty { get; }
    public decimal LineTotal { get; }
    public string LineTotalText => LineTotal.ToString(_moneyFormat, CultureInfo.InvariantCulture);
    public decimal RemainingQty => SoldQty - AlreadyRefundedQty;

    [NotifyPropertyChangedFor(nameof(RefundQty))]
    [NotifyPropertyChangedFor(nameof(RefundAmountPreview))]
    [NotifyPropertyChangedFor(nameof(RefundAmountPreviewText))]
    [ObservableProperty] private string _refundQtyText = "";

    public decimal RefundQty
    {
        get
        {
            if (!decimal.TryParse(RefundQtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v <= 0) return 0;
            return Math.Min(v, RemainingQty);
        }
    }

    // Proportional to the line's original total, same formula the web app's
    // RefundModal.jsx already uses — not re-derived from current catalog pricing.
    public decimal RefundAmountPreview => SoldQty > 0 ? Money.Round(RefundQty * (LineTotal / SoldQty), _decimalDigits) : 0;
    public string RefundAmountPreviewText => RefundAmountPreview.ToString(_moneyFormat, CultureInfo.InvariantCulture);
}

public sealed partial class RefundLookupDialogViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly string _deviceToken;
    private readonly string _locationId;
    private readonly string _processedByUserId;

    // Instance property, not static — Avalonia's {Binding} only resolves against
    // the DataContext instance (same reasoning as PaymentDialogViewModel.Methods).
    public string[] Methods { get; } = ["CASH", "CARD", "GIFT_CARD", "STORE_CREDIT", "OTHER"];

    public RefundLookupDialogViewModel(ApiClient api, string deviceToken, string locationId, string processedByUserId, bool hasPermission, bool isOnline)
    {
        _api = api;
        _deviceToken = deviceToken;
        _locationId = locationId;
        _processedByUserId = processedByUserId;
        HasPermission = hasPermission;
        IsOnline = isOnline;
    }

    public bool HasPermission { get; }
    public bool IsOnline { get; }
    public bool CanRefund => HasPermission && IsOnline;

    public bool ShowOfflineMessage => HasPermission && !IsOnline;

    public ObservableCollection<OrderSummaryDto> Results { get; } = [];
    public ObservableCollection<RefundLineViewModel> RefundLines { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _successMessage;
    [ObservableProperty] private OrderDetailDto? _selectedOrder;

    [NotifyPropertyChangedFor(nameof(CanSubmitRefund))]
    [ObservableProperty] private string _refundMethod = "CASH";
    [NotifyPropertyChangedFor(nameof(CanSubmitRefund))]
    [ObservableProperty] private string _giftCardCode = "";
    [ObservableProperty] private string _reason = "";

    public bool IsSearchStep => SelectedOrder is null;
    public bool IsGiftCard => RefundMethod == "GIFT_CARD";
    public bool RefundSucceeded { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    public bool CanSubmitRefund =>
        !IsBusy &&
        RefundLines.Any(l => l.RefundQty > 0) &&
        (!IsGiftCard || !string.IsNullOrWhiteSpace(GiftCardCode));

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!CanRefund) return;
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await _api.ListOrdersAsync(_deviceToken, SearchText.Trim(), _locationId);
            Results.Clear();
            foreach (var r in results) Results.Add(r);
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            ErrorMessage = $"Couldn't search orders: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SelectOrderAsync(OrderSummaryDto summary)
    {
        if (!CanRefund) return;
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var order = await _api.GetOrderAsync(_deviceToken, summary.Id);
            var refundedByItem = order.Refunds
                .SelectMany(r => r.Items)
                .GroupBy(i => i.OrderItemId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

            RefundLines.Clear();
            foreach (var item in order.Items)
            {
                var productName = item.Variant?.Product?.Name ?? item.Variant?.Sku ?? "Item";
                var alreadyRefunded = refundedByItem.GetValueOrDefault(item.Id, 0);
                var line = new RefundLineViewModel(item.Id, productName, item.Quantity, alreadyRefunded, item.LineTotal, order.Currency);
                line.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanSubmitRefund));
                RefundLines.Add(line);
            }

            SelectedOrder = order;
            OnPropertyChanged(nameof(IsSearchStep));
            OnPropertyChanged(nameof(CanSubmitRefund));
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            ErrorMessage = $"Couldn't load this order: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BackToSearch()
    {
        SelectedOrder = null;
        RefundLines.Clear();
        ErrorMessage = null;
        SuccessMessage = null;
        OnPropertyChanged(nameof(IsSearchStep));
        OnPropertyChanged(nameof(CanSubmitRefund));
    }

    [RelayCommand]
    private async Task SubmitRefundAsync()
    {
        if (!CanRefund || SelectedOrder is null || !CanSubmitRefund) return;

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var request = new RefundRequest
            {
                Items = [.. RefundLines.Where(l => l.RefundQty > 0).Select(l => new RefundRequestItem { OrderItemId = l.OrderItemId, Quantity = l.RefundQty, Amount = l.RefundAmountPreview })],
                Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
                RefundMethod = RefundMethod,
                GiftCardCode = IsGiftCard ? GiftCardCode.Trim() : null,
                ProcessedByUserId = _processedByUserId,
            };
            var updated = await _api.RefundOrderAsync(_deviceToken, SelectedOrder.Id, request);
            RefundSucceeded = true;
            SuccessMessage = $"Refunded. Order is now {updated.Status}.";
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            ErrorMessage = $"Couldn't process this refund: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(RefundSucceeded);
}
