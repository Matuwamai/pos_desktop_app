using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class PrinterSettingsDialog : Window
{
    public PrinterSettingsDialog() => InitializeComponent();

    public PrinterSettingsDialog(PrinterSettingsDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = applied => Close(applied);
    }
}
