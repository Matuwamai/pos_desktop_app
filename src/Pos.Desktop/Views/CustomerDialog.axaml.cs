using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class CustomerDialog : Window
{
    public CustomerDialog() => InitializeComponent();

    public CustomerDialog(CustomerDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = applied => Close(applied);
    }
}
