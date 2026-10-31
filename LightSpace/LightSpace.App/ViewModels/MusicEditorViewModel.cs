using System.Runtime.CompilerServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Controls;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Sources;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Откуда звук, в сегментах редактора музыки: системный звук или микрофон.
/// </summary>
public sealed record AudioInputOption(AudioInputKind Kind, string Title)
{
    public static readonly IReadOnlyList<AudioInputOption> All = new AudioInputOption[]
    {
        new(AudioInputKind.Output, "Системный звук"),
        new(AudioInputKind.Microphone, "Микрофон")
    };

    public override string ToString() => Title;
}

/// <summary>
/// Устройство звука в выпадающей панели выбора устройства.
/// </summary>
/// <param name="Id">Системный идентификатор устройства; пусто — устройство Windows по умолчанию.</param>
public sealed record AudioDeviceOption(string Id, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// Режим музыки в сегментах: спектр, ритм или пульс.
/// </summary>
public sealed record MusicModeOption(MusicMode Mode, string Title)
{
    public static readonly IReadOnlyList<MusicModeOption> All = new MusicModeOption[]
    {
        new(MusicMode.Spectrum, "Спектр"),
        new(MusicMode.Rhythm, "Ритм"),
        new(MusicMode.Pulse, "Пульс")
    };

    public override string ToString() => Title;
}

/// <summary>
/// Цвета музыки в сегментах: радуга или один цвет.
/// </summary>
public sealed record MusicColorsOption(MusicColors Colors, string Title)
{
    public static readonly IReadOnlyList<MusicColorsOption> All = new MusicColorsOption[]
    {
        new(MusicColors.Rainbow, "Радуга"),
        new(MusicColors.Single, "Свой цвет")
    };

    public override string ToString() => Title;
}

/// <summary>
/// Редактор содержимого-музыки: откуда звук и с какого устройства, режим, цвета, чувствительность, сглаживание
/// и живое превью тем же рендером, что и кадры устройств. Все правки сразу попадают в содержимое.
/// </summary>
public sealed partial class MusicEditorViewModel : ObservableObject
{
    private const int PreviewColumns = 32;
    private const int PreviewRows = 12;

    private static readonly DeviceGeometry PreviewSpace = DeviceGeometry.Grid(PreviewColumns, PreviewRows);

    private readonly SourceHub _sources;
    private readonly Action _changed;
    private readonly ColorBitmap _bitmap = new(PreviewColumns, PreviewRows);

    // Устройства звука, прочитанные в последний раз, по источнику: выбор другого сценария не опрашивает Windows заново
    private readonly Dictionary<AudioInputKind, IReadOnlyList<AudioDeviceInfo>> _knownDevices = new();

    private MusicConfig? _music;
    private MusicPlayback _playback = new();
    private IReadOnlyList<AudioDeviceOption> _devices = Array.Empty<AudioDeviceOption>();

    /// <summary>
    /// sources — откуда превью берёт звук; changed вызывается после каждой правки.
    /// </summary>
    public MusicEditorViewModel(SourceHub sources, Action changed)
    {
        _sources = sources;
        _changed = changed;
    }

    /// <summary>
    /// Кисть с текущим кадром превью.
    /// </summary>
    public Brush Preview => _bitmap.Brush;

    public IReadOnlyList<AudioInputOption> Inputs => AudioInputOption.All;

    public IReadOnlyList<MusicModeOption> Modes => MusicModeOption.All;

    public IReadOnlyList<MusicColorsOption> ColorOptions => MusicColorsOption.All;

    /// <summary>
    /// Откуда звук. Смена источника возвращает устройство по умолчанию: устройства вывода и записи разные.
    /// </summary>
    public AudioInputOption? SelectedInput
    {
        get => _music == null ? null : Inputs.First(o => o.Kind == _music.Input);
        set
        {
            // Сегменты сбрасывают выбор в null, когда пересобираются
            if (value == null || _music == null || value.Kind == _music.Input)
                return;

            _music.Input = value.Kind;
            _music.DeviceId = "";

            OnPropertyChanged();
            ShowDevices();
            _changed();
        }
    }

    /// <summary>
    /// Устройства для выбора: устройство Windows по умолчанию и все включённые устройства выбранного источника.
    /// </summary>
    public IReadOnlyList<AudioDeviceOption> Devices
    {
        get => _devices;
        private set => SetProperty(ref _devices, value);
    }

    /// <summary>
    /// Устройство, с которого слушается звук.
    /// </summary>
    public AudioDeviceOption? SelectedDevice
    {
        get => _music == null ? null : _devices.FirstOrDefault(d => d.Id == _music.DeviceId);
        set
        {
            // Выпадающая панель сбрасывает выбор в null, когда пересобирает список, — устройство от этого не меняется
            if (value == null || _music == null || value.Id == _music.DeviceId)
                return;

            _music.DeviceId = value.Id;
            OnPropertyChanged();
            _changed();
        }
    }

    public MusicModeOption? SelectedMode
    {
        get => _music == null ? null : Modes.First(o => o.Mode == _music.Mode);
        set
        {
            if (value == null || _music == null || value.Mode == _music.Mode)
                return;

            _music.Mode = value.Mode;
            OnPropertyChanged();
            _changed();
        }
    }

    public MusicColorsOption? SelectedColors
    {
        get => _music == null ? null : ColorOptions.First(o => o.Colors == _music.Colors);
        set
        {
            if (value == null || _music == null || value.Colors == _music.Colors)
                return;

            _music.Colors = value.Colors;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSingleColor));
            _changed();
        }
    }

    /// <summary>
    /// Музыка светится одним цветом — тогда виден выбор цвета.
    /// </summary>
    public bool IsSingleColor => _music?.Colors == MusicColors.Single;

    public RgbColor Color
    {
        get => _music != null && RgbColor.TryParse(_music.Color, out var color) ? color : RgbColor.Parse(MusicConfig.DefaultColor);
        set
        {
            if (_music == null || value == Color)
                return;

            _music.Color = value.ToHex();
            OnPropertyChanged();
            _changed();
        }
    }

    public double Sensitivity
    {
        get => _music?.Sensitivity ?? 100;
        set => SetNumber(value, m => m.Sensitivity, (m, v) => m.Sensitivity = v);
    }

    public double Smoothing
    {
        get => _music?.Smoothing ?? 0;
        set => SetNumber(value, m => m.Smoothing, (m, v) => m.Smoothing = v);
    }

    /// <summary>
    /// Открывает содержимое для правки; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content)
    {
        _music = content?.Music;

        // Превью другого содержимого начинает сглаживание заново
        _playback = new MusicPlayback();

        ShowDevices();
        OnPropertyChanged(nameof(SelectedInput));
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(SelectedColors));
        OnPropertyChanged(nameof(IsSingleColor));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Sensitivity));
        OnPropertyChanged(nameof(Smoothing));
    }

    /// <summary>
    /// Рисует следующий кадр превью: прошло seconds секунд. Пока превью рисуется, его звук слушается.
    /// </summary>
    public void Advance(double seconds)
    {
        if (_music == null)
        {
            StopPreview();
            return;
        }

        var settings = MusicSettings.From(_music);
        var inputs = new HashSet<AudioInput> { settings.Input };

        _sources.Require(this, new SourceDemand(new HashSet<string>(), inputs));

        _playback.Advance(settings, _sources.LevelsOf(settings.Input), seconds);
        _bitmap.Write(_playback.Render(settings, PreviewSpace));
    }

    /// <summary>
    /// Превью больше не видно: его звук больше не нужен.
    /// </summary>
    public void StopPreview()
    {
        _sources.Require(this, SourceDemand.None);
    }

    /// <summary>
    /// Перечитывает устройства звука, когда открывают выбор устройства.
    /// </summary>
    [RelayCommand]
    private void RefreshDevices()
    {
        _knownDevices.Clear();
        ShowDevices();
    }

    /// <summary>
    /// Показывает устройства выбранного источника; Windows опрашивается, только если их ещё не читали.
    /// </summary>
    private void ShowDevices()
    {
        var options = new List<AudioDeviceOption> { new("", "Устройство Windows по умолчанию") };

        if (_music != null)
        {
            if (!_knownDevices.TryGetValue(_music.Input, out var devices))
            {
                devices = AudioDevices.List(_music.Input);
                _knownDevices[_music.Input] = devices;
            }

            options.AddRange(devices.Select(d => new AudioDeviceOption(d.Id, d.Name)));

            // Выбранное устройство сейчас отключено: оно остаётся в списке, а звук пока берётся с устройства по умолчанию
            if (options.All(o => o.Id != _music.DeviceId))
                options.Add(new AudioDeviceOption(_music.DeviceId, "Отключённое устройство"));
        }

        Devices = options;
        OnPropertyChanged(nameof(SelectedDevice));
    }

    private void SetNumber(double value, Func<MusicConfig, int> get, Action<MusicConfig, int> set, [CallerMemberName] string? name = null)
    {
        var number = (int)Math.Round(value);

        if (_music == null || number == get(_music))
            return;

        set(_music, number);
        OnPropertyChanged(name);
        _changed();
    }
}
