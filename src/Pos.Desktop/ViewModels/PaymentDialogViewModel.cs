using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Sync;
using Pos.Infra.Api;

namespace Pos.Desktop.ViewModels;

/// <summary>One tender line. <c>CARD</c>/<c>OTHER</c> just record a reference.
/// <c>GIFT_CARD</c> additionally needs <see cref="GiftCardCode"/> — the server checks
/// and decrements its balance atomically at sync time (<c>offlineSync.js</c>'s
/// <c>importOfflineOrder</c>), so a stale local balance is never trusted here.</summary>
public sealed partial class TenderLineViewModel : ViewModelBase
{
    private readonly bool _showStkOption;

    public TenderLineViewModel(string method, decimal amount, bool showStkOption = false)
    {
        Method = method;
        AmountText = amount > 0 ? amount.ToString("0.00", CultureInfo.InvariantCulture) : "";
        _showStkOption = showStkOption;
    }

    [NotifyPropertyChangedFor(nameof(IsMpesa))]
    [NotifyPropertyChangedFor(nameof(IsCardOrOther))]
    [NotifyPropertyChangedFor(nameof(IsMpesaWithStk))]
    [NotifyPropertyChangedFor(nameof(IsGiftCard))]
    [NotifyPropertyChangedFor(nameof(IsStoreCredit))]
    [ObservableProperty] private string _method;
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _transactionRef = "";
    [ObservableProperty] private string _giftCardCode = "";

    // The "Send prompt" sub-flow, shown alongside (never instead of) the manual
    // confirmation-code field above — whichever fills in TransactionRef first is
    // what lets the dialog submit.
    [ObservableProperty] private string _stkPhoneNumber = "";
    [ObservableProperty] private bool _isSendingStk;
    [NotifyPropertyChangedFor(nameof(HasStkStatus))]
    [ObservableProperty] private string? _stkStatusText;

    public bool HasStkStatus => !string.IsNullOrEmpty(StkStatusText);

    public decimal Amount => decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;
    public bool IsMpesa => Method == Pos.Core.Sync.OfflinePaymentMethod.Mpesa;
    public bool IsCardOrOther => Method == Pos.Core.Sync.OfflinePaymentMethod.Card || Method == Pos.Core.Sync.OfflinePaymentMethod.Other;
    public bool IsMpesaWithStk => IsMpesa && _showStkOption;
    public bool IsGiftCard => Method == Pos.Core.Sync.OfflinePaymentMethod.GiftCard;
    public bool IsStoreCredit => Method == Pos.Core.Sync.OfflinePaymentMethod.StoreCredit;
}

/// <summary>
/// Payment entry, ported from <c>PaymentModal.jsx</c>: split tenders, quick-cash buttons,
/// submit once the tendered total covers the sale. Gift card and store credit are only
/// ever offered while <see cref="IsOnline"/> (same gating M-Pesa STK push already uses) —
/// not because this dialog talks to the server for them (it doesn't; they queue through
/// the same offline-sync outbox as every other tender), but because redeeming either one
/// needs a fresh, in-transaction balance check the server can only do once the sale
/// actually syncs, and that should happen promptly, not sit queued indefinitely.
/// </summary>
public sealed partial class PaymentDialogViewModel : ViewModelBase
{
    // Instance property, not static: Avalonia's {Binding} only resolves against the
    // DataContext instance, not static members (those need {x:Static}) — simpler to
    // make this an instance property than to switch every binding site to x:Static.
    public string[] Methods { get; }

    private readonly ApiClient? _api;
    private readonly string? _deviceToken;

    public bool IsOnline { get; }
    public string? CustomerId { get; }

    /// <summary>Informational only, same as the web cashier's cart-pane display — not
    /// enforced client-side as a hard cap. The server is the actual authority on the
    /// customer's current balance at the moment the sale syncs.</summary>
    public decimal? CustomerStoreCreditBalance { get; }
    public bool ShowStoreCreditBalance => CustomerStoreCreditBalance is > 0;

    // STK push needs a live round-trip to the server — unlike every other tender
    // here, which never leaves the local SQLite write path (see SaleService). Only
    // offered when both a credential is configured and the terminal is online;
    // otherwise the manual confirmation-code field is the only option, same as
    // before this existed.
    public PaymentDialogViewModel(decimal total, string? mpesaType = null, string? mpesaShortcode = null,
        bool mpesaStkConfigured = false, ApiClient? api = null, string? deviceToken = null, bool isOnline = false,
        string? customerId = null, decimal? customerStoreCreditBalance = null)
    {
        Total = total;
        MpesaShortcode = mpesaShortcode;
        MpesaHint = string.IsNullOrWhiteSpace(mpesaShortcode)
            ? "M-Pesa isn't set up yet — add a till/paybill number in Business Settings."
            : $"Have the customer pay {(mpesaType == "PAYBILL" ? "Paybill" : "Till")} number {mpesaShortcode}, then enter the confirmation code below.";
        ShowStkOption = mpesaStkConfigured && isOnline;
        IsOnline = isOnline;
        CustomerId = customerId;
        CustomerStoreCreditBalance = customerStoreCreditBalance;
        _api = api;
        _deviceToken = deviceToken;

        Methods = isOnline
            ? [OfflinePaymentMethod.Cash, OfflinePaymentMethod.Card, OfflinePaymentMethod.Mpesa, OfflinePaymentMethod.GiftCard, OfflinePaymentMethod.StoreCredit, OfflinePaymentMethod.Other]
            : [OfflinePaymentMethod.Cash, OfflinePaymentMethod.Card, OfflinePaymentMethod.Mpesa, OfflinePaymentMethod.Other];

        AddLineInternal(OfflinePaymentMethod.Cash, total);
    }

