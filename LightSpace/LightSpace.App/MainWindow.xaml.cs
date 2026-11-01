using System.ComponentModel;
using System.Windows;
using LightSpace.App.Services;
using LightSpace.App.ViewModels;

namespace LightSpace.App;

/// <summary>
/// Главное окно приложения, как в LedHub: системное стекло под всем окном, меню разделов слева и страницы справа.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Ширина, уже которой меню разделов сворачивается до значков.
    /// </summary>
    private const double CompactNavWidth = 1100;

    private const double NavWidth = 232;
    private const double CompactNavItemWidth = 68;

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        Dialogs.Host = Modal;

        SourceInitialized += (_, _) => ApplyBackdrop();
        SizeChanged += (_, _) => UpdateAdaptiveLayout();
        StateChanged += (_, _) => UpdateMaximizedMargin();
        StateChanged += (_, _) => UpdateShown();
        IsVisibleChanged += (_, _) => UpdateShown();
        _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
        Closed += (_, _) => _viewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.UseAcrylic))
            ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        WindowBackdrop.Apply(this, _viewModel.Settings.UseAcrylic);
    }

    /// <summary>
    /// В нешироком окне меню разделов сворачивается до значков.
    /// </summary>
    private void UpdateAdaptiveLayout()
    {
        var compact = ActualWidth < CompactNavWidth;

        Nav.Width = compact ? CompactNavItemWidth : NavWidth;
        NavList.Tag = compact;
    }

    /// <summary>
    /// Окно спрятано в трей или свёрнуто — живые цвета и превью не нужны.
    /// </summary>
    private void UpdateShown()
    {
        _viewModel.SetWindowShown(IsVisible && WindowState != WindowState.Minimized);
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
