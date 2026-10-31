using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Неизменяемая копия настроек содержимого-экрана.
/// </summary>
/// <param name="MonitorKey">Системное имя монитора; пусто — основной монитор Windows.</param>
/// <param name="Saturation">Множитель насыщенности: 1 — как на экране.</param>
/// <param name="Brightness">Множитель яркости: 1 — как на экране.</param>
/// <param name="Smoothing">Доля прежнего цвета, которая остаётся за 1/20 секунды, от 0 до 0,95.</param>
public sealed record ScreenSettings(string MonitorKey, double Saturation, double Brightness, double Smoothing)
{
    public static ScreenSettings From(ScreenConfig screen)
    {
        return new ScreenSettings(screen.MonitorKey, screen.Saturation / 100.0, screen.Brightness / 100.0, screen.Smoothing / 100.0);
    }
}

/// <summary>
/// Как играет содержимое-экран: каждая точка берёт цвет картинки монитора под своим местом, цвета плавно следуют
/// за картинкой. Помнит прежние цвета каждого устройства для сглаживания, поэтому у каждого играющего содержимого
/// свой экземпляр; живёт в одном потоке.
/// </summary>
public sealed class ScreenPlayback
{
    // Сглаживание задано на 1/20 секунды: так же, как в старом LedHub при 20 кадрах в секунду
    private const double SmoothingRate = 20;

    private readonly Dictionary<string, (double R, double G, double B)[]> _states = new();

    private ScreenPicture _picture = ScreenPicture.Empty;

    // Доля нового цвета в этом кадре
    private double _follow = 1;

    /// <summary>
    /// Берёт для следующего кадра картинку монитора, прошло seconds секунд с прошлого кадра.
    /// </summary>
    public void Advance(ScreenSettings settings, ScreenPicture picture, double seconds)
    {
        _picture = picture;
        _follow = 1 - Math.Pow(settings.Smoothing, seconds * SmoothingRate);
    }

    /// <summary>
    /// Цвета точек: points — места точек в пространстве width × height, которое целиком растягивается на кадр монитора;
    /// null — точка нигде не стоит и остаётся чёрной. key — чьи это точки: по нему помнятся прежние цвета.
    /// </summary>
    public RgbColor[] Render(string key, ScreenSettings settings, IReadOnlyList<EffectPixel?> points, double width, double height)
    {
        var colors = new RgbColor[points.Count];
        var isNew = !_states.TryGetValue(key, out var states) || states.Length != points.Count;

        if (isNew)
        {
            states = new (double R, double G, double B)[points.Count];
            _states[key] = states;
        }

        for (var i = 0; i < colors.Length; i++)
        {
            if (points[i] is not { } point)
                continue;

            var target = Adjust(_picture.Sample(point.X / width, point.Y / height), settings);

            // Первый кадр сразу показывает картинку, дальше цвет плавно догоняет её
            var follow = isNew ? 1 : _follow;
            var state = states![i];

            state = (
                state.R + (target.R - state.R) * follow,
                state.G + (target.G - state.G) * follow,
                state.B + (target.B - state.B) * follow);

            states[i] = state;
            colors[i] = new RgbColor(ToByte(state.R), ToByte(state.G), ToByte(state.B));
        }

        return colors;
    }

    /// <summary>
    /// Цвет картинки с насыщенностью и яркостью из настроек.
    /// </summary>
    private static RgbColor Adjust(RgbColor color, ScreenSettings settings)
    {
        var (hue, saturation, value) = color.ToHsv();

        return RgbColor.FromHsv(hue, Math.Min(1, saturation * settings.Saturation), Math.Min(1, value * settings.Brightness));
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}
