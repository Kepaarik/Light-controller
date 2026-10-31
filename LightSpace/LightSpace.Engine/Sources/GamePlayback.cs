using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Space;
using static LightSpace.Engine.Effects.EffectMath;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Неизменяемая копия настроек содержимого-игры.
/// </summary>
/// <param name="BackgroundLevel">Яркость фона, от 0 до 1.</param>
/// <param name="DisabledEvents">Выключенные события — ключи из <see cref="GameConfig.EventKey"/>.</param>
/// <param name="Screen">Картинка какого монитора и как светится фоном «Картинка экрана» и в других играх.</param>
public sealed record GameSettings(
    GameKind Game,
    GameBackground Background,
    double BackgroundLevel,
    bool KeyboardBars,
    bool StripBars,
    IReadOnlySet<string> DisabledEvents,
    ScreenSettings Screen)
{
    /// <summary>
    /// Нужна картинка монитора: другая игра светится ею целиком, игра с интеграцией — фоном.
    /// </summary>
    public bool NeedsScreen => Game == GameKind.Other || Background == GameBackground.Screen;

    /// <summary>
    /// Нужны события, которые присылает игра.
    /// </summary>
    public bool NeedsGameState => Game != GameKind.Other;

    public static GameSettings From(GameConfig game)
    {
        // Картинка монитора в игре светится с теми же цветами и сглаживанием, что содержимое-экран по умолчанию
        var screen = ScreenSettings.From(new ScreenConfig { MonitorKey = game.MonitorKey });

        return new GameSettings(game.Game, game.Background, game.BackgroundBrightness / 100.0, game.KeyboardBars, game.StripBars,
            game.DisabledEvents.ToHashSet(), screen);
    }

    /// <summary>
    /// Событие игры eventKey включено.
    /// </summary>
    public bool IsEnabled(string eventKey)
    {
        return !DisabledEvents.Contains(GameConfig.EventKey(Game, eventKey));
    }
}

