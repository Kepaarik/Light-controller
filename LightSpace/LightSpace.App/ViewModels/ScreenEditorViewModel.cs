using System.Runtime.CompilerServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Controls;
using LightSpace.Core.Config;
using LightSpace.Engine.Sources;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Монитор в выпадающей панели выбора монитора.
/// </summary>
/// <param name="Key">Системное имя монитора; пусто — основной монитор Windows.</param>
public sealed record MonitorOption(string Key, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// Редактор содержимого-экрана: монитор, насыщенность, яркость, сглаживание и живое превью картинки монитора
/// тем же рендером, что и кадры устройств. Все правки сразу попадают в содержимое.
/// </summary>
public sealed partial class ScreenEditorViewModel : ObservableObject
{
    private const int PreviewColumns = 32;
    private const int PreviewRows = 18;

    // Под этим именем превью помнит свои сглаженные цвета
    private const string PreviewKey = "preview";

    private static readonly DeviceGeometry PreviewSpace = DeviceGeometry.Grid(PreviewColumns, PreviewRows);

    private readonly SourceHub _sources;
    private readonly Action _changed;
    private readonly ColorBitmap _bitmap = new(PreviewColumns, PreviewRows);

    private ScreenConfig? _screen;
    private ScreenPlayback _playback = new();
    private IReadOnlyList<MonitorOption> _monitors = Array.Empty<MonitorOption>();

    /// <summary>
    /// sources — откуда превью берёт картинку монитора; changed вызывается после каждой правки.
    /// </summary>
    public ScreenEditorViewModel(SourceHub sources, Action changed)
    {
        _sources = sources;
        _changed = changed;

        RefreshMonitors();
    }

    /// <summary>
    /// Кисть с текущим кадром превью.
    /// </summary>
    public Brush Preview => _bitmap.Brush;

    /// <summary>
    /// Мониторы для выбора: основной монитор Windows и все подключённые.
    /// </summary>
    public IReadOnlyList<MonitorOption> Monitors
    {
        get => _monitors;
        private set => SetProperty(ref _monitors, value);
    }

    /// <summary>
    /// Монитор, с которого берётся картинка.
    /// </summary>
    public MonitorOption? SelectedMonitor
    {
        get => _screen == null ? null : _monitors.FirstOrDefault(m => m.Key == _screen.MonitorKey);
        set
        {
            // Выпадающая панель сбрасывает выбор в null, когда пересобирает список, — монитор от этого не меняется
            if (value == null || _screen == null || value.Key == _screen.MonitorKey)
                return;

            _screen.MonitorKey = value.Key;
            OnPropertyChanged();
            _changed();
        }
    }

    public double Saturation
    {
        get => _screen?.Saturation ?? ScreenConfig.DefaultSaturation;
        set => SetNumber(value, s => s.Saturation, (s, v) => s.Saturation = v);
    }

    public double Brightness
    {
        get => _screen?.Brightness ?? 100;
        set => SetNumber(value, s => s.Brightness, (s, v) => s.Brightness = v);
    }

    public double Smoothing
    {
        get => _screen?.Smoothing ?? 0;
        set => SetNumber(value, s => s.Smoothing, (s, v) => s.Smoothing = v);
    }

    /// <summary>
    /// Открывает содержимое для правки; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content)
    {
        _screen = content?.Screen;

        // Превью другого содержимого начинает сглаживание заново
        _playback = new ScreenPlayback();

        RefreshMonitors();
        OnPropertyChanged(nameof(Saturation));
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(Smoothing));
    }

    /// <summary>
    /// Рисует следующий кадр превью: прошло seconds секунд. Пока превью рисуется, его монитор снимается.
    /// </summary>
    public void Advance(double seconds)
    {
        if (_screen == null)
        {
            StopPreview();
            return;
        }

        var settings = ScreenSettings.From(_screen);
        var monitors = new HashSet<string> { settings.MonitorKey };

        _sources.Require(this, new SourceDemand(monitors, new HashSet<AudioInput>()));

        _playback.Advance(settings, _sources.PictureOf(settings.MonitorKey), seconds);
        _bitmap.Write(_playback.Render(PreviewKey, settings, PreviewSpace.Points, PreviewSpace.Width, PreviewSpace.Height));
    }

    /// <summary>
    /// Превью больше не видно: его монитор больше не нужен.
    /// </summary>
    public void StopPreview()
    {
        _sources.Require(this, SourceDemand.None);
    }

    /// <summary>
    /// Перечитывает подключённые мониторы, когда открывают выбор монитора.
    /// </summary>
    [RelayCommand]
    private void RefreshMonitors()
    {
        var options = new List<MonitorOption> { new("", "Основной монитор Windows") };

        foreach (var monitor in Engine.Sources.Monitors.List())
        {
            var title = $"Монитор {monitor.Number} · {monitor.Width}×{monitor.Height}";

            options.Add(new MonitorOption(monitor.Key, monitor.IsPrimary ? title + " · основной" : title));
        }

        // Выбранный монитор сейчас отключён: он остаётся в списке, а картинка пока берётся с основного
        if (_screen != null && options.All(o => o.Key != _screen.MonitorKey))
            options.Add(new MonitorOption(_screen.MonitorKey, "Отключённый монитор"));

        Monitors = options;
        OnPropertyChanged(nameof(SelectedMonitor));
    }

    private void SetNumber(double value, Func<ScreenConfig, int> get, Action<ScreenConfig, int> set, [CallerMemberName] string? name = null)
    {
        var number = (int)Math.Round(value);

        if (_screen == null || number == get(_screen))
            return;

        set(_screen, number);
        OnPropertyChanged(name);
        _changed();
    }
}
