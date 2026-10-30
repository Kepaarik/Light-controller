using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.App.Services;
using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Страница «Настройки»: каждое изменение сразу попадает в конфиг и сохраняется в файл.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    // Ползунок меняет значение на каждом шаге — пишем файл, только когда его отпустили
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly AppConfig _config;
    private readonly DispatcherTimer _saveTimer;

    public SettingsViewModel(AppConfig config)
    {
        _config = config;

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    /// <summary>
    /// Частоты кадров для сегментов.
    /// </summary>
    public IReadOnlyList<int> FrameRates => AppConfig.FrameRates;

    public bool LightingEnabled
    {
        get => _config.LightingEnabled;
        set => Update(_config.LightingEnabled, value, (c, v) => c.LightingEnabled = v);
    }

    public int FrameRate
    {
        get => _config.FrameRate;
        set => Update(_config.FrameRate, value, (c, v) => c.FrameRate = v);
    }

    public double TransitionMilliseconds
    {
        get => _config.TransitionMilliseconds;
        set => Update(_config.TransitionMilliseconds, (int)Math.Round(value), (c, v) => c.TransitionMilliseconds = v);
    }

    public double FocusReleaseDelaySeconds
    {
        get => _config.FocusReleaseDelaySeconds;
        set => Update(_config.FocusReleaseDelaySeconds, Math.Round(value, 1), (c, v) => c.FocusReleaseDelaySeconds = v);
    }

    public bool StartWithWindows
    {
        get => _config.StartWithWindows;
        set
        {
            if (Update(_config.StartWithWindows, value, (c, v) => c.StartWithWindows = v))
                StartupRegistration.Apply(value);
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _config.MinimizeToTrayOnClose;
        set => Update(_config.MinimizeToTrayOnClose, value, (c, v) => c.MinimizeToTrayOnClose = v);
    }

    /// <summary>
    /// Сохраняет отложенные изменения сразу, например перед выходом.
    /// </summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        ConfigStore.Save(_config);
    }

    private bool Update<T>(T oldValue, T newValue, Action<AppConfig, T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(oldValue, newValue, _config, apply, propertyName))
            return false;

        _saveTimer.Stop();
        _saveTimer.Start();

        return true;
    }
}
