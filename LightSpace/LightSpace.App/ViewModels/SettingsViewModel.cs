using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Страница «Настройки»: каждое изменение сразу попадает в конфиг и сохраняется в файл.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
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

        Glass = new GlassViewModel(config.Glass, ScheduleSave);
    }

    /// <summary>
    /// Тонировка и прозрачность стекла окна.
    /// </summary>
    public GlassViewModel Glass { get; }

    /// <summary>
    /// Скрытый раздел с подробным журналом устройств.
    /// </summary>
    public DeviceLogViewModel Log { get; } = new();

    /// <summary>
    /// Системное стекло есть в этой версии Windows.
    /// </summary>
    public bool IsAcrylicSupported => WindowBackdrop.IsSupported;

    /// <summary>
    /// Окно из прозрачного стекла (Acrylic); иначе — матовое (Mica).
    /// </summary>
    public bool UseAcrylic
    {
        get => _config.UseAcrylic;
        set => Update(_config.UseAcrylic, value, (c, v) => c.UseAcrylic = v);
    }

    public bool LightingEnabled
    {
        get => _config.LightingEnabled;
        set => Update(_config.LightingEnabled, value, (c, v) => c.LightingEnabled = v);
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

    /// <summary>
    /// Версия этой сборки для карточки «О приложении».
    /// </summary>
    public string VersionText => $"Версия {UpdateCheck.Current}";

    /// <summary>
    /// Найденный новый релиз; null — обновлений нет или их ещё не проверяли.
    /// </summary>
    public AvailableUpdate? FoundUpdate { get; private set; }

    public bool HasUpdate => FoundUpdate != null;

    public string UpdateText => FoundUpdate is { } update ? $"Доступна версия {update.Version}" : "";

    /// <summary>
    /// Проверяет, не вышла ли новая версия; ошибки сети молча пропускаются.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        FoundUpdate = await UpdateCheck.FindAsync();

        OnPropertyChanged(nameof(FoundUpdate));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateText));
    }

    /// <summary>
    /// Открывает в браузере страницу найденного релиза.
    /// </summary>
    [RelayCommand]
    private void OpenUpdate()
    {
        if (FoundUpdate != null)
            Process.Start(new ProcessStartInfo(FoundUpdate.PageUrl) { UseShellExecute = true });
    }

    /// <summary>
    /// Сохраняет отложенные изменения сразу, например перед выходом.
    /// </summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        ConfigStore.Save(_config);
    }

    /// <summary>
    /// Сохраняет настройки чуть позже: частые изменения подряд дают одну запись в файл.
    /// </summary>
    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private bool Update<T>(T oldValue, T newValue, Action<AppConfig, T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(oldValue, newValue, _config, apply, propertyName))
            return false;

        ScheduleSave();

        return true;
    }
}
