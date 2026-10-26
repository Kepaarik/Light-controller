using System.Windows;
using LedHub.ViewModels;

namespace LedHub;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Shutdown();
    }
}
