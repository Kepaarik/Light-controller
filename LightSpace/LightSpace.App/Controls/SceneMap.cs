using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightSpace.Core;

namespace LightSpace.App.Controls;

/// <summary>
/// Карта сцены: раскладывает элементы по их местам на сцене. Место элемента (Place) — прямоугольник в единицах сцены,
/// Angle — поворот вокруг его середины по часовой стрелке. Вся сцена вписывается в размер карты с сохранением пропорций
/// и стоит по центру, поэтому несколько карт одного размера друг над другом совпадают точка в точку.
/// Сама карта ничего не рисует и пропускает мышь там, где нет элементов; подложка сцены — отдельный элемент
/// со стилем SceneSurface на месте <see cref="WholeScene"/>.
/// </summary>
public sealed class SceneMap : Canvas
{
    public static readonly DependencyProperty PlaceProperty = DependencyProperty.RegisterAttached(
        "Place", typeof(Rect), typeof(SceneMap), new PropertyMetadata(Rect.Empty, OnPlacementChanged));

    public static readonly DependencyProperty AngleProperty = DependencyProperty.RegisterAttached(
        "Angle", typeof(double), typeof(SceneMap), new PropertyMetadata(0.0, OnPlacementChanged));

    /// <summary>
    /// Место всей сцены: для подложки и для элементов во всю сцену.
    /// </summary>
    public static readonly Rect WholeScene = new(0, 0, SceneArea.Width, SceneArea.Height);

    public SceneMap()
    {
        // Размер карты задаёт масштаб сцены: при каждом его изменении элементы встают на новые места
        SizeChanged += (_, _) => PositionAll();
    }

    /// <summary>
    /// Место элемента на сцене без поворота; пустое — элемент не показывается.
    /// </summary>
    public static Rect GetPlace(DependencyObject element) => (Rect)element.GetValue(PlaceProperty);

    public static void SetPlace(DependencyObject element, Rect value) => element.SetValue(PlaceProperty, value);

    /// <summary>
    /// Поворот элемента вокруг его середины в градусах по часовой стрелке.
    /// </summary>
    public static double GetAngle(DependencyObject element) => (double)element.GetValue(AngleProperty);

    public static void SetAngle(DependencyObject element, double value) => element.SetValue(AngleProperty, value);

    /// <summary>
    /// Сколько точек экрана приходится на единицу сцены, когда сцену вписывают в область area.
    /// </summary>
    public static double ScaleFor(Size area)
    {
        return Math.Max(Math.Min(area.Width / SceneArea.Width, area.Height / SceneArea.Height), 0);
    }

    /// <summary>
    /// Где лежит сцена, вписанная в область area.
    /// </summary>
    public static Rect SceneBoundsIn(Size area)
    {
        var scale = ScaleFor(area);
        var width = SceneArea.Width * scale;
        var height = SceneArea.Height * scale;

        return new Rect((area.Width - width) / 2, (area.Height - height) / 2, width, height);
    }

    /// <summary>
    /// Точка сцены под точкой point области area.
    /// </summary>
    public static Point ToScene(Point point, Size area)
    {
        var bounds = SceneBoundsIn(area);
        var scale = Math.Max(ScaleFor(area), double.Epsilon);

        return new Point((point.X - bounds.X) / scale, (point.Y - bounds.Y) / scale);
    }

    private static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if (VisualTreeHelper.GetParent(element) is SceneMap map)
        {
            map.Position(element);
            return;
        }

        // Место задали раньше, чем элемент попал на карту, — он встанет на место, когда загрузится
        element.Loaded -= OnElementLoaded;
        element.Loaded += OnElementLoaded;
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;

        element.Loaded -= OnElementLoaded;

        if (VisualTreeHelper.GetParent(element) is SceneMap map)
            map.Position(element);
    }

    private void PositionAll()
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(this); i++)
        {
            if (VisualTreeHelper.GetChild(this, i) is FrameworkElement element)
                Position(element);
        }
    }

    /// <summary>
    /// Ставит элемент на его место на сцене в нынешнем масштабе карты.
    /// </summary>
    private void Position(FrameworkElement element)
    {
        var place = GetPlace(element);

        if (place.IsEmpty)
        {
            element.Width = 0;
            element.Height = 0;
            return;
        }

        var area = new Size(ActualWidth, ActualHeight);
        var scale = ScaleFor(area);
        var bounds = SceneBoundsIn(area);
        var angle = GetAngle(element);

        SetLeft(element, bounds.X + place.X * scale);
        SetTop(element, bounds.Y + place.Y * scale);
        element.Width = place.Width * scale;
        element.Height = place.Height * scale;

        // Поворот не влияет на раскладку: элемент поворачивается вокруг своей середины уже на месте
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = angle == 0 ? Transform.Identity : new RotateTransform(angle);
    }
}
