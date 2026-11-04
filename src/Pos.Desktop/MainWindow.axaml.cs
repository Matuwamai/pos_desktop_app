using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    public AppViewModel AppViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        AppViewModel = new AppViewModel();
        DataContext = AppViewModel;
        _ = AppViewModel.InitializeAsync();
    }
}
