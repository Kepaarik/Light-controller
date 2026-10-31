using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LightSpace.App.Controls;
using LightSpace.App.ViewModels;
using LightSpace.Core;

namespace LightSpace.App.Views;

/// <summary>
/// Страница «Сцена»: карта стола с устройствами и группами.
/// </summary>
public partial class ScenePage : UserControl
{
    // Где нажали кнопку мыши на сцене, что было под курсором и был ли зажат Ctrl
    private Point _pressPoint;
    private object? _pressTarget;
    private bool _isAdding;
    private bool _isPressed;
    private bool _isBanding;

    // Устройства, которые тянут мышью в режиме редактирования расстановки, где была мышь на прошлом шаге (в единицах сцены)
    // и сдвинулись ли они хоть раз
    private IReadOnlyList<SceneDeviceViewModel>? _moving;
    private Point _lastScenePoint;
    private bool _isMoved;

    public ScenePage()
    {
        InitializeComponent();
    }

    private SceneViewModel? Scene => SceneArea.DataContext as SceneViewModel;

    /// <summary>
    /// Нажатие на сцене: что это — клик, начало рамки или перетаскивание устройства, станет ясно, когда мышь сдвинется
    /// или отпустят кнопку.
    /// </summary>
    private void OnAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(MapHost);
        _pressTarget = TargetOf(e.OriginalSource as DependencyObject);
        _isAdding = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _isPressed = true;
        _isBanding = false;
        _isMoved = false;
        _moving = null;

        // Устройства двигаются только в режиме редактирования расстановки, чтобы случайно ничего не сдвинуть
        if (Scene is { IsEditingLayout: true } scene && _pressTarget is SceneDeviceViewModel device && !_isAdding)
        {
            _moving = scene.DevicesToMove(device);
            _lastScenePoint = SceneMap.ToScene(_pressPoint, MapHost.RenderSize);
        }

        SceneArea.CaptureMouse();
        e.Handled = true;
    }

    private void OnAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPressed)
            return;

        var point = e.GetPosition(MapHost);

        // Мелкое дрожание руки при клике рамкой не считается
        if (!_isBanding && !_isMoved && Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_moving != null && Scene is { } scene)
        {
            var scenePoint = SceneMap.ToScene(point, MapHost.RenderSize);

            scene.MoveDevices(_moving, scenePoint.X - _lastScenePoint.X, scenePoint.Y - _lastScenePoint.Y);
            _lastScenePoint = scenePoint;
            _isMoved = true;
            return;
        }

        _isBanding = true;

        var band = new Rect(_pressPoint, point);

        Canvas.SetLeft(SelectionBand, band.X);
        Canvas.SetTop(SelectionBand, band.Y);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;
        SelectionBand.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Кнопку отпустили: рамка выделяет устройства, которых коснулась, клик выбирает то, на что нажали.
    /// Перетаскивание устройств заканчивается при потере захвата мыши.
    /// </summary>
    private void OnAreaMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPressed)
            return;

        var isBanding = _isBanding;
        var isMoved = _isMoved;
        var band = new Rect(_pressPoint, e.GetPosition(MapHost));
        var target = _pressTarget;

        // Отпускание захвата сбрасывает состояние нажатия и прячет рамку
        SceneArea.ReleaseMouseCapture();

        if (Scene is not { } scene || isMoved)
            return;

        if (isBanding)
        {
            scene.SelectDevices(DevicesIn(scene, band), _isAdding);
            return;
        }

        switch (target)
        {
            case SceneDeviceViewModel device:
                scene.ClickDevice(device, _isAdding);
                break;

            case SceneGroupViewModel group:
                scene.ClickGroup(group, _isAdding);
                break;

            // Ctrl+клик мимо устройств не должен терять уже выделенные
            default:
                if (!_isAdding)
                    scene.ClickEmpty();
                break;
        }
    }

    private void OnAreaLostMouseCapture(object sender, MouseEventArgs e)
    {
        // Устройства отпустили — новые места сохраняются, даже если захват мыши пропал сам
        if (_isMoved)
            Scene?.FinishMove();

        _isPressed = false;
        _isBanding = false;
        _isMoved = false;
        _moving = null;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Устройство или группа под курсором: ближайшая фигура устройства или рамка группы вверх по дереву элементов.
    /// </summary>
    private object? TargetOf(DependencyObject? element)
    {
        while (element != null && element != SceneArea)
        {
            if (element is FrameworkElement { Name: "DeviceShape", DataContext: SceneDeviceViewModel device })
                return device;

            if (element is FrameworkElement { Name: "GroupFrame", DataContext: SceneGroupViewModel group })
                return group;

            // Текст внутри TextBlock — не визуальный элемент, его родитель находится через логическое дерево
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>
    /// Устройства, которые на сцене пересекаются с рамкой.
    /// </summary>
    private List<SceneDeviceViewModel> DevicesIn(SceneViewModel scene, Rect band)
    {
        var from = SceneMap.ToScene(band.TopLeft, MapHost.RenderSize);
        var to = SceneMap.ToScene(band.BottomRight, MapHost.RenderSize);
        var area = new SceneRect(from.X, from.Y, to.X - from.X, to.Y - from.Y);

        return scene.Devices.Where(d => d.Bounds.IntersectsWith(area)).ToList();
    }
}
