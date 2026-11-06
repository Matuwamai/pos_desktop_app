using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Pricing;
using Pos.Core.Sync;
using Pos.Infra.Api;

namespace Pos.Desktop.ViewModels;

public sealed partial class TenderLineViewModel : ViewModelBase
{
    private readonly bool _showStkOption;
    private readonly bool _showStripeOption;

    public TenderLineViewModel(string method, decimal amount, bool showStkOption = false, string moneyFormat = "0.00", bool showStripeOption = false)
    {
        Method = method;
        AmountText = amount > 0 ? amount.ToString(moneyFormat, CultureInfo.InvariantCulture) : "";
        _showStkOption = showStkOption;
        _showStripeOption = showStripeOption;
    }

    [NotifyPropertyChangedFor(nameof(IsMpesa))]
    [NotifyPropertyChangedFor(nameof(IsCardOrOther))]
    [NotifyPropertyChangedFor(nameof(IsMpesaWithStk))]
    [NotifyPropertyChangedFor(nameof(IsStripeWithCheckout))]
    [NotifyPropertyChangedFor(nameof(IsStripePaid))]
    [NotifyPropertyChangedFor(nameof(IsGiftCard))]
    [NotifyPropertyChangedFor(nameof(IsStoreCredit))]
    [ObservableProperty] private string _method;
    [ObservableProperty] private string _amountText = "";
    [NotifyPropertyChangedFor(nameof(IsStripePaid))]
    [ObservableProperty] private string _transactionRef = "";
    [ObservableProperty] private string _giftCardCode = "";

    [ObservableProperty] private string _stkPhoneNumber = "";
    [ObservableProperty] private bool _isSendingStk;
    [NotifyPropertyChangedFor(nameof(HasStkStatus))]
    [ObservableProperty] private string? _stkStatusText;

    // Stripe "scan to pay" — unlike M-Pesa above, there's no manual fallback (the server
    // only accepts a transactionRef that matches a real webhook-confirmed intent), so
    // once an intent is created the only path to CanSubmit is this flow completing.
    [ObservableProperty] private bool _isCreatingStripeIntent;
    [NotifyPropertyChangedFor(nameof(HasStripeStatus))]
    [ObservableProperty] private string? _stripeStatusText;
    [NotifyPropertyChangedFor(nameof(StripeQrCodeBitmap))]
    [ObservableProperty] private string? _stripeQrCodeDataUrl;
    [ObservableProperty] private string? _stripeCheckoutUrl;

    public bool HasStkStatus => !string.IsNullOrEmpty(StkStatusText);
    public bool HasStripeStatus => !string.IsNullOrEmpty(StripeStatusText);