    public string? MpesaShortcode { get; }
    public string MpesaHint { get; }
    public bool ShowStkOption { get; }

    private void NotifyTotalsChanged()
    {
        OnPropertyChanged(nameof(Tendered));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(HasMpesaLine));
    }

    public bool HasMpesaLine => Lines.Any(l => l.Method == OfflinePaymentMethod.Mpesa);

    private void AddLineInternal(string method, decimal amount)
    {
        var line = new TenderLineViewModel(method, amount, ShowStkOption);
        line.PropertyChanged += (_, _) => NotifyTotalsChanged();
        Lines.Add(line);
        NotifyTotalsChanged();
    }

    public decimal Total { get; }
    public ObservableCollection<TenderLineViewModel> Lines { get; } = [];

    public decimal Tendered => Lines.Sum(l => l.Amount);
    public decimal Remaining => Math.Round(Total - Tendered, 2);
    public bool CanSubmit => Remaining <= 0.001m && Lines.Count > 0 &&
        Lines.All(l => l.Amount > 0
            && (l.Method != OfflinePaymentMethod.Mpesa || !string.IsNullOrWhiteSpace(l.TransactionRef))
            && (l.Method != OfflinePaymentMethod.GiftCard || !string.IsNullOrWhiteSpace(l.GiftCardCode))
            && (l.Method != OfflinePaymentMethod.StoreCredit || !string.IsNullOrWhiteSpace(CustomerId)));

    public IReadOnlyList<OfflineOrderPayment>? Result { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    [RelayCommand]
    private void AddLine() => AddLineInternal(OfflinePaymentMethod.Cash, Math.Max(Remaining, 0));

    [RelayCommand]
    private void RemoveLine(TenderLineViewModel line)
    {
        if (Lines.Count <= 1) return;
        Lines.Remove(line);
        NotifyTotalsChanged();
    }

    [RelayCommand]
    private void QuickCash(string amountText)
    {
        var bump = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var last = Lines.LastOrDefault();
        if (last is not null && last.Method == OfflinePaymentMethod.Cash)
        {
            last.AmountText = (last.Amount + bump).ToString("0.00", CultureInfo.InvariantCulture);
            NotifyTotalsChanged();
        }
        else
        {
            AddLineInternal(OfflinePaymentMethod.Cash, bump);
        }
    }

    [RelayCommand]
    private void Submit()
    {
        if (!CanSubmit) return;
        Result = [.. Lines.Select(l => new OfflineOrderPayment
        {
            Method = l.Method,
            Amount = l.Amount,
            TransactionRef = string.IsNullOrWhiteSpace(l.TransactionRef) ? null : l.TransactionRef,
            GiftCardCode = string.IsNullOrWhiteSpace(l.GiftCardCode) ? null : l.GiftCardCode,
        })];
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    private static readonly Regex KenyanPhoneRegex = new(@"^254[17]\d{8}$", RegexOptions.Compiled);

    // Resolves to a TransactionRef, same as if the cashier had typed one in — once
    // this fills it, CanSubmit's check ("TransactionRef is non-blank") is satisfied
    // exactly like manual entry, no separate state to track there. Bounded by a
    // wall-clock deadline rather than CancellationToken-driven cancellation, so a
    // plain timeout and a real connection error are distinguishable in the catch
    // below instead of both throwing the same way.
    [RelayCommand]
    private async Task SendStkPushAsync(TenderLineViewModel line)
    {
        line.StkStatusText = null;
        if (_api is null || _deviceToken is null) return;

        if (!KenyanPhoneRegex.IsMatch(line.StkPhoneNumber.Trim()))
        {
            line.StkStatusText = "Enter the phone number as 2547XXXXXXXX or 2541XXXXXXXX";
            return;
        }

        line.IsSendingStk = true;
        try
        {
            var intent = await _api.CreatePaymentIntentAsync(_deviceToken, line.Amount, line.StkPhoneNumber.Trim());
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                var status = await _api.GetPaymentIntentAsync(_deviceToken, intent.Id);
                if (status.Status == "COMPLETED")
                {
                    line.TransactionRef = status.TransactionRef ?? "";
                    return;
                }
                if (status.Status is "FAILED" or "CANCELLED" or "TIMEOUT")
                {
                    line.StkStatusText = status.ResultDesc ?? "The M-Pesa prompt was not completed — try again or enter the code manually.";
                    return;
                }
            }
            line.StkStatusText = "Taking longer than expected — try again or enter the code manually.";
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            line.StkStatusText = $"Couldn't reach the server: {ex.Message}";
        }
        finally
        {
            line.IsSendingStk = false;
        }
    }
}
