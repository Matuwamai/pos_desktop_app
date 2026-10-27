using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Sync;

namespace Pos.Desktop.ViewModels;

/// <summary>One tender line. Gift card / store credit aren't offered here — see the
/// Phase 2 plan's decision #5: those (and real card/mobile-money charging) are
/// deferred, not wired at all this pass. <c>CARD</c>/<c>OTHER</c> just record a
/// reference, matching what <c>OfflinePaymentMethod</c> already allows offline.</summary>
public sealed partial class TenderLineViewModel : ViewModelBase
{
    public TenderLineViewModel(string method, decimal amount)
    {
        Method = method;
        AmountText = amount > 0 ? amount.ToString("0.00", CultureInfo.InvariantCulture) : "";
    }

    [NotifyPropertyChangedFor(nameof(IsMpesa))]
    [NotifyPropertyChangedFor(nameof(IsCardOrOther))]
    [ObservableProperty] private string _method;
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private string _transactionRef = "";

    public decimal Amount => decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;
    public bool IsMpesa => Method == Pos.Core.Sync.OfflinePaymentMethod.Mpesa;
    public bool IsCardOrOther => Method == Pos.Core.Sync.OfflinePaymentMethod.Card || Method == Pos.Core.Sync.OfflinePaymentMethod.Other;
}

/// <summary>
/// Payment entry, ported from <c>PaymentModal.jsx</c>: split tenders, quick-cash buttons,
/// submit once the tendered total covers the sale. The methods offered
/// (<see cref="Methods"/>) are deliberately narrower than the web app's — see
/// <see cref="TenderLineViewModel"/>'s doc comment.
/// </summary>
public sealed partial class PaymentDialogViewModel : ViewModelBase
{
    // Instance property, not static: Avalonia's {Binding} only resolves against the
    // DataContext instance, not static members (those need {x:Static}) — simpler to
    // make this an instance property than to switch every binding site to x:Static.
    public string[] Methods { get; } =
        [OfflinePaymentMethod.Cash, OfflinePaymentMethod.Card, OfflinePaymentMethod.Mpesa, OfflinePaymentMethod.Other];

    public PaymentDialogViewModel(decimal total, string? mpesaType = null, string? mpesaShortcode = null)
    {
        Total = total;
        MpesaShortcode = mpesaShortcode;
        MpesaHint = string.IsNullOrWhiteSpace(mpesaShortcode)
            ? "M-Pesa isn't set up yet — add a till/paybill number in Business Settings."
            : $"Have the customer pay {(mpesaType == "PAYBILL" ? "Paybill" : "Till")} number {mpesaShortcode}, then enter the confirmation code below.";
        AddLineInternal(OfflinePaymentMethod.Cash, total);
    }

    public string? MpesaShortcode { get; }
    public string MpesaHint { get; }

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
        var line = new TenderLineViewModel(method, amount);
        line.PropertyChanged += (_, _) => NotifyTotalsChanged();
        Lines.Add(line);
        NotifyTotalsChanged();
    }

    public decimal Total { get; }
    public ObservableCollection<TenderLineViewModel> Lines { get; } = [];

    public decimal Tendered => Lines.Sum(l => l.Amount);
    public decimal Remaining => Math.Round(Total - Tendered, 2);
    public bool CanSubmit => Remaining <= 0.001m && Lines.Count > 0 &&
        Lines.All(l => l.Amount > 0 && (l.Method != OfflinePaymentMethod.Mpesa || !string.IsNullOrWhiteSpace(l.TransactionRef)));

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
        })];
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
