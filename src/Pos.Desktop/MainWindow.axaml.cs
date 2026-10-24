using Avalonia.Controls;
using Pos.Desktop.ViewModels;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new AppViewModel();
        DataContext = vm;
        _ = vm.InitializeAsync();
    }
}
