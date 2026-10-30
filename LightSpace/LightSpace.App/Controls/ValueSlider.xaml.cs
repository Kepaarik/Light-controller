using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LightSpace.App.Controls;

/// <summary>
/// Ползунок с подписью и текущим значением: «Длительность перехода … 400 мс».
/// Заливка по умолчанию акцентная, её можно заменить своей кистью (например, градиентом цвета).
/// </summary>
public partial class ValueSlider : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ValueSlider), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ValueSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ValueSlider), new PropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ValueSlider), new PropertyMetadata(100.0, OnRangeChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(ValueSlider), new PropertyMetadata(1.0, OnRangeChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ValueSlider), new PropertyMetadata("", OnFormatChanged));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(ValueSlider), new PropertyMetadata(0, OnFormatChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ValueSlider), new PropertyMetadata(null, OnFillChanged));

    // Пока ползунку передаются значение или диапазон из кода, его изменения не считаются действием пользователя
    private bool _isSyncing;

    public ValueSlider()
    {
        InitializeComponent();

        Inner.SetResourceReference(ForegroundProperty, "AccentBrush");
        SyncInner();
        UpdateValueText();
    }

    /// <summary>
    /// Подпись слева над ползунком.
    /// </summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Шаг, к которому прилипает значение.
    /// </summary>
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>
    /// Единица после значения: «мс», «с», «%».
    /// </summary>
    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>
    /// Сколько знаков после запятой показывать у значения.
    /// </summary>
    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    /// <summary>
    /// Заливка пройденной части; если не задана — акцентный цвет.
    /// </summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (ValueSlider)d;

        slider.SyncInner();
        slider.UpdateValueText();
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ValueSlider)d).SyncInner();
    }

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ValueSlider)d).UpdateValueText();
    }

    private void OnInnerValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncing)
            return;

        SetCurrentValue(ValueProperty, e.NewValue);
    }

    /// <summary>
    /// Передаёт ползунку диапазон, шаг и значение; сначала диапазон, чтобы значение не обрезалось по старому.
    /// </summary>
    private void SyncInner()
    {
        // До InitializeComponent элементов разметки ещё нет
        if (Inner is null)
            return;

        _isSyncing = true;

        Inner.Minimum = Minimum;
        Inner.Maximum = Math.Max(Minimum, Maximum);
        Inner.SmallChange = Step;
        Inner.LargeChange = Step;
        Inner.TickFrequency = Step;
        Inner.Value = Value;

        _isSyncing = false;
    }

    private static void OnFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (ValueSlider)d;

        if (e.NewValue is Brush brush)
            slider.Inner.Foreground = brush;
        else
            slider.Inner.SetResourceReference(ForegroundProperty, "AccentBrush");
    }

    private void UpdateValueText()
    {
        // До InitializeComponent элементов разметки ещё нет
        if (ValueLabel is null)
            return;

        var number = Value.ToString("N" + Math.Clamp(Decimals, 0, 4), CultureInfo.CurrentCulture);

        ValueLabel.Text = string.IsNullOrEmpty(Unit) ? number : $"{number} {Unit}";
    }
}
