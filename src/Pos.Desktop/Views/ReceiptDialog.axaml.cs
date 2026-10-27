using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class ReceiptDialog : Window
{
    public ReceiptDialog() => InitializeComponent();

    public ReceiptDialog(ReceiptDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = () => Close();
    }
}
