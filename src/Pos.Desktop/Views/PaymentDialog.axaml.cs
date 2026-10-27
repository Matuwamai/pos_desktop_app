using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class PaymentDialog : Window
{
    public PaymentDialog() => InitializeComponent();

    public PaymentDialog(PaymentDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = _ => Close();
    }
}
