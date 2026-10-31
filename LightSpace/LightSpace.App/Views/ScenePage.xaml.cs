using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LightSpace.App.ViewModels;

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

    public ScenePage()
    {
        InitializeComponent();
    }

    private SceneViewModel? Scene => SceneArea.DataContext as SceneViewModel;

    /// <summary>
    /// Нажатие на сцене: что это — клик или начало рамки, станет ясно, когда мышь сдвинется или отпустят кнопку.
    /// </summary>
    private void OnAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(SceneArea);
        _pressTarget = TargetOf(e.OriginalSource as DependencyObject);
        _isAdding = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _isPressed = true;
        _isBanding = false;

        SceneArea.CaptureMouse();
        e.Handled = true;
    }

    private void OnAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPressed)
            return;

        var point = e.GetPosition(SceneArea);

        // Мелкое дрожание руки при клике рамкой не считается
        if (!_isBanding && Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

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
    /// </summary>
    private void OnAreaMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPressed)
            return;

        var isBanding = _isBanding;
        var band = new Rect(_pressPoint, e.GetPosition(SceneArea));
        var target = _pressTarget;

        // Отпускание захвата сбрасывает состояние нажатия и прячет рамку
        SceneArea.ReleaseMouseCapture();

        if (Scene is not { } scene)
            return;

        if (isBanding)
        {
            scene.SelectDevices(DevicesIn(band), _isAdding);
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
        _isPressed = false;
        _isBanding = false;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Устройство или группа под курсором: ближайшая карточка устройства или рамка группы вверх по дереву элементов.
    /// </summary>
    private object? TargetOf(DependencyObject? element)
    {
        while (element != null && element != SceneArea)
        {
            if (element is FrameworkElement { Name: "DeviceCard", DataContext: SceneDeviceViewModel device })
                return device;

            if (element is FrameworkElement { Name: "GroupFrame", DataContext: SceneGroupViewModel group })
                return group;

            // Текст внутри TextBlock — не визуальный элемент, его родитель находится через логическое дерево
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>
    /// Устройства, карточки которых пересекаются с рамкой.
    /// </summary>
    private List<SceneDeviceViewModel> DevicesIn(Rect band)
    {
        var devices = new List<SceneDeviceViewModel>();

        CollectDevices(SceneArea, band, devices);

        return devices;
    }

    private void CollectDevices(DependencyObject parent, Rect band, List<SceneDeviceViewModel> devices)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is Border { Name: "DeviceCard", IsVisible: true, DataContext: SceneDeviceViewModel device } card)
            {
                var bounds = card.TransformToAncestor(SceneArea).TransformBounds(new Rect(card.RenderSize));

                if (bounds.IntersectsWith(band))
                    devices.Add(device);

                continue;
            }

            CollectDevices(child, band, devices);
        }
    }
}