/// <summary>
/// Как играет содержимое-игра. В Dota 2 свет показывает здоровье и ману героя, в Counter-Strike 2 — здоровье и броню,
/// а поверх — события: выбор героя, оглушение, вспышка, бомба, победа. На клавиатуре здоровье — верхняя половина рядов,
/// мана или броня — нижняя; на ленте — первая и вторая половина; устройство одного цвета светится цветом здоровья.
/// Вне матча всё светится одним цветом. Другие игры светятся картинкой монитора, как содержимое-экран.
/// Помнит урон, бомбу и сглаженные цвета экрана, поэтому у каждого играющего содержимого свой экземпляр; живёт в одном потоке.
/// </summary>
public sealed class GamePlayback
{
    // Дольше этого игра молчит только в меню — тогда ждём начала матча
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);

    // Насколько светится пустая часть полоски, чтобы было видно, где она кончается
    private const double TrackLevel = 0.07;

    private static readonly RgbColor White = RgbColor.White;
    private static readonly RgbColor DotaRed = new(200, 30, 20);
    private static readonly RgbColor Gold = new(255, 180, 40);
    private static readonly RgbColor Mana = new(30, 90, 255);
    private static readonly RgbColor Radiant = new(40, 220, 80);
    private static readonly RgbColor Dire = new(230, 40, 30);
    private static readonly RgbColor Stun = new(255, 255, 255);
    private static readonly RgbColor Hex = new(120, 255, 60);
    private static readonly RgbColor Silence = new(170, 60, 255);
    private static readonly RgbColor Smoke = new(60, 40, 160);
    private static readonly RgbColor Hit = new(255, 0, 0);
    private static readonly RgbColor CounterTerrorists = new(80, 150, 255);
    private static readonly RgbColor Terrorists = new(255, 160, 30);
    private static readonly RgbColor Armor = new(170, 200, 255);
    private static readonly RgbColor Fire = new(255, 90, 0);

    // Картинка монитора: целиком в других играх и фоном в играх с интеграцией
    private readonly ScreenPlayback _screen = new();

    private GameLook _look = GameLook.Fill(Black, 0);
    private RgbColor _teamColor = Gold;
    private double _time;

    // Вспышка урона: здоровье упало — свет коротко краснеет
    private int _lastHealth = -1;
    private double _hit;

    // Бомба в CS2: писк ускоряется со временем, взрыв и разминирование — вспышка
    private string _lastBomb = "";
    private double _bombPlantedAt;
    private double _beepPhase;
    private double _eventFlash;
    private RgbColor _eventColor;

    /// <summary>
    /// Игра game сейчас в матче и присылает свежее состояние; иначе свет ждёт начала матча.
    /// </summary>
    public static bool IsInMatch(GameKind game, GameState state)
    {
        return game switch
        {
            GameKind.Dota2 => state.Dota is { } dota && DateTime.UtcNow - dota.ReceivedAt <= StaleAfter && dota.GameState != "",
            GameKind.CounterStrike2 => state.CounterStrike is { } cs && DateTime.UtcNow - cs.ReceivedAt <= StaleAfter && cs.MapPhase != "",
            _ => false
        };
    }

    /// <summary>
    /// Двигает время на seconds секунд и берёт для следующего кадра последнее состояние игры и картинку монитора.
    /// </summary>
    public void Advance(GameSettings settings, GameState state, ScreenPicture picture, double seconds)
    {
        var dt = Math.Clamp(seconds, 0, 0.5);
        _time += dt;

        if (settings.NeedsScreen)
            _screen.Advance(settings.Screen, picture, seconds);

        _look = settings.Game switch
        {
            GameKind.Dota2 => DotaLook(settings, state.Dota, _time, dt),
            GameKind.CounterStrike2 => CsLook(settings, state.CounterStrike, _time, dt),
            _ => GameLook.Fill(Black, 0)
        };
    }

    /// <summary>
    /// Цвета светодиодов устройства; у устройства одного цвета — один цвет. geometry — пространство устройства,
    /// по нему ложатся полоски; points — места тех же светодиодов в пространстве width × height, которое целиком
    /// растягивается на кадр монитора, — по ним берётся картинка. key — чьи это точки: по нему помнятся цвета экрана.
    /// </summary>
    public RgbColor[] Render(string key, GameSettings settings, DeviceGeometry geometry, IReadOnlyList<EffectPixel?> points,
        double width, double height)
    {
        if (settings.Game == GameKind.Other)
            return _screen.Render(key, settings.Screen, points, width, height);

        var screen = settings.Background == GameBackground.Screen ? _screen.Render(key, settings.Screen, points, width, height) : null;

        if (!geometry.IsAddressable)
            return new[] { RenderSolid(settings, screen) };

        // Вне матча полосок нет — всё светится одним цветом
        if (!_look.HasBars)
            return Enumerable.Repeat(Shade(_look.Solid), geometry.Points.Count).ToArray();

        if (geometry.IsLinear)
            return settings.StripBars ? RenderAlongStrip(geometry) : RenderBackdrop(settings, geometry, screen);

        var colors = RenderBackdrop(settings, geometry, screen);

        if (settings.KeyboardBars)
            PaintHalves(geometry, colors);

        return colors;
    }

    private GameLook DotaLook(GameSettings settings, DotaState? state, double time, double dt)
    {
        if (state == null || DateTime.UtcNow - state.ReceivedAt > StaleAfter || state.GameState == "")
            return GameLook.Fill(DotaRed, Breathe(time, 0.25, 0.15, 0.5));

        switch (state.GameState)
        {
            case DotaState.HeroSelection:
            case DotaState.StrategyTime:
                // Выбор героя — просто мигаем
                return settings.IsEnabled(GameEvents.HeroSelection)
                    ? GameLook.Fill(Gold, Frac(time * 1.2) < 0.5 ? 1 : 0.1)
                    : GameLook.Fill(Gold, 0.4);

            case DotaState.PreGame:
                return GameLook.Fill(TeamColor(state.Team), Breathe(time, 0.4, 0.4, 1));

            case DotaState.PostGame:
                return settings.IsEnabled(GameEvents.Victory)
                    ? Finale(state.WinTeam, state.Team, time)
                    : GameLook.Fill(TeamColor(state.Team), 0.6);

            case DotaState.InProgress:
                break;

            default:
                return GameLook.Fill(DotaRed, Breathe(time, 0.25, 0.15, 0.5));
        }

        _teamColor = TeamColor(state.Team);

        if (!state.HasHero)
            return GameLook.Fill(TeamColor(state.Team), 0.6);

        if (!state.IsAlive)
        {
            _lastHealth = -1;

            if (!settings.IsEnabled(GameEvents.Death))
                return GameLook.Fill(TeamColor(state.Team), 0.3);

            // Последние секунды до возрождения — частое белое мигание
            return state.RespawnSeconds is > 0 and <= 3
                ? GameLook.Fill(White, Frac(time * 3) < 0.5 ? 0.8 : 0.15)
                : GameLook.Fill(DotaRed, Breathe(time, 0.5, 0.1, 0.3));
        }

        TrackHit(state.Health, dt);

        var health = state.Health / 100.0;
        var level = state.IsDaytime ? 1 : 0.8;

        // Мало здоровья — свет тревожно пульсирует
        if (health < 0.25 && settings.IsEnabled(GameEvents.LowHealth))
            level *= 0.55 + 0.45 * Breathe(time, 2.5, 0, 1);

        var (overlay, amount) = settings.IsEnabled(GameEvents.Status)
            ? state switch
            {
                { IsStunned: true } => (Stun, 0.3 + 0.25 * Math.Sin(time * 40)),
                { IsHexed: true } => (Hex, 0.45),
                { IsSilenced: true } => (Silence, 0.35),
                { IsSmoked: true } => (Smoke, 0.5),
                _ => (Black, 0.0)
            }
            : (Black, 0.0);

        if (_hit > amount && settings.IsEnabled(GameEvents.Damage))
            (overlay, amount) = (Hit, _hit);

        var healthColor = HealthColor(health);

        return new GameLook(health, healthColor, state.Mana / 100.0, Mana, healthColor, overlay, amount, level, true);
    }

    private GameLook CsLook(GameSettings settings, CsState? state, double time, double dt)
    {
        if (state == null || DateTime.UtcNow - state.ReceivedAt > StaleAfter || state.MapPhase == "")
            return GameLook.Fill(Terrorists, Breathe(time, 0.25, 0.15, 0.5));

        TrackBomb(state.Bomb, time, dt);

        var team = state.Team == "CT" ? CounterTerrorists : Terrorists;
        GameLook look;

        _teamColor = team;

        if (state.RoundPhase == "over")
        {
            look = settings.IsEnabled(GameEvents.Victory) ? Finale(state.WinTeam, state.Team, time) : GameLook.Fill(team, 0.6);
        }
        else if (state.RoundPhase == "freezetime")
        {
            look = GameLook.Fill(team, Breathe(time, 0.6, 0.35, 1));
        }
        else if (!state.IsOwnPlayer || state.Health <= 0)
        {
            // Погиб и смотрит за другими — свет почти гаснет
            _lastHealth = -1;
            look = GameLook.Fill(team, 0.15);
        }
        else
        {
            TrackHit(state.Health, dt);

            var health = state.Health / 100.0;
            var healthColor = HealthColor(health);
            var (overlay, amount) = (Black, 0.0);

            if (state.Flashed > 0 && settings.IsEnabled(GameEvents.Flash))
                (overlay, amount) = (White, state.Flashed / 255.0);
            else if (state.Burning > 0 && settings.IsEnabled(GameEvents.Burn))
                (overlay, amount) = (Fire, state.Burning / 255.0 * (0.6 + 0.4 * Noise(time * 8, 0, 0)));

            if (_hit > amount && settings.IsEnabled(GameEvents.Damage))
                (overlay, amount) = (Hit, _hit);

            look = new GameLook(health, healthColor, state.Armor / 100.0, Armor, healthColor, overlay, amount, 1, true);
        }

        if (!settings.IsEnabled(GameEvents.Bomb))
            return look;

        // Заложенная бомба пищит красным поверх всего, взрыв и разминирование — общая вспышка
        if (state.Bomb == "planted" && state.RoundPhase != "over" && Frac(_beepPhase) < 0.25 && look.OverlayAmount < 0.7)
            look = look with { Overlay = Hit, OverlayAmount = 0.7 };

        if (_eventFlash > look.OverlayAmount)
            look = look with { Overlay = _eventColor, OverlayAmount = _eventFlash, Level = Math.Max(look.Level, _eventFlash) };

        return look;
    }

    /// <summary>
    /// Конец матча или раунда: победа своей команды переливается, поражение — тусклый красный.
    /// </summary>
    private static GameLook Finale(string winTeam, string team, double time)
    {
        if (team != "" && !winTeam.Equals(team, StringComparison.OrdinalIgnoreCase))
            return GameLook.Fill(DotaRed, 0.25);

        return GameLook.Fill(RgbColor.FromHsv(time * 120 % 360, 1, 1), Breathe(time, 1, 0.6, 1));
    }

    private void TrackHit(int health, double dt)
    {
        var drop = _lastHealth < 0 ? 0 : _lastHealth - health;

        if (drop > 3)
            _hit = Math.Max(_hit, Math.Min(0.8, 0.25 + drop / 30.0));

        _lastHealth = health;
        _hit = Math.Max(0, _hit - dt * 2.5);
    }

    private void TrackBomb(string bomb, double time, double dt)
    {
        if (bomb != _lastBomb)
        {
            switch (bomb)
            {
                case "planted":
                    _bombPlantedAt = time;
                    _beepPhase = 0;
                    break;

                case "exploded":
                    (_eventColor, _eventFlash) = (Fire, 1);
                    break;

                case "defused":
                    (_eventColor, _eventFlash) = (CounterTerrorists, 1);
                    break;
            }

            _lastBomb = bomb;
        }

        // Бомба взрывается через 40 секунд; писк учащается от раза в секунду до семи
        var period = 1 - Math.Clamp((time - _bombPlantedAt) / 40, 0, 1) * 0.85;
        _beepPhase += dt / period;
        _eventFlash = Math.Max(0, _eventFlash - dt * 0.7);
    }

    /// <summary>
    /// Цвет устройства без отдельных светодиодов: в матче — цвет здоровья, а без полосок на лентах — фон;
    /// события — поверх. screen — цвета картинки монитора, если фон — картинка.
    /// </summary>
    private RgbColor RenderSolid(GameSettings settings, RgbColor[]? screen)
    {
        if (_look.HasBars && !settings.StripBars)
            return Shade(Backdrop(settings, screen is { Length: > 0 } ? screen[0] : null));

        return Shade(_look.Solid);
    }

    /// <summary>
    /// Лента: первая половина — здоровье, вторая — мана или броня.
    /// </summary>
    private RgbColor[] RenderAlongStrip(DeviceGeometry geometry)
    {
        var count = geometry.Points.Count;
        var colors = new RgbColor[count];
        var half = count / 2;

        for (var i = 0; i < count; i++)
        {
            colors[i] = i < half
                ? Bar(true, (i + 0.5) / Math.Max(1, half))
                : Bar(false, (i - half + 0.5) / Math.Max(1, count - half));
        }

        return colors;
    }

    /// <summary>
    /// Делит плоское устройство по рядам пополам: верхняя половина — здоровье, нижняя — мана или броня.
    /// Обе полоски заполняются слева направо по месту клавиши.
    /// </summary>
    private void PaintHalves(DeviceGeometry geometry, RgbColor[] colors)
    {
        var points = geometry.Points
            .Select((point, index) => (Point: point, Index: index))
            .Where(p => p.Point != null && p.Index < colors.Length)
            .Select(p => (Point: p.Point!.Value, p.Index))
            .ToList();

        if (points.Count == 0)
            return;

        var top = points.Min(p => p.Point.Y);
        var bottom = points.Max(p => p.Point.Y);
        var middle = (top + bottom) / 2;

        foreach (var (point, index) in points)
        {
            // Место середины клавиши на всей ширине устройства: крайняя справа загорается чуть раньше 100 %,
            // как и полоска на ленте, а не только на полной полоске
            var position = point.X / Math.Max(geometry.Width, 0.01);

            colors[index] = Bar(point.Y < middle, position);
        }
    }

    /// <summary>
    /// Фон с событиями поверх: картинка монитора, цвет команды или темнота.
    /// </summary>
    private RgbColor[] RenderBackdrop(GameSettings settings, DeviceGeometry geometry, RgbColor[]? screen)
    {
        var colors = new RgbColor[geometry.Points.Count];

        for (var i = 0; i < colors.Length; i++)
        {
            // Светодиод без места не горит
            if (geometry.Points[i] == null)
                continue;

            colors[i] = Shade(Backdrop(settings, screen != null && i < screen.Length ? screen[i] : null));
        }

        return colors;
    }

    /// <summary>
    /// Цвет фона, приглушённый до яркости из настроек; screen — цвет картинки монитора в этой точке, если он есть.
    /// </summary>
    private RgbColor Backdrop(GameSettings settings, RgbColor? screen)
    {
        var color = settings.Background switch
        {
            GameBackground.Screen => screen ?? Black,
            GameBackground.Team => _teamColor,
            _ => Black
        };

        return Dim(color, settings.BackgroundLevel);
    }

    private RgbColor Bar(bool isHealth, double position)
    {
        var fill = isHealth ? _look.Left : _look.Right;
        var color = isHealth ? _look.LeftColor : _look.RightColor;

        return Shade(fill >= 1 || position < fill ? color : Dim(color, TrackLevel));
    }

    private RgbColor Shade(RgbColor color)
    {
        return Dim(Mix(color, _look.Overlay, Math.Clamp(_look.OverlayAmount, 0, 1)), Math.Clamp(_look.Level, 0, 1));
    }

    private static RgbColor HealthColor(double health)
    {
        // Полное здоровье — зелёный, половина — жёлтый, на исходе — красный
        return RgbColor.FromHsv(120 * Math.Clamp(health, 0, 1), 1, 1);
    }

    private static RgbColor TeamColor(string team)
    {
        return team.Equals("radiant", StringComparison.OrdinalIgnoreCase) ? Radiant
            : team.Equals("dire", StringComparison.OrdinalIgnoreCase) ? Dire
            : Gold;
    }

    /// <summary>
    /// Плавное дыхание между min и max с частотой speed раз в секунду.
    /// </summary>
    private static double Breathe(double time, double speed, double min, double max)
    {
        return min + (max - min) * (0.5 - 0.5 * Math.Cos(time * speed * 2 * Math.PI));
    }

    /// <summary>
    /// Как выглядит свет на этом кадре: две полоски (здоровье и мана или броня), цвет для устройств одного цвета,
    /// цвет поверх всего и общая яркость. HasBars — идёт матч и полоски есть; иначе (выбор героя, смерть,
    /// конец раунда) всё светится одним цветом.
    /// </summary>
    private readonly record struct GameLook(
        double Left,
        RgbColor LeftColor,
        double Right,
        RgbColor RightColor,
        RgbColor Solid,
        RgbColor Overlay,
        double OverlayAmount,
        double Level,
        bool HasBars = false)
    {
        public static GameLook Fill(RgbColor color, double level)
        {
            return new GameLook(1, color, 1, color, color, Black, 0, level);
        }
    }
}
