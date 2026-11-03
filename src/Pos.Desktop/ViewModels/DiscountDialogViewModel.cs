using System.Globalization;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Pricing;
using Pos.Infra.Api;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// Manual discount entry, gated on role exactly like the server (<c>services/order.js</c>:
/// only OWNER/MANAGER may apply a manual discount) — replicated client-side here because
/// there is no server round-trip to catch it while offline. A promo-code mode previews the
/// discount live via <see cref="ApiClient.GetPromotionByCodeAsync"/> — only offered while
/// <see cref="IsOnline"/>, same gating M-Pesa STK push already uses, since the promotion's
/// active/date-window state is the server's to decide, not a stale local read. Either way
/// the server re-validates the promotion again at sync time (<c>resolveDiscountInput</c>) —
/// this preview is for the cashier's benefit, not the actual authority.
/// </summary>
public sealed partial class DiscountDialogViewModel : ViewModelBase
{
    private readonly ApiClient? _api;
    private readonly string? _deviceToken;
    private readonly decimal _subtotal;

    public DiscountDialogViewModel(string role, decimal? currentAmount, string? currentReason, decimal subtotal = 0,
        ApiClient? api = null, string? deviceToken = null, bool isOnline = false)
    {
        CanApplyDiscount = role is "OWNER" or "MANAGER";
        IsOnline = isOnline;
        _api = api;
        _deviceToken = deviceToken;
        _subtotal = subtotal;
        if (currentAmount.HasValue) AmountText = currentAmount.Value.ToString("0.00", CultureInfo.InvariantCulture);
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
                ErrorMessage = $"This promotion requires a subtotal of at least {promo.MinSubtotal.Value:0.00}.";
                _appliedPromotionCode = null;
                return;
            }

            switch (promo.Type)
            {
                case "PERCENTAGE":
                    AmountText = Money.Round2(_subtotal * (promo.Value / 100m)).ToString("0.00", CultureInfo.InvariantCulture);
                    PromoPreviewLabel = $"{promo.Name} — {promo.Value:0.##}% off";
                    break;
                case "FIXED_AMOUNT":
                    AmountText = promo.Value.ToString("0.00", CultureInfo.InvariantCulture);
                    PromoPreviewLabel = $"{promo.Name} — {promo.Value:0.00} off";
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
        // Only carried through if the amount on screen is still the one that code resolved
        // to — editing the code text after a lookup (without looking it up again) clears it,
        // same as web_app's "apply" always reflecting the last successful lookup.
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
