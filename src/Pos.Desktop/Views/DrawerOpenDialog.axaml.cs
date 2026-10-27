using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class DrawerOpenDialog : Window
{
    public DrawerOpenDialog() => InitializeComponent();

    public DrawerOpenDialog(DrawerOpenDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = _ => Close();
    }
}
