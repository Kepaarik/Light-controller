using System.Windows.Media;
using LightSpace.App.Controls;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Плитка эффекта в галерее: название и живое превью, которое считается тем же рендером, что и кадры устройств,
/// на маленькой сетке точек.
/// </summary>
public sealed class EffectTileViewModel
{
    private const int PreviewColumns = 24;
    private const int PreviewRows = 10;

    // Момент эффекта, с которого начинается превью: не самое начало, чтобы картинка сразу была «живой»
    private const double PreviewStartTime = 3.3;

    private static readonly DeviceGeometry PreviewSpace = DeviceGeometry.Grid(PreviewColumns, PreviewRows);

    private readonly ColorBitmap _bitmap = new(PreviewColumns, PreviewRows);
    private double _time = PreviewStartTime;

    public EffectTileViewModel(EffectDefinition definition, EffectValues values)
    {
        Definition = definition;
        Values = values;

        Advance(0);
    }

    public EffectDefinition Definition { get; }

    public string Title => Definition.Title;

    public string Description => Definition.Description;

    /// <summary>
    /// Кисть с текущим кадром превью.
    /// </summary>
    public Brush Preview => _bitmap.Brush;

    /// <summary>
    /// Настройки, с которыми рисуется превью: те же, что сохранены у эффекта.
    /// </summary>
    public EffectValues Values { get; set; }

    /// <summary>
    /// Продвигает превью на seconds секунд с учётом скорости эффекта и рисует новый кадр.
    /// </summary>
    public void Advance(double seconds)
    {
        _time += seconds * Definition.Number(Values, "speed");

        _bitmap.Write(EffectRenderer.Render(Definition, Values, PreviewSpace, _time));
    }
}
