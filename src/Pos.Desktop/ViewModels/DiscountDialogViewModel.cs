using System.Globalization;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Pricing;
using Pos.Infra.Api;

namespace Pos.Desktop.ViewModels;


public sealed partial class DiscountDialogViewModel : ViewModelBase
{
    private readonly ApiClient? _api;
    private readonly string? _deviceToken;
    private readonly decimal _subtotal;
    private readonly int _decimalDigits;
    private readonly string _moneyFormat;

    public DiscountDialogViewModel(string role, decimal? currentAmount, string? currentReason, decimal subtotal = 0,
        ApiClient? api = null, string? deviceToken = null, bool isOnline = false, string currency = "USD")
    {
        CanApplyDiscount = role is "OWNER" or "MANAGER";
        IsOnline = isOnline;
        _api = api;
        _deviceToken = deviceToken;
        _subtotal = subtotal;
        _decimalDigits = CurrencyInfo.GetDecimalDigits(currency);
        _moneyFormat = _decimalDigits == 0 ? "0" : "0." + new string('0', _decimalDigits);
        if (currentAmount.HasValue) AmountText = currentAmount.Value.ToString(_moneyFormat, CultureInfo.InvariantCulture);
        Reason = currentReason ?? "";
    }

    public bool CanApplyDiscount { get; }
    public bool IsOnline { get; }

    [ObservableProperty] private bool _usePromoCode;
    [ObservableProperty] private string _promoCodeText = "";
    [ObservableProperty] private string? _promoPreviewLabel;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private string? _errorMessage;

    private string? _appliedPromotionCode;

    public decimal? ResultAmount { get; private set; }
    public string? ResultReason { get; private set; }
    public string? ResultPromotionCode { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    [RelayCommand]
    private void ShowPromoMode() => UsePromoCode = true;

    [RelayCommand]
    private void ShowManualMode() => UsePromoCode = false;

    [RelayCommand]
    private async Task LookupPromoAsync()
    {
        ErrorMessage = null;
        PromoPreviewLabel = null;
        _appliedPromotionCode = null;

        var code = PromoCodeText.Trim();
        if (code.Length == 0)
        {
            ErrorMessage = "Enter a promo code.";
            return;
        }
        if (_api is null || _deviceToken is null || !IsOnline)
        {
            ErrorMessage = "Connect to the network to look up a promo code.";
            return;
        }

        IsBusy = true;
        try
        {
            var promo = await _api.GetPromotionByCodeAsync(_deviceToken, code);
            _appliedPromotionCode = promo.Code;

            if (promo.MinSubtotal.HasValue && _subtotal < promo.MinSubtotal.Value)
            {
                ErrorMessage = $"This promotion requires a subtotal of at least {promo.MinSubtotal.Value.ToString(_moneyFormat, CultureInfo.InvariantCulture)}.";
                _appliedPromotionCode = null;
                return;
            }

            switch (promo.Type)
            {
                case "PERCENTAGE":
                    AmountText = Money.Round(_subtotal * (promo.Value / 100m), _decimalDigits).ToString(_moneyFormat, CultureInfo.InvariantCulture);
                    PromoPreviewLabel = $"{promo.Name} — {promo.Value:0.##}% off";
                    break;
                case "FIXED_AMOUNT":
                    AmountText = promo.Value.ToString(_moneyFormat, CultureInfo.InvariantCulture);
                    PromoPreviewLabel = $"{promo.Name} — {promo.Value.ToString(_moneyFormat, CultureInfo.InvariantCulture)} off";
                    break;
                default: // BOGO — same rule the server enforces: no computed amount, the cashier must type one.
                    PromoPreviewLabel = $"{promo.Name} — enter the discount amount this BOGO works out to.";
                    break;
            }
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            ErrorMessage = $"Couldn't look up that code: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Apply()
    {
        if (!CanApplyDiscount)
        {
            ErrorMessage = "Only a manager can apply a discount.";
            return;
        }
        if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ErrorMessage = "Enter an amount greater than zero.";
            return;
        }
        ResultAmount = amount;
        ResultReason = string.IsNullOrWhiteSpace(Reason) ? null : Reason;

        ResultPromotionCode = UsePromoCode ? _appliedPromotionCode : null;
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Remove()
    {
        ResultAmount = 0;
        ResultReason = null;
        ResultPromotionCode = null;
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    partial void OnPromoCodeTextChanged(string value) => _appliedPromotionCode = null;
}
