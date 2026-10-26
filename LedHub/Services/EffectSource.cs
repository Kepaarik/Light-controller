using LedHub.Core;

namespace LedHub.Services;

/// <summary>
/// Программный эффект: цвет каждой точки рассчитывается по времени и положению на ленте.
/// </summary>
public sealed class EffectSource : ILightSource
{
    private readonly EffectKind _kind;
    private double _phase;
    private RgbColor _color;

    public EffectSource(EffectKind kind)
    {
        _kind = kind;
    }

    public void BeginFrame(double phase, EffectSettings settings)
    {
        _phase = phase;
        _color = settings.Color;
    }

    public RgbColor RenderSolid()
    {
        return RenderPixel(_kind, _phase, _color, 0);
    }

    public IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount)
    {
        return Enumerable.Range(0, ledCount)
            .Select(i => RenderPixel(_kind, _phase, _color, i / (double)ledCount))
            .ToList();
    }

    /// <summary>
    /// Цвет точки ленты в момент времени; position — положение на ленте от 0 до 1.
    /// Обычные ленты получают точку position = 0, поэтому «бегущие» эффекты на них пульсируют целиком.
    /// </summary>
    private static RgbColor RenderPixel(EffectKind kind, double phase, RgbColor color, double position)
    {
        switch (kind)
        {
            case EffectKind.Rainbow:
                return RgbColor.FromHsv(phase * 60 + position * 360, 1, 1);

            case EffectKind.ColorCycle:
                return RgbColor.FromHsv(phase * 45, 1, 1);

            case EffectKind.Wave:
                var wave = (1 + Math.Cos(2 * Math.PI * (position - phase / 4))) / 2;
                return color.Scale((int)Math.Round(8 + 92 * wave * wave));

            case EffectKind.Comet:
                // Расстояние позади «головы» кометы, которая пробегает ленту за три единицы фазы
                var behindHead = ((phase / 3 - position) % 1 + 1) % 1;
                var tail = behindHead < 0.3 ? Math.Pow(1 - behindHead / 0.3, 2) : 0;
                return color.Scale((int)Math.Round(100 * tail));

            case EffectKind.Twinkle:
                return Random.Shared.NextDouble() < 0.05 ? RgbColor.White : color.Scale(12);

            case EffectKind.Fire:
                var heat = Random.Shared.NextDouble();
                return RgbColor.FromHsv(8 + 30 * heat, 1, 0.45 + 0.55 * heat);

            case EffectKind.Police:
                var isLeftHalf = position < 0.5;
                var isSwapped = (int)(phase * 2) % 2 == 1;
                return isLeftHalf ^ isSwapped ? new RgbColor(255, 0, 0) : new RgbColor(0, 40, 255);

            case EffectKind.Breathing:
                var breath = (1 - Math.Cos(phase * Math.PI / 2)) / 2;
                return color.Scale((int)Math.Round(5 + 95 * breath));

            case EffectKind.Strobe:
                return phase * 4 % 1 < 0.5 ? color : new RgbColor(0, 0, 0);

            default:
                // Свеча: каждая точка независимо и неравномерно подрагивает
                return color.Scale(55 + Random.Shared.Next(46));
        }
    }
}
