namespace LedHub.ViewModels;

/// <summary>
/// Что делает клик в редакторе шестигранников.
/// </summary>
public enum HexEditMode
{
    /// <summary>Добавить или убрать шестигранник.</summary>
    Shape,

    /// <summary>Закрасить отдельный светодиод.</summary>
    PaintLed,

    /// <summary>Закрасить все светодиоды шестигранника.</summary>
    PaintHex
}
