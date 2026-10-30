using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LightSpace.App.Controls;

/// <summary>
/// Пустое состояние страницы или панели: что здесь будет и что сделать, чтобы это появилось.
/// </summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(null, OnActionTextChanged));

    public static readonly DependencyProperty ActionIconProperty = DependencyProperty.Register(
        nameof(ActionIcon), typeof(string), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(EmptyState), new PropertyMetadata(null));

    public EmptyState()
    {
        InitializeComponent();

        ActionButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Символ большого значка из Segoe Fluent Icons.
    /// </summary>
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Пояснение под заголовком.
    /// </summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>
    /// Подпись кнопки главного действия; без неё кнопки нет.
    /// </summary>
    public string? ActionText
    {
        get => (string?)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>
    /// Значок на кнопке главного действия.
    /// </summary>
    public string? ActionIcon
    {
        get => (string?)GetValue(ActionIconProperty);
        set => SetValue(ActionIconProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private static void OnActionTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var state = (EmptyState)d;

        state.ActionButton.Visibility = string.IsNullOrEmpty(e.NewValue as string) ? Visibility.Collapsed : Visibility.Visible;
    }
}
