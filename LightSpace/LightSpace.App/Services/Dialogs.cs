using LightSpace.App.Controls;

namespace LightSpace.App.Services;

/// <summary>
/// Модальные карточки поверх главного окна вместо MessageBox и системных диалогов.
/// </summary>
public static class Dialogs
{
    /// <summary>
    /// Слой главного окна, в котором показываются карточки.
    /// </summary>
    public static ModalHost? Host { get; set; }

    /// <summary>
    /// Спрашивает подтверждение; true — пользователь согласился. Без окна ответ всегда «нет».
    /// </summary>
    public static Task<bool> ConfirmAsync(string title, string message, string confirmText = "ОК", string? cancelText = "Отмена",
        bool isDanger = false)
    {
        return Host?.ShowAsync(title, message, confirmText, cancelText, isDanger) ?? Task.FromResult(false);
    }

    /// <summary>
    /// Показывает сообщение с одной кнопкой.
    /// </summary>
    public static Task ShowAsync(string title, string message)
    {
        return ConfirmAsync(title, message, "Понятно", cancelText: null);
    }
}
