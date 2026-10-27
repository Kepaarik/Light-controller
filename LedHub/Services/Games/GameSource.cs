using System.Diagnostics;
using LedHub.Core;
using static LedHub.Services.Effects.EffectMath;

namespace LedHub.Services.Games;

/// <summary>
/// Подсветка под игру. В Dota 2 подсветка монитора показывает здоровье и ману героя, в Counter-Strike 2 —
/// здоровье и броню, а поверх — события: выбор героя, оглушение, вспышка, бомба, победа. Прочие игры
/// подсвечиваются по картинке экрана.
/// </summary>
public sealed class GameSource : ILightSource, IDisposable
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

    private readonly GameKind _kind;
    private readonly GsiServer _server;
    private readonly Func<LedDevice, MonitorBinding?> _bindingFor;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Прочие игры: лента вокруг монитора повторяет края картинки, остальные ленты играют эффект по экрану
    private readonly MonitorSyncSource? _screen;
    private readonly ILightSource? _screenEffect;

    private GameLook _look = GameLook.Fill(Black, 0);
    private double _lastTime;

    // Вспышка урона: health упал — подсветка коротко краснеет
    private int _lastHealth = -1;
    private double _hit;

    // Бомба в CS2: писк ускоряется со временем, взрыв и разминирование — вспышка
    private string _lastBomb = "";
    private double _bombPlantedAt;
    private double _beepPhase;
    private double _eventFlash;
    private RgbColor _eventColor;

    /// <summary>
    /// bindingFor — монитор для ленты вокруг главного монитора; у остальных лент — null.
    /// screenEffect — эффект по картинке экрана для остальных лент в прочих играх.
    /// </summary>
    public GameSource(GameKind kind, GsiServer server, Func<LedDevice, MonitorBinding?> bindingFor, ILightSource? screenEffect)
    {
        _kind = kind;
        _server = server;
        _bindingFor = bindingFor;
        _screenEffect = screenEffect;

        if (kind == GameKind.Other)
            _screen = new MonitorSyncSource(bindingFor);
    }

    public void BeginFrame(double phase, EffectSettings settings)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastTime, 0, 0.5);
        _lastTime = now;

        switch (_kind)
        {
            case GameKind.Dota2:
                _look = DotaLook(_server.Dota, now, dt);
                break;

            case GameKind.CounterStrike2:
                _look = CsLook(_server.Cs, now, dt);
                break;

            default:
                _screen?.BeginFrame(phase, settings);
                _screenEffect?.BeginFrame(phase, settings);
                break;
        }
    }

    public RgbColor RenderSolid(LedDevice device)
    {
        if (_kind == GameKind.Other)
        {
            return _bindingFor(device) != null && _screen != null
                ? _screen.RenderSolid(device)
                : _screenEffect?.RenderSolid(device) ?? Black;
        }

        return Shade(_look.Solid);
    }

    public IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount)
    {
        var binding = _bindingFor(device);

        if (_kind == GameKind.Other)
        {
            return binding != null && _screen != null
                ? _screen.RenderStrip(device, ledCount)
                : _screenEffect?.RenderStrip(device, ledCount) ?? new RgbColor[ledCount];
        }

        return binding?.Layout != null
            ? RenderAroundMonitor(binding, ledCount)
            : RenderAlongStrip(ledCount);
    }

    public void Dispose()
    {
        _screen?.Dispose();
        (_screenEffect as IDisposable)?.Dispose();
    }

    private GameLook DotaLook(DotaState? state, double time, double dt)
    {
        if (state == null || DateTime.UtcNow - state.ReceivedAt > StaleAfter || state.GameState == "")
            return GameLook.Fill(DotaRed, Breathe(time, 0.25, 0.15, 0.5));

        switch (state.GameState)
        {
            case DotaState.HeroSelection:
            case DotaState.StrategyTime:
                // Выбор героя — просто мигаем
                return GameLook.Fill(Gold, Frac(time * 1.2) < 0.5 ? 1 : 0.1);

            case DotaState.PreGame:
                return GameLook.Fill(TeamColor(state.Team), Breathe(time, 0.4, 0.4, 1));

            case DotaState.PostGame:
                return Finale(state.WinTeam, state.Team, time);

            case DotaState.InProgress:
                break;

            default:
                return GameLook.Fill(DotaRed, Breathe(time, 0.25, 0.15, 0.5));
        }

        if (!state.HasHero)
            return GameLook.Fill(TeamColor(state.Team), 0.6);

        if (!state.IsAlive)
        {
            _lastHealth = -1;

            // Последние секунды до возрождения — частое белое мигание
            return state.RespawnSeconds is > 0 and <= 3
                ? GameLook.Fill(White, Frac(time * 3) < 0.5 ? 0.8 : 0.15)
                : GameLook.Fill(DotaRed, Breathe(time, 0.5, 0.1, 0.3));
        }

        TrackHit(state.Health, dt);

        var health = state.Health / 100.0;
        var level = state.IsDaytime ? 1 : 0.8;

        // Мало здоровья — подсветка тревожно пульсирует
        if (health < 0.25)
            level *= 0.55 + 0.45 * Breathe(time, 2.5, 0, 1);

        var (overlay, amount) = state switch
        {
            { IsStunned: true } => (Stun, 0.3 + 0.25 * Math.Sin(time * 40)),
            { IsHexed: true } => (Hex, 0.45),
            { IsSilenced: true } => (Silence, 0.35),
            { IsSmoked: true } => (Smoke, 0.5),
            _ => (Black, 0.0)
        };

        if (_hit > amount)
            (overlay, amount) = (Hit, _hit);

        var healthColor = HealthColor(health);

        return new GameLook(health, healthColor, state.Mana / 100.0, Mana, healthColor, overlay, amount, level);
    }

    private GameLook CsLook(CsState? state, double time, double dt)
    {
        if (state == null || DateTime.UtcNow - state.ReceivedAt > StaleAfter || state.MapPhase == "")
            return GameLook.Fill(Terrorists, Breathe(time, 0.25, 0.15, 0.5));

        TrackBomb(state.Bomb, time, dt);

        var team = state.Team == "CT" ? CounterTerrorists : Terrorists;
        GameLook look;

        if (state.RoundPhase == "over")
        {
            look = Finale(state.WinTeam, state.Team, time);
        }
        else if (state.RoundPhase == "freezetime")
        {
            look = GameLook.Fill(team, Breathe(time, 0.6, 0.35, 1));
        }
        else if (!state.IsOwnPlayer || state.Health <= 0)
        {
            // Погиб и смотрит за другими — подсветка почти гаснет
            _lastHealth = -1;
            look = GameLook.Fill(team, 0.15);
        }
        else
        {
            TrackHit(state.Health, dt);

            var health = state.Health / 100.0;
            var healthColor = HealthColor(health);
            var (overlay, amount) = (Black, 0.0);

            if (state.Flashed > 0)
                (overlay, amount) = (White, state.Flashed / 255.0);
            else if (state.Burning > 0)
                (overlay, amount) = (Fire, state.Burning / 255.0 * (0.6 + 0.4 * Noise(time * 8, 0, 0)));

            if (_hit > amount)
                (overlay, amount) = (Hit, _hit);

            look = new GameLook(health, healthColor, state.Armor / 100.0, Armor, healthColor, overlay, amount, 1);
        }

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
    /// Лента вокруг монитора: левая половина — здоровье, правая — мана или броня. Обе полоски растут
    /// снизу вверх по боковым сторонам и сходятся в центре верхней.
    /// </summary>
    private IReadOnlyList<RgbColor> RenderAroundMonitor(MonitorBinding binding, int ledCount)
    {
        var layout = binding.Layout!;
        var aspect = MonitorSyncSource.PhysicalAspect(binding);
        var points = ScreenLayoutMath.Build(layout, ledCount, aspect);
        var back = layout.IsViewedFromBack;
        var colors = new RgbColor[ledCount];

        // Стороны так, как их видно спереди: со спины левая и правая меняются местами
        var useLeft = back ? layout.UseRight : layout.UseLeft;
        var useRight = back ? layout.UseLeft : layout.UseRight;

        for (var i = 0; i < ledCount; i++)
        {
            if (points[i] is not { } point)
                continue;

            var x = back ? 1 - point.X : point.X;
            var side = (point.Side, back) switch
            {
                (ScreenSide.Left, true) => ScreenSide.Right,
                (ScreenSide.Right, true) => ScreenSide.Left,
                _ => point.Side
            };

            var isLeft = side == ScreenSide.Left || (side != ScreenSide.Right && x < 0.5);
            var u = isLeft ? x : 1 - x;
            var bottom = layout.UseBottom ? aspect / 2 : 0;
            var vertical = (isLeft ? useLeft : useRight) ? 1 : 0;
            var top = layout.UseTop ? aspect / 2 : 0;
            var total = bottom + vertical + top;

            var position = side switch
            {
                ScreenSide.Bottom => (0.5 - u) * aspect,
                ScreenSide.Top => bottom + vertical + u * aspect,
                _ => bottom + (1 - point.Y)
            };

            colors[i] = Bar(isLeft, total > 0 ? position / total : 0.5);
        }

        return colors;
    }

    /// <summary>
    /// Лента без раскладки: первая половина — здоровье, вторая — мана или броня.
    /// </summary>
    private IReadOnlyList<RgbColor> RenderAlongStrip(int ledCount)
    {
        var colors = new RgbColor[ledCount];
        var half = ledCount / 2;

        for (var i = 0; i < ledCount; i++)
        {
            colors[i] = i < half
                ? Bar(true, (i + 0.5) / Math.Max(1, half))
                : Bar(false, (i - half + 0.5) / Math.Max(1, ledCount - half));
        }

        return colors;
    }

    private RgbColor Bar(bool isLeft, double position)
    {
        var fill = isLeft ? _look.Left : _look.Right;
        var color = isLeft ? _look.LeftColor : _look.RightColor;

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
    /// Как выглядит подсветка на этом кадре: две полоски (здоровье слева, мана или броня справа),
    /// цвет для обычных лент, цвет поверх всего и общая яркость.
    /// </summary>
    private readonly record struct GameLook(
        double Left,
        RgbColor LeftColor,
        double Right,
        RgbColor RightColor,
        RgbColor Solid,
        RgbColor Overlay,
        double OverlayAmount,
        double Level)
    {
        public static GameLook Fill(RgbColor color, double level)
        {
            return new GameLook(1, color, 1, color, color, Black, 0, level);
        }
    }
}
