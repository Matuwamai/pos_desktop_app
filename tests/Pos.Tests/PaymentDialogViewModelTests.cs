using Pos.Core.Sync;
using Pos.Desktop.ViewModels;

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
}
