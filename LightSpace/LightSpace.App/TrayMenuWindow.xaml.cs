using System.Windows;
using System.Windows.Input;
using LightSpace.App.ViewModels;

namespace LightSpace.App;

/// <summary>
/// Меню значка в трее: включить или выключить подсветку, открыть окно, выйти.
/// Закрывается, как только теряет фокус.
/// </summary>
public partial class TrayMenuWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Action _open;
    private readonly Action _exit;

    private bool _isClosing;

    public TrayMenuWindow(MainViewModel viewModel, Action open, Action exit)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _open = open;
        _exit = exit;
        DataContext = viewModel;

        Deactivated += (_, _) => CloseOnce();
        Closing += (_, _) => _isClosing = true;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Показывает меню над курсором в пределах рабочей области экрана; координаты — в физических пикселях.
    /// </summary>
    public void ShowAt(Point cursor, Rect workArea)
    {
        // Сначала за краем экрана: размер карточки и масштаб монитора известны только после показа
        Left = -32000;
        Top = -32000;

        Show();
        UpdateLayout();

        var source = PresentationSource.FromVisual(this);
        var toDips = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;

        var point = toDips.Transform(cursor);
        var area = new Rect(toDips.Transform(workArea.TopLeft), toDips.Transform(workArea.BottomRight));

        // Правый нижний угол меню — у курсора, как у системного меню трея
        Left = Math.Clamp(point.X - ActualWidth, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(point.Y - ActualHeight, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));

        Activate();
    }

    private void OnToggleLighting(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleLightingCommand.Execute(null);
        CloseOnce();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        CloseOnce();
        _open();
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        CloseOnce();
        _exit();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            CloseOnce();
    }

    /// <summary>
    /// Закрывает меню, если оно ещё не закрывается: Deactivated приходит и во время закрытия.
    /// </summary>
    private void CloseOnce()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        Close();
    }
}
