namespace LightSpace.Core.Config;

/// <summary>
/// Пользовательские настройки Light Space, сохраняемые между запусками.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Версия формата настроек, которую понимает эта сборка приложения.
    /// </summary>
    public const int CurrentVersion = 5;

    /// <summary>
    /// Версия формата, в котором записан файл настроек.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Подсветка всех устройств включена.
    /// </summary>
    public bool LightingEnabled { get; set; } = true;

    /// <summary>
    /// Запускать приложение вместе с Windows.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Окно из прозрачного стекла (Acrylic); иначе — матовое (Mica).
    /// </summary>
    public bool UseAcrylic { get; set; } = true;

    /// <summary>
    /// Левое меню окна свёрнуто до значков.
    /// </summary>
    public bool IsMenuCollapsed { get; set; }

    /// <summary>
    /// Тонировка и прозрачность стекла окна.
    /// </summary>
    public GlassConfig Glass { get; set; } = new();

    /// <summary>
    /// Устройства, которые уже находились хотя бы раз, в порядке их появления.
    /// </summary>
    public List<DeviceConfig> Devices { get; set; } = new();

    /// <summary>
    /// Яркость и содержимое каждого известного устройства: по одной записи на устройство.
    /// </summary>
    public List<DeviceLightingConfig> DeviceLighting { get; set; } = new();

    /// <summary>
    /// Сцена, которая сейчас играет: копия сцены последнего применённого профиля. Её устройства играют сцену
    /// вместо своей подсветки. null — сцена не играет.
    /// </summary>
    public SceneConfig? Scene { get; set; }

    /// <summary>
    /// Подсветка мониторов: ленты вокруг мониторов, повороты мониторов и раскладки лент.
    /// </summary>
    public List<MonitorConfig> Monitors { get; set; } = new();

    /// <summary>
    /// Общая яркость всей подсветки, 0–100 %: умножается на яркость каждого устройства.
    /// </summary>
    public int MasterBrightness { get; set; } = 100;

    /// <summary>
    /// Сохранённые профили подсветки в порядке их создания.
    /// </summary>
    public List<ProfileConfig> Profiles { get; set; } = new();

    /// <summary>
    /// Сохраняет то, что сейчас играет на устройствах, новым профилем — вместе со сценой, если она играет.
    /// </summary>
    public ProfileConfig SaveProfile(string name)
    {
        var profile = new ProfileConfig
        {
            Id = ProfileConfig.NewId(),
            Name = name,
            DeviceLighting = CurrentLighting(),
            Scene = Scene?.Clone()
        };

        Profiles.Add(profile);

        return profile;
    }

    /// <summary>
    /// Записывает в профиль то, что сейчас играет на устройствах, вместо сохранённого раньше. Сцену профиля это
    /// не трогает: её собирают в самом профиле.
    /// </summary>
    public void UpdateProfile(ProfileConfig profile)
    {
        profile.DeviceLighting = CurrentLighting();
    }

    // Копия яркости и содержимого каждого устройства: правки профиля не меняют то, что играет
    private List<DeviceLightingConfig> CurrentLighting()
    {
        return DeviceLighting.Select(l => l.Clone()).ToList();
    }

    /// <summary>
    /// Возвращает устройствам яркость и содержимое из профиля и включает его сцену; сцена прежнего профиля
    /// перестаёт играть. Устройства, которых в профиле нет, играют своё; забытые с тех пор устройства пропускаются.
    /// </summary>
    public void ApplyProfile(ProfileConfig profile)
    {
        var devices = Devices.Select(d => d.Id).ToHashSet();

        foreach (var saved in profile.DeviceLighting.Where(l => devices.Contains(l.DeviceId)))
        {
            var lighting = EnsureDeviceLighting(saved.DeviceId);

            lighting.Brightness = saved.Brightness;
            lighting.Content = saved.Content.Clone();
        }

        Scene = profile.Scene?.Clone();
        Scene?.Devices.RemoveAll(d => !devices.Contains(d.DeviceId));
    }

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        // Пустой раздел стекла в файле дал бы null вместо настроек по умолчанию
        Glass ??= new GlassConfig();
        Glass.Normalize();

        // Устройство без идентификатора не узнать при поиске, а повторы одного устройства путали бы его настройки
        Devices = Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .ToList();

        // Пустые разделы в файле дали бы null вместо содержимого и настроек эффектов
        NormalizeDeviceLighting();
        NormalizeMonitors();

        // Забытое устройство сцену не играет
        Scene?.Normalize();
        Scene?.Devices.RemoveAll(d => Devices.All(known => known.Id != d.DeviceId));

        MasterBrightness = Math.Clamp(MasterBrightness, 0, 100);

        Profiles = (Profiles ?? new List<ProfileConfig>()).Where(p => p != null).ToList();

        foreach (var profile in Profiles)
            profile.Normalize();
    }

    /// <summary>
    /// Яркость и содержимое устройства; у нового устройства они появляются при первом обращении.
    /// </summary>
    public DeviceLightingConfig EnsureDeviceLighting(string deviceId)
    {
        var lighting = DeviceLighting.FirstOrDefault(l => l.DeviceId == deviceId);

        if (lighting != null)
            return lighting;

        lighting = DeviceLightingConfig.For(deviceId, new ContentConfig(), 100);
        DeviceLighting.Add(lighting);

        return lighting;
    }

    /// <summary>
    /// Убирает подсветку забытого устройства; из сцены, которая сейчас играет, оно тоже уходит.
    /// </summary>
    public void RemoveDeviceLighting(string deviceId)
    {
        DeviceLighting.RemoveAll(l => l.DeviceId == deviceId);
        Scene?.Devices.RemoveAll(d => d.DeviceId == deviceId);
    }

    /// <summary>
    /// У монитора одна запись, а у ленты — один монитор; ссылки на забытые устройства убираются.
    /// </summary>
    private void NormalizeMonitors()
    {
        var devices = Devices.Select(d => d.Id).ToHashSet();
        var bound = new HashSet<string>();

        Monitors = (Monitors ?? new List<MonitorConfig>())
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.MonitorKey))
            .GroupBy(m => m.MonitorKey)
            .Select(g => g.First())
            .ToList();

        foreach (var monitor in Monitors)
        {
            monitor.Normalize();

            if (monitor.DeviceId != null && (!devices.Contains(monitor.DeviceId) || !bound.Add(monitor.DeviceId)))
                monitor.DeviceId = null;
        }
    }

    /// <summary>
    /// У каждого известного устройства ровно одна запись подсветки, записей забытых устройств нет.
    /// </summary>
    private void NormalizeDeviceLighting()
    {
        var devices = Devices.Select(d => d.Id).ToHashSet();
        var taken = new HashSet<string>();

        DeviceLighting = (DeviceLighting ?? new List<DeviceLightingConfig>())
            .Where(l => l?.DeviceId != null && devices.Contains(l.DeviceId) && taken.Add(l.DeviceId))
            .ToList();

        foreach (var lighting in DeviceLighting)
            lighting.Normalize();

        foreach (var device in Devices)
            EnsureDeviceLighting(device.Id);
    }
}
