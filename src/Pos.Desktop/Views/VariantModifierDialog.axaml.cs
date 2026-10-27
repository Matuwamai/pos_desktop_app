using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop.Views;

public partial class VariantModifierDialog : Window
{
    public VariantModifierDialog() => InitializeComponent();

    public VariantModifierDialog(VariantModifierDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose = _ => Close();
    }
}
