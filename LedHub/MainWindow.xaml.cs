using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LedHub.ViewModels;

namespace LedHub;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;

    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Shutdown();
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    private void LedCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PaintLed(sender);
    }

    /// <summary>
    /// Закрашивает светодиоды, по которым ведут мышью с зажатой кнопкой.
    /// </summary>
    private void LedCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            PaintLed(sender);
    }

    private void PaintLed(object sender)
    {
        if (sender is FrameworkElement { DataContext: LedCellViewModel cell } && DataContext is MainViewModel viewModel)
            viewModel.PaintLedCommand.Execute(cell);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Включает тёмный системный заголовок окна, чтобы он не выбивался из тёмной темы.
    /// </summary>
    private void UseDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;

        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
