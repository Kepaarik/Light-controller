using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LightSpace.App.Controls;

/// <summary>
/// Перестановка строк списка: строку берут мышью и тянут на новое место. Подходит любому ItemsControl:
/// пока строку тянут, список сразу показывает её на новом месте, а команда MoveCommand получает каждый шаг перестановки.
/// </summary>
public static class DragReorder
{
    // Формат данных перетаскивания: строки одного списка нельзя бросить в другой
    private const string DataFormat = "LightSpace.DragReorder";

    /// <summary>
    /// Команда перестановки; параметр — <see cref="ReorderMove"/> с прежним и новым местом строки.
    /// Пока команда не задана, строки не перетаскиваются.
    /// </summary>
    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(DragReorder), new PropertyMetadata(null, OnMoveCommandChanged));

    /// <summary>
    /// Строку сейчас тянут мышью. Наследуется вложенными элементами строки, чтобы её оформление могло это показать.
    /// </summary>
    public static readonly DependencyProperty IsDraggingProperty = DependencyProperty.RegisterAttached(
        "IsDragging", typeof(bool), typeof(DragReorder), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    // Список, в котором нажали кнопку мыши, строка под курсором и где её нажали
    private static ItemsControl? _pressedList;
    private static object? _pressedItem;
    private static Point _pressPoint;

    public static ICommand? GetMoveCommand(DependencyObject element) => (ICommand?)element.GetValue(MoveCommandProperty);

    public static void SetMoveCommand(DependencyObject element, ICommand? value) => element.SetValue(MoveCommandProperty, value);

    public static bool GetIsDragging(DependencyObject element) => (bool)element.GetValue(IsDraggingProperty);

    public static void SetIsDragging(DependencyObject element, bool value) => element.SetValue(IsDraggingProperty, value);

    private static void OnMoveCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list)
            return;

        list.PreviewMouseLeftButtonDown -= OnPressed;
        list.PreviewMouseMove -= OnMouseMove;
        list.DragOver -= OnDragOver;
        list.Drop -= OnDrop;

        list.AllowDrop = e.NewValue != null;

        if (e.NewValue == null)
            return;

        list.PreviewMouseLeftButtonDown += OnPressed;
        list.PreviewMouseMove += OnMouseMove;
        list.DragOver += OnDragOver;
        list.Drop += OnDrop;
    }

    /// <summary>
    /// Нажатие запоминается, но не перехватывается: кнопки внутри строки работают как обычно.
    /// </summary>
    private static void OnPressed(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;

        _pressedList = list;
        _pressedItem = ItemUnder(list, e.OriginalSource as DependencyObject);
        _pressPoint = e.GetPosition(list);
    }

    /// <summary>
    /// Мышь с зажатой кнопкой ушла дальше порога — начинается перетаскивание строки.
    /// </summary>
    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var list = (ItemsControl)sender;

        if (e.LeftButton != MouseButtonState.Pressed || _pressedList != list || _pressedItem == null)
            return;

        var point = e.GetPosition(list);

        // Мелкое дрожание руки при клике не считается
        if (Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _pressedItem;
        _pressedItem = null;

        var container = list.ItemContainerGenerator.ContainerFromItem(item);

        container?.SetValue(IsDraggingProperty, true);

        try
        {
            DragDrop.DoDragDrop(list, new DataObject(DataFormat, item), DragDropEffects.Move);
        }
        finally
        {
            // Пока строку тянули, список мог пересоздать её контейнер — отметку снимаем со всех
            foreach (var other in list.Items)
                list.ItemContainerGenerator.ContainerFromItem(other)?.ClearValue(IsDraggingProperty);

            container?.ClearValue(IsDraggingProperty);
            _pressedList = null;
        }
    }

    /// <summary>
    /// Строку протащили над другой строкой — она сразу встаёт на её место.
    /// </summary>
    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var list = (ItemsControl)sender;

        e.Handled = true;

        if (_pressedList != list || !e.Data.GetDataPresent(DataFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        var dragged = e.Data.GetData(DataFormat);
        var target = ItemUnder(list, e.OriginalSource as DependencyObject);

        if (dragged == null || target == null)
            return;

        var from = list.Items.IndexOf(dragged);
        var to = list.Items.IndexOf(target);

        if (from < 0 || to < 0 || from == to || GetMoveCommand(list) is not { } command)
            return;

        var move = new ReorderMove(from, to);

        if (!command.CanExecute(move))
            return;

        command.Execute(move);

        // Список мог пересоздать контейнер строки — отметка перетаскивания переезжает на новый
        list.ItemContainerGenerator.ContainerFromIndex(to)?.SetValue(IsDraggingProperty, true);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// Строка списка, внутри которой находится элемент; null — элемент не внутри строки.
    /// </summary>
    private static object? ItemUnder(ItemsControl list, DependencyObject? element)
    {
        if (element == null || ItemsControl.ContainerFromElement(list, element) is not { } container)
            return null;

        var item = list.ItemContainerGenerator.ItemFromContainer(container);

        return item == DependencyProperty.UnsetValue ? null : item;
    }
}

/// <summary>
/// Шаг перестановки строк: строка с места From встаёт на место To.
/// </summary>
public sealed record ReorderMove(int From, int To);
