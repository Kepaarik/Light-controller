using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LightSpace.App.Controls;
using LightSpace.App.ViewModels;
using LightSpace.Core;

namespace LightSpace.App.Views;

/// <summary>
/// Редактор содержимого-сцены: холст со слоями, список слоёв и настройки выбранного слоя.
/// </summary>
public partial class SceneEditor : UserControl
{
    // Что делает мышь на холсте после нажатия
    private enum Gesture
    {
        None,
        Move,
        Resize,
        Draw
    }

    // Где нажали кнопку мыши (в единицах сцены), какой слой под ней, его границы в этот момент и сдвинулась ли мышь
    private Gesture _gesture;
    private Point _pressPoint;
    private SceneLayerViewModel? _pressedLayer;
    private SceneRect _pressBounds;
    private bool _isDragged;
    private Point _pressPixel;

    public SceneEditor()
    {
        InitializeComponent();
    }

    private SceneEditorViewModel? Editor => DataContext as SceneEditorViewModel;

    /// <summary>
    /// Нажатие на холсте: за ручку выбранного слоя — изменение размера, внутри слоя — перетаскивание,
    /// на месте без слоёв — новые границы выбранного слоя. Клик это или перетаскивание, станет ясно, когда мышь сдвинется.
    /// </summary>
    private void OnAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Editor is not { } editor)
            return;

        _pressPixel = e.GetPosition(LayerArea);
        _pressPoint = ToScene(_pressPixel);
        _isDragged = false;

        if (HandleOf(e.OriginalSource as DependencyObject) is { } resized)
        {
            _gesture = Gesture.Resize;
            _pressedLayer = resized;
        }
        else
        {
            _pressedLayer = editor.DraggedLayerAt(_pressPoint.X, _pressPoint.Y);
            _gesture = _pressedLayer != null ? Gesture.Move : Gesture.Draw;
        }

        _pressBounds = _pressedLayer?.Settings.Bounds ?? default;

        LayerArea.CaptureMouse();
        e.Handled = true;
    }

    private void OnAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (_gesture == Gesture.None || Editor is not { } editor)
            return;

        var pixel = e.GetPosition(LayerArea);

        // Мелкое дрожание руки при клике перетаскиванием не считается
        if (!_isDragged && Math.Abs(pixel.X - _pressPixel.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pixel.Y - _pressPixel.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var point = ToScene(pixel);

        switch (_gesture)
        {
            case Gesture.Move when _pressedLayer != null:
                editor.SelectedLayer = _pressedLayer;
                _pressedLayer.SetBounds(_pressBounds with
                {
                    X = _pressBounds.X + point.X - _pressPoint.X,
                    Y = _pressBounds.Y + point.Y - _pressPoint.Y
                });
                break;

            case Gesture.Resize when _pressedLayer != null:
                _pressedLayer.SetBounds(_pressBounds with
                {
                    Width = Inside(point).X - _pressBounds.X,
                    Height = Inside(point).Y - _pressBounds.Y
                });
                break;

            // Новые границы получает выбранный слой; если слоёв нет, рисовать нечего
            case Gesture.Draw when editor.SelectedLayer is { } layer:
                var from = Inside(_pressPoint);
                var to = Inside(point);

                layer.SetBounds(new SceneRect(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y),
                    Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)));
                break;

            default:
                return;
        }

        _isDragged = true;
    }

    /// <summary>
    /// Кнопку отпустили: клик выбирает верхний слой под курсором. Перетаскивание заканчивается при потере захвата мыши.
    /// </summary>
    private void OnAreaMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_gesture == Gesture.None)
            return;

        var isClick = !_isDragged;

        // Отпускание захвата сбрасывает состояние нажатия
        LayerArea.ReleaseMouseCapture();

        if (isClick && Editor is { } editor && editor.LayerAt(_pressPoint.X, _pressPoint.Y) is { } layer)
            editor.SelectedLayer = layer;
    }

    private void OnAreaLostMouseCapture(object sender, MouseEventArgs e)
    {
        // Новые границы уходят движку, даже если захват мыши пропал сам
        if (_isDragged)
            Editor?.CommitBounds();

        _gesture = Gesture.None;
        _pressedLayer = null;
        _isDragged = false;
    }

    /// <summary>
    /// Слой, за чью ручку размера взялись: ближайшая ручка вверх по дереву элементов.
    /// </summary>
    private SceneLayerViewModel? HandleOf(DependencyObject? element)
    {
        while (element != null && element != LayerArea)
        {
            if (element is FrameworkElement { Name: "LayerHandle", DataContext: SceneLayerViewModel layer })
                return layer;

            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>
    /// Точка холста в единицах сцены.
    /// </summary>
    private Point ToScene(Point pixel)
    {
        return SceneMap.ToScene(pixel, LayerArea.RenderSize);
    }

    /// <summary>
    /// Точка сцены, прижатая к её краям.
    /// </summary>
    private static Point Inside(Point point)
    {
        return new Point(Math.Clamp(point.X, 0, SceneArea.Width), Math.Clamp(point.Y, 0, SceneArea.Height));
    }
}
