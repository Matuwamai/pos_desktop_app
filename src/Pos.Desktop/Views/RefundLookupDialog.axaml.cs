using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class RefundLookupDialog : Window
{
    public RefundLookupDialog() => InitializeComponent();

    public RefundLookupDialog(RefundLookupDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = succeeded => Close(succeeded);
    }
}
