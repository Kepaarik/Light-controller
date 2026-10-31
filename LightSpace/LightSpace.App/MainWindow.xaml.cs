using System.Windows;
using System.Windows.Input;
using LightSpace.App.Services;
using LightSpace.App.ViewModels;

namespace LightSpace.App;

/// <summary>
/// Главное окно приложения: свой заголовок с вкладками на фоне Mica и страницы под ним.
/// </summary>
public partial class MainWindow : Window
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        Dialogs.Host = Modal;

        SourceInitialized += (_, _) => WindowBackdrop.Apply(this, ThemeService.IsDark);
        StateChanged += (_, _) => UpdateMaximizedState();
        StateChanged += (_, _) => UpdateShown();
        IsVisibleChanged += (_, _) => UpdateShown();
        ThemeService.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;

        AddShowcase();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        WindowBackdrop.Apply(this, ThemeService.IsDark);
    }

    /// <summary>
    /// Окно спрятано в трей или свёрнуто — живые цвета и превью не нужны.
    /// </summary>
    private void UpdateShown()
    {
        _viewModel.SetWindowShown(IsVisible && WindowState != WindowState.Minimized);
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Развёрнутое окно со своей рамкой вылезает за край экрана на толщину рамки — отступ возвращает содержимое на экран.
    /// </summary>
    private void UpdateMaximizedState()
    {
        var maximized = WindowState == WindowState.Maximized;
        var frame = SystemParameters.WindowResizeBorderThickness;

        Root.Margin = maximized
            ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4)
            : new Thickness(0);

        MaximizeButton.Content = maximized ? RestoreGlyph : MaximizeGlyph;
        MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
    }

    /// <summary>
    /// В отладочной сборке добавляет скрытую страницу «Витрина», она открывается по Ctrl+Shift+D.
    /// </summary>
    private void AddShowcase()
    {
#if DEBUG
        var page = new Views.ShowcasePage();

        page.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(MainViewModel.SelectedPage))
        {
            Source = _viewModel,
            Converter = Controls.EqualsToVisibilityConverter.Instance,
            ConverterParameter = nameof(AppPage.Showcase)
        });

        Pages.Children.Add(page);

        var open = new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => _viewModel.SelectedPage = AppPage.Showcase),
            Key.D, ModifierKeys.Control | ModifierKeys.Shift);

        InputBindings.Add(open);
#endif
    }
}
