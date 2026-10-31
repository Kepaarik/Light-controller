using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Frames;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вкладка «Сцена»: живые цвета устройств и панель эффекта — галерея плиток с живыми превью, настройки выбранного
/// эффекта и яркость. Выбор и настройки сразу попадают в содержимое «По умолчанию» и уходят движку новым снимком.
/// </summary>
public sealed partial class SceneViewModel : ObservableObject
{
    // Живые цвета и превью обновляются не чаще 30 раз в секунду
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly AppConfig _config;
    private readonly DeviceManager _manager;
    private readonly FrameLoop _loop;
    private readonly Action _contentChanged;
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _clock = new();

    private EffectTileViewModel _selectedTile;
    private TimeSpan _lastTick;

    /// <summary>
    /// contentChanged вызывается после каждого изменения эффекта, его настроек или яркости.
    /// </summary>
    public SceneViewModel(AppConfig config, DeviceManager manager, FrameLoop loop, Action contentChanged)
    {
        _config = config;
        _manager = manager;
        _loop = loop;
        _contentChanged = contentChanged;

        Tiles = EffectLibrary.All
            .Select(e => new EffectTileViewModel(e, EffectValues.From(config.DefaultContent.Effects.GetValueOrDefault(e.Id))))
            .ToList();

        _selectedTile = Tiles.FirstOrDefault(t => t.Definition.Id == config.DefaultContent.EffectId) ?? Tiles[0];
        BuildParameters();

        _manager.DevicesChanged += (_, _) => SyncDevices();
        SyncDevices();

        _liveTimer = new DispatcherTimer { Interval = LiveInterval };
        _liveTimer.Tick += (_, _) => OnLiveTick();
    }

    /// <summary>
    /// Плитки галереи по разделам: цвета, движение, природа.
    /// </summary>
    public IReadOnlyList<EffectTileViewModel> Tiles { get; }

    /// <summary>
    /// Эффект, который играет на всех устройствах.
    /// </summary>
    public EffectTileViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            // Галерея сбрасывает выбор в null, когда пересобирает плитки, — играющий эффект от этого не меняется
            if (value == null || !SetProperty(ref _selectedTile, value))
                return;

            _config.DefaultContent.EffectId = value.Definition.Id;
            BuildParameters();
            _contentChanged();
        }
    }

    /// <summary>
    /// Настройки выбранного эффекта.
    /// </summary>
    public ObservableCollection<EffectParameterViewModel> Parameters { get; } = new();

    /// <summary>
    /// Яркость подсветки всех устройств, 0–100 %.
    /// </summary>
    public double Brightness
    {
        get => _config.Brightness;
        set
        {
            var brightness = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (brightness == _config.Brightness)
                return;

            _config.Brightness = brightness;
            OnPropertyChanged();
            _contentChanged();
        }
    }

    public ObservableCollection<SceneDeviceViewModel> Devices { get; } = new();

    public bool HasDevices => Devices.Count > 0;

    /// <summary>
    /// Включает живые цвета и превью, пока сцену видно, и выключает, когда окно скрыто или открыта другая вкладка.
    /// </summary>
    public void SetLive(bool isLive)
    {
        if (isLive == _liveTimer.IsEnabled)
            return;

        _loop.IsLiveColorsWanted = isLive;

        if (!isLive)
        {
            _liveTimer.Stop();
            _clock.Stop();
            return;
        }

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _liveTimer.Start();
    }

    /// <summary>
    /// Возвращает все настройки выбранного эффекта к значениям по умолчанию.
    /// </summary>
    [RelayCommand]
    private void ResetParameters()
    {
        foreach (var parameter in Parameters)
            parameter.Reset();

        OnParameterChanged();
    }

    /// <summary>
    /// Пересобирает настройки под выбранный эффект; правки пишутся в его сохранённые настройки.
    /// </summary>
    private void BuildParameters()
    {
        Parameters.Clear();

        var values = ValuesConfigOf(_selectedTile.Definition);

        foreach (var parameter in _selectedTile.Definition.Parameters)
        {
            EffectParameterViewModel item = parameter.Kind switch
            {
                EffectParameterKind.Color => new ColorParameterViewModel(parameter, values, OnParameterChanged),
                EffectParameterKind.Choice => new ChoiceParameterViewModel(parameter, values, OnParameterChanged),
                EffectParameterKind.Toggle => new ToggleParameterViewModel(parameter, values, OnParameterChanged),
                _ => new NumberParameterViewModel(parameter, values, OnParameterChanged)
            };

            Parameters.Add(item);
        }
    }

    private void OnParameterChanged()
    {
        // Превью плитки рисуется с теми же настройками, что и эффект на устройствах
        _selectedTile.Values = EffectValues.From(ValuesConfigOf(_selectedTile.Definition));

        _contentChanged();
    }

    /// <summary>
    /// Сохранённые настройки эффекта; заводятся пустыми, если эффект ещё не настраивали.
    /// </summary>
    private EffectValuesConfig ValuesConfigOf(EffectDefinition effect)
    {
        var effects = _config.DefaultContent.Effects;

        if (!effects.TryGetValue(effect.Id, out var values))
        {
            values = new EffectValuesConfig();
            effects[effect.Id] = values;
        }

        return values;
    }

    /// <summary>
    /// Приводит устройства сцены к списку менеджера: после поиска у устройств могли смениться возможности.
    /// </summary>
    private void SyncDevices()
    {
        Devices.Clear();

        foreach (var device in _manager.Devices)
            Devices.Add(new SceneDeviceViewModel(device));

        OnPropertyChanged(nameof(HasDevices));
    }

    /// <summary>
    /// Один кадр живой сцены: последние цвета устройств от движка и следующий кадр превью на плитках.
    /// </summary>
    private void OnLiveTick()
    {
        var now = _clock.Elapsed;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        foreach (var tile in Tiles)
            tile.Advance(seconds);

        var live = _loop.LiveColors;

        foreach (var device in Devices)
        {
            var colors = live is { IsLightingEnabled: true } && live.Devices.TryGetValue(device.Id, out var found) ? found : null;

            device.Show(colors, live?.Brightness ?? 100);
        }
    }
}
