using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class DiscountDialog : Window
{
    public DiscountDialog() => InitializeComponent();

    public DiscountDialog(DiscountDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = applied => Close(applied);
    }
}
