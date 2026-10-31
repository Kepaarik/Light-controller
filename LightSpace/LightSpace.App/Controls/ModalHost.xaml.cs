using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LightSpace.App.Controls;

/// <summary>
/// Слой модальной карточки поверх содержимого окна: вопрос, пояснение, необязательный переключатель и две кнопки.
/// Карточки показываются через <see cref="Services.Dialogs"/>.
/// </summary>
public partial class ModalHost : UserControl
{
    private TaskCompletionSource<bool>? _pending;

    public ModalHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Включён ли переключатель последней показанной карточки.
    /// </summary>
    public bool IsOptionChecked => OptionBox.IsChecked == true;

    /// <summary>
    /// Показывает карточку и ждёт ответа: true — подтверждение, false — отмена или Esc.
    /// Если карточка уже открыта, прежний вопрос считается отменённым.
    /// optionText — подпись переключателя под пояснением; он показывается выключенным. null — переключателя нет.
    /// </summary>
    public Task<bool> ShowAsync(string title, string message, string confirmText, string? cancelText, bool isDanger,
        string? optionText = null)
    {
        _pending?.TrySetResult(false);
        _pending = new TaskCompletionSource<bool>();

        TitleText.Text = title;
        MessageText.Text = message;
        OptionBox.Content = optionText;
        OptionBox.IsChecked = false;
        OptionBox.Visibility = optionText is null ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = confirmText;
        ConfirmButton.SetResourceReference(StyleProperty, isDanger ? "DangerButton" : "AccentButton");
        CancelButton.Content = cancelText;
        CancelButton.Visibility = cancelText is null ? Visibility.Collapsed : Visibility.Visible;

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, Fade(0, 1));
        ConfirmButton.Focus();

        return _pending.Task;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    /// <summary>
    /// Esc закрывает карточку как отмена; Enter нажимает кнопку в фокусе.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close(false);
        e.Handled = true;
    }

    private void Close(bool result)
    {
        var pending = _pending;
        _pending = null;

        var fade = Fade(1, 0);
        fade.Completed += (_, _) =>
        {
            // За время исчезновения могли открыть новую карточку — её не прячем
            if (_pending is null)
                Visibility = Visibility.Collapsed;
        };

        BeginAnimation(OpacityProperty, fade);
        pending?.TrySetResult(result);
    }

    private DoubleAnimation Fade(double from, double to)
    {
        return new DoubleAnimation(from, to, (Duration)FindResource("MotionNormal"))
        {
            EasingFunction = (IEasingFunction)FindResource("EaseOut")
        };
    }
}
