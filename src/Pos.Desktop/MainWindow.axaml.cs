using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    // Exposed so CashierView's code-behind (reached via TopLevel.GetTopLevel(this), the
    // same way it already resolves its dialog owner window) can call back into
    // AppViewModel.HandleDeviceRevokedAsync without CashierViewModel itself needing a
    // reference to it.
    public AppViewModel AppViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        AppViewModel = new AppViewModel();
        DataContext = AppViewModel;
        _ = AppViewModel.InitializeAsync();
    }
}