    // Avalonia's Image control needs an IImage/Bitmap, not a data-URL string — decoded
    // here (rather than a shared IValueConverter) for the same reason this codebase
    // already exposes pre-formatted *Text properties instead of converters: a plain
    // converter has no natural place to cache work tied to this line's own state.
    public Bitmap? StripeQrCodeBitmap
    {
        get
        {
            if (string.IsNullOrEmpty(StripeQrCodeDataUrl)) return null;
            var commaIndex = StripeQrCodeDataUrl.IndexOf(',');
            if (commaIndex < 0) return null;
            try
            {
                var bytes = Convert.FromBase64String(StripeQrCodeDataUrl[(commaIndex + 1)..]);
                return new Bitmap(new MemoryStream(bytes));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    public decimal Amount => decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;
    public bool IsMpesa => Method == Pos.Core.Sync.OfflinePaymentMethod.Mpesa;
    public bool IsCardOrOther => Method == Pos.Core.Sync.OfflinePaymentMethod.Card || Method == Pos.Core.Sync.OfflinePaymentMethod.Other;
    public bool IsMpesaWithStk => IsMpesa && _showStkOption;
    public bool IsStripe => Method == Pos.Core.Sync.OfflinePaymentMethod.Stripe;
    public bool IsStripeWithCheckout => IsStripe && _showStripeOption;
    public bool IsStripePaid => IsStripe && !string.IsNullOrWhiteSpace(TransactionRef);
    public bool IsGiftCard => Method == Pos.Core.Sync.OfflinePaymentMethod.GiftCard;
    public bool IsStoreCredit => Method == Pos.Core.Sync.OfflinePaymentMethod.StoreCredit;
}


public sealed partial class PaymentDialogViewModel : ViewModelBase
{
    public string[] Methods { get; }

    private readonly ApiClient? _api;
    private readonly string? _deviceToken;
    private readonly int _decimalDigits;
    private readonly string _moneyFormat;

    public bool IsOnline { get; }
    public string? CustomerId { get; }
    public string Currency { get; }

    public decimal? CustomerStoreCreditBalance { get; }
    public bool ShowStoreCreditBalance => CustomerStoreCreditBalance is > 0;
    public string? CustomerStoreCreditBalanceText => CustomerStoreCreditBalance?.ToString(_moneyFormat, CultureInfo.InvariantCulture);


    public PaymentDialogViewModel(decimal total, string? mpesaType = null, string? mpesaShortcode = null,
        bool mpesaStkConfigured = false, ApiClient? api = null, string? deviceToken = null, bool isOnline = false,
        string? customerId = null, decimal? customerStoreCreditBalance = null, string currency = "USD",
        bool stripeConfigured = false)
    {
        Currency = currency;
        _decimalDigits = CurrencyInfo.GetDecimalDigits(currency);
        _moneyFormat = _decimalDigits == 0 ? "0" : "0." + new string('0', _decimalDigits);
        Total = total;
        MpesaShortcode = mpesaShortcode;
        MpesaHint = string.IsNullOrWhiteSpace(mpesaShortcode)
            ? "M-Pesa isn't set up yet — add a till/paybill number in Business Settings."
            : $"Have the customer pay {(mpesaType == "PAYBILL" ? "Paybill" : "Till")} number {mpesaShortcode}, then enter the confirmation code below.";
        ShowStkOption = mpesaStkConfigured && isOnline;
        // Unlike M-Pesa above, there's no manual-entry fallback for Stripe, so it's only
        // worth offering as a tender option at all once there's a real way to pay it.
        ShowStripeOption = stripeConfigured && isOnline;
        IsOnline = isOnline;
        CustomerId = customerId;
        CustomerStoreCreditBalance = customerStoreCreditBalance;
        _api = api;
        _deviceToken = deviceToken;

        List<string> onlineMethods = [OfflinePaymentMethod.Cash, OfflinePaymentMethod.Card, OfflinePaymentMethod.Mpesa];
        if (ShowStripeOption) onlineMethods.Add(OfflinePaymentMethod.Stripe);
        onlineMethods.AddRange([OfflinePaymentMethod.GiftCard, OfflinePaymentMethod.StoreCredit, OfflinePaymentMethod.Other]);

        Methods = isOnline
            ? [.. onlineMethods]
            : [OfflinePaymentMethod.Cash, OfflinePaymentMethod.Card, OfflinePaymentMethod.Mpesa, OfflinePaymentMethod.Other];

        AddLineInternal(OfflinePaymentMethod.Cash, total);
    }

    public string? MpesaShortcode { get; }
    public string MpesaHint { get; }
    public bool ShowStkOption { get; }
    public bool ShowStripeOption { get; }

    private void NotifyTotalsChanged()
    {
        OnPropertyChanged(nameof(Tendered));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(HasMpesaLine));
    }

    public bool HasMpesaLine => Lines.Any(l => l.Method == OfflinePaymentMethod.Mpesa);

    private void AddLineInternal(string method, decimal amount)
    {
        var line = new TenderLineViewModel(method, amount, ShowStkOption, _moneyFormat, ShowStripeOption);
        line.PropertyChanged += (_, _) => NotifyTotalsChanged();
        Lines.Add(line);
        NotifyTotalsChanged();
    }

    public decimal Total { get; }
    public string TotalText => Total.ToString(_moneyFormat, CultureInfo.InvariantCulture);
    public ObservableCollection<TenderLineViewModel> Lines { get; } = [];

    public decimal Tendered => Lines.Sum(l => l.Amount);
    public decimal Remaining => Money.Round(Total - Tendered, _decimalDigits);
    public string RemainingText => Remaining.ToString(_moneyFormat, CultureInfo.InvariantCulture);
    public bool CanSubmit => Remaining <= 0.001m && Lines.Count > 0 &&
        Lines.All(l => l.Amount > 0
            && (l.Method != OfflinePaymentMethod.Mpesa || !string.IsNullOrWhiteSpace(l.TransactionRef))
            && (l.Method != OfflinePaymentMethod.Stripe || !string.IsNullOrWhiteSpace(l.TransactionRef))
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
            last.AmountText = (last.Amount + bump).ToString(_moneyFormat, CultureInfo.InvariantCulture);
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

    [RelayCommand]
    private async Task CreateStripeIntentAsync(TenderLineViewModel line)
    {
        line.StripeStatusText = null;
        line.StripeQrCodeDataUrl = null;
        line.StripeCheckoutUrl = null;
        if (_api is null || _deviceToken is null) return;

        line.IsCreatingStripeIntent = true;
        try
        {
            var intent = await _api.CreateStripePaymentIntentAsync(_deviceToken, line.Amount);
            line.StripeQrCodeDataUrl = intent.QrCodeDataUrl;
            line.StripeCheckoutUrl = intent.CheckoutUrl;

            var deadline = DateTime.UtcNow.AddMinutes(10); // mirrors the server's own intent expiry (services/stripePaymentIntent.js)
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                var status = await _api.GetStripePaymentIntentAsync(_deviceToken, intent.Id);
                if (status.Status == "COMPLETED")
                {
                    line.TransactionRef = status.TransactionRef ?? "";
                    line.StripeQrCodeDataUrl = null;
                    line.StripeCheckoutUrl = null;
                    return;
                }
                if (status.Status is "FAILED" or "CANCELLED" or "TIMEOUT")
                {
                    line.StripeStatusText = "The Stripe payment was not completed in time — try again.";
                    line.StripeQrCodeDataUrl = null;
                    line.StripeCheckoutUrl = null;
                    return;
                }
                // The QR/URL don't change between polls, but re-assigning keeps the dialog
                // showing them for as long as the line stays PENDING.
                line.StripeQrCodeDataUrl = status.QrCodeDataUrl ?? line.StripeQrCodeDataUrl;
                line.StripeCheckoutUrl = status.CheckoutUrl ?? line.StripeCheckoutUrl;
            }
            line.StripeStatusText = "Taking longer than expected — try again.";
            line.StripeQrCodeDataUrl = null;
            line.StripeCheckoutUrl = null;
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            line.StripeStatusText = $"Couldn't reach the server: {ex.Message}";
            line.StripeQrCodeDataUrl = null;
            line.StripeCheckoutUrl = null;
        }
        finally
        {
            line.IsCreatingStripeIntent = false;
        }
    }
}
