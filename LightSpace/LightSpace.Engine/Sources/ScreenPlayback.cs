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
/// <param name="KeepAspect">Сцена ложится на середину кадра без растяжения; false — растягивается на весь кадр.</param>
public sealed record ScreenSettings(string MonitorKey, double Saturation, double Brightness, double Smoothing, bool KeepAspect = false)
{
    public static ScreenSettings From(ScreenConfig screen)
    {
        return new ScreenSettings(screen.MonitorKey, screen.Saturation / 100.0, screen.Brightness / 100.0, screen.Smoothing / 100.0,
            screen.KeepAspect);
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

    // Чьи точки рисовались с прошлого Advance: прежние цвета остальных — устройств, которых больше нет, — забываются
    private readonly HashSet<string> _rendered = new();

    private ScreenPicture _picture = ScreenPicture.Empty;

    // Доля нового цвета в этом кадре
    private double _follow = 1;

    /// <summary>
    /// Берёт для следующего кадра картинку монитора, прошло seconds секунд с прошлого кадра.
    /// </summary>
    public void Advance(ScreenSettings settings, ScreenPicture picture, double seconds)
    {
        if (_states.Count > _rendered.Count)
        {
            foreach (var key in _states.Keys.Where(k => !_rendered.Contains(k)).ToList())
                _states.Remove(key);
        }

        _rendered.Clear();
        _picture = picture;
        _follow = 1 - Math.Pow(settings.Smoothing, seconds * SmoothingRate);
    }

    /// <summary>
    /// Цвета точек: points — места точек в пространстве width × height, которое ложится на кадр монитора по настройкам;
    /// null — точка нигде не стоит и остаётся чёрной. key — чьи это точки: по нему помнятся прежние цвета.
    /// </summary>
    public RgbColor[] Render(string key, ScreenSettings settings, IReadOnlyList<EffectPixel?> points, double width, double height)
    {
        var colors = new RgbColor[points.Count];
        var isNew = !_states.TryGetValue(key, out var states) || states.Length != points.Count;

        _rendered.Add(key);

        if (isNew)
        {
            states = new (double R, double G, double B)[points.Count];
            _states[key] = states;
        }

        var (scaleX, scaleY) = settings.KeepAspect ? AspectScale(width, height) : (1.0, 1.0);

        for (var i = 0; i < colors.Length; i++)
        {
            if (points[i] is not { } point)
                continue;

            var u = 0.5 + (point.X / width - 0.5) * scaleX;
            var v = 0.5 + (point.Y / height - 0.5) * scaleY;
            var target = Adjust(_picture.Sample(u, v), settings);

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
    /// Какую долю кадра по ширине и высоте видит сцена width × height, когда она ложится на середину кадра
    /// без растяжения: по одной стороне — весь кадр, по другой — его середина.
    /// </summary>
    private (double X, double Y) AspectScale(double width, double height)
    {
        var scene = width / Math.Max(height, 0.01);
        var picture = (double)_picture.Columns / Math.Max(_picture.Rows, 1);

        // Сцена уже кадра — она видит всю высоту, а края кадра слева и справа не видны; шире — наоборот
        return scene < picture ? (scene / picture, 1) : (1, picture / scene);
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
