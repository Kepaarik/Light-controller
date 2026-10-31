using System.Globalization;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Числа в полях ввода: показываются с одним знаком после запятой, вводятся с запятой или с точкой.
/// </summary>
public static class NumberText
{
    public static string Format(double value)
    {
        return Math.Round(value, 1).ToString("0.#", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Число из текста поля; false — в поле не число.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        var normalized = (text ?? "").Trim().Replace(',', '.');

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}
