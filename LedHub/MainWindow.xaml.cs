using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LedHub.ViewModels;

namespace LedHub;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropMica = 2;
    private const int BackdropAcrylic = 3;

    /// <summary>
    /// Ширина, уже которой меню разделов сворачивается до значков.
    /// </summary>
    private const double CompactNavWidth = 1100;

    /// <summary>
    /// Ширина, уже которой список устройств и управление лентой показываются по очереди.
    /// </summary>
    private const double NarrowWidth = 860;

    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Closed += (_, _) => _viewModel.Shutdown();
        SourceInitialized += (_, _) => ApplyBackdrop();
        SizeChanged += (_, _) => UpdateAdaptiveLayout();
        StateChanged += (_, _) => UpdateMaximizedMargin();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Ленты ищутся сразу при запуске, чтобы не нажимать «Найти устройства» каждый раз
        Loaded += async (_, _) =>
        {
            if (_viewModel.ScanOnStartup)
                await _viewModel.ScanCommand.ExecuteAsync(null);
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.UseAcrylic))
            ApplyBackdrop();
    }

    /// <summary>
    /// Включает тёмный заголовок и системное стекло под всем окном; в старых Windows окно остаётся просто тёмным.
    /// </summary>
    private void ApplyBackdrop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        var dark = 1;

        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        if (!_viewModel.IsAcrylicSupported || source is null)
        {
            Background = (Brush)FindResource("WindowBrush");
            return;
        }

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var backdrop = _viewModel.UseAcrylic ? BackdropAcrylic : BackdropMica;

        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        DwmExtendFrameIntoClientArea(handle, ref margins);
        DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));
    }

    private void UpdateAdaptiveLayout()
    {
        _viewModel.IsNavCompact = ActualWidth < CompactNavWidth;
        _viewModel.IsNarrow = ActualWidth < NarrowWidth;
    }

    /// <summary>
    /// Развёрнутое окно со своей рамкой вылезает за край экрана на толщину рамки — отступ возвращает содержимое на экран.
    /// </summary>
    private void UpdateMaximizedMargin()
    {
        var frame = SystemParameters.WindowResizeBorderThickness;

        Root.Margin = WindowState == WindowState.Maximized
            ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4)
            : new Thickness(0);
    }
}
