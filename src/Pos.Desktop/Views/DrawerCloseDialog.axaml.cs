using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class DrawerCloseDialog : Window
{
    public DrawerCloseDialog() => InitializeComponent();

    public DrawerCloseDialog(DrawerCloseDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = confirmed => Close(confirmed);
    }
}
