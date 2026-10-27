using LedHub.Core;

namespace LedHub.ViewModels;

/// <summary>
/// Клавиша клавиатуры в редакторе светодиодов: её светодиод, надпись и место на схеме в пикселях.
/// </summary>
public sealed class KeyCellViewModel
{
    public KeyCellViewModel(LedCellViewModel cell, KeyboardKey key, double unit)
    {
        Cell = cell;
        Label = key.Label;
        Left = key.X * unit;
        Top = key.Y * unit;
        Width = key.Width * unit;
        Height = key.Height * unit;
    }

    public LedCellViewModel Cell { get; }

    public string Label { get; }

    public double Left { get; }

    public double Top { get; }

    public double Width { get; }

    public double Height { get; }
}
