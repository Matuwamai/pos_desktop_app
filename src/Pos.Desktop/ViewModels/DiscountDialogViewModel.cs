using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// Manual discount entry, gated on role exactly like the server (<c>services/order.js</c>:
/// only OWNER/MANAGER may apply a manual discount) — replicated client-side here because
/// there is no server round-trip to catch it while offline. No promo-code tab: resolving
/// a code is explicitly out of scope for this pass (see <c>PricingMath.DiscountInput</c>'s
/// doc comment and the Phase 2 plan's decision #3/#5).
/// </summary>
public sealed partial class DiscountDialogViewModel : ViewModelBase
{
    public DiscountDialogViewModel(string role, decimal? currentAmount, string? currentReason)
    {
        CanApplyDiscount = role is "OWNER" or "MANAGER";
        if (currentAmount.HasValue) AmountText = currentAmount.Value.ToString("0.00", CultureInfo.InvariantCulture);
        Reason = currentReason ?? "";
    }

    public bool CanApplyDiscount { get; }

    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private string? _errorMessage;

    public decimal? ResultAmount { get; private set; }
    public string? ResultReason { get; private set; }
    public Action<bool>? RequestClose { get; set; }

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
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Remove()
    {
        ResultAmount = 0;
        ResultReason = null;
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
