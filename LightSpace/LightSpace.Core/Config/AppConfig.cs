namespace LightSpace.Core.Config;

/// <summary>
/// Пользовательские настройки Light Space, сохраняемые между запусками.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Версия формата настроек, которую понимает эта сборка приложения.
    /// </summary>
    public const int CurrentVersion = 4;

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
    /// Группы устройств, включая группы-одиночки: каждое известное устройство входит ровно в одну группу.
    /// </summary>
    public List<GroupConfig> Groups { get; set; } = new();

    /// <summary>
    /// Что играет на устройствах: вся сцена, группы или каждое устройство отдельно. Настройки остальных режимов
    /// сохраняются и снова играют, когда к ним возвращаются.
    /// </summary>
    public SceneMode Mode { get; set; }

    /// <summary>
    /// Яркость и содержимое всей сцены. Своего списка устройств у неё нет: в неё входят все.
    /// </summary>
    public GroupConfig Scene { get; set; } = NewScene();

    /// <summary>
    /// Яркость и содержимое каждого устройства в режиме «Устройства»: по одной группе-одиночке на устройство.
    /// </summary>
    public List<GroupConfig> DeviceLighting { get; set; } = new();

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
    /// Сохраняет то, что сейчас играет на устройствах, новым профилем.
    /// </summary>
    public ProfileConfig SaveProfile(string name)
    {
        var profile = new ProfileConfig
        {
            Id = GroupConfig.NewId(),
            Name = name,
            DeviceLighting = DeviceLighting.Select(l => GroupConfig.Solo(l.DeviceIds[0], l.DefaultContent.Clone(), l.Brightness)).ToList()
        };

        Profiles.Add(profile);

        return profile;
    }

    /// <summary>
    /// Возвращает устройствам яркость и содержимое из профиля. Устройства, которых в профиле нет, играют своё;
    /// забытые с тех пор устройства пропускаются.
    /// </summary>
    public void ApplyProfile(ProfileConfig profile)
    {
        var devices = Devices.Select(d => d.Id).ToHashSet();

        foreach (var saved in profile.DeviceLighting.Where(l => devices.Contains(l.DeviceIds[0])))
        {
            var lighting = EnsureDeviceLighting(saved.DeviceIds[0]);

            lighting.Brightness = saved.Brightness;
            lighting.DefaultContent = saved.DefaultContent.Clone();
        }
    }

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        // Режим, которого нет в этой сборке, мог попасть в файл из правки руками
        if (!Enum.IsDefined(Mode))
            Mode = SceneMode.Groups;

        // Пустой раздел стекла в файле дал бы null вместо настроек по умолчанию
        Glass ??= new GlassConfig();
        Glass.Normalize();

        // Устройство без идентификатора не узнать при поиске, а повторы одного устройства путали бы его настройки
        Devices = Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .ToList();

        foreach (var device in Devices)
            device.Placement?.Normalize();

        // Пустые разделы в файле дали бы null вместо содержимого и настроек эффектов
        NormalizeGroups();
        NormalizeDeviceLighting();
        NormalizeMonitors();

        MasterBrightness = Math.Clamp(MasterBrightness, 0, 100);

        Profiles = (Profiles ?? new List<ProfileConfig>()).Where(p => p != null).ToList();

        foreach (var profile in Profiles)
            profile.Normalize();
    }

    /// <summary>
    /// То, что сейчас играет на устройствах: вся сцена, группы или каждое устройство отдельно.
    /// </summary>
    public IReadOnlyList<GroupConfig> PlayingGroups()
    {
        return Mode switch
        {
            SceneMode.WholeScene => new[] { Scene },
            SceneMode.Devices => DeviceLighting,
            _ => Groups
        };
    }

    /// <summary>
    /// Яркость и содержимое устройства в режиме «Устройства»; у нового устройства они появляются при первом обращении.
    /// </summary>
    public GroupConfig EnsureDeviceLighting(string deviceId)
    {
        var lighting = DeviceLighting.FirstOrDefault(g => g.DeviceIds.Contains(deviceId));

        if (lighting != null)
            return lighting;

        lighting = GroupConfig.Solo(deviceId, new ContentConfig(), 100);
        DeviceLighting.Add(lighting);

        return lighting;
    }

    /// <summary>
    /// Группа, в которую входит устройство; null — устройство ещё ни в одну группу не входит.
    /// </summary>
    public GroupConfig? GroupOf(string deviceId)
    {
        return Groups.FirstOrDefault(g => g.DeviceIds.Contains(deviceId));
    }

    /// <summary>
    /// Группа устройства; устройство без группы становится группой-одиночкой с содержимым по умолчанию.
    /// </summary>
    public GroupConfig EnsureGroup(string deviceId)
    {
        var group = GroupOf(deviceId);

        if (group != null)
            return group;

        group = GroupConfig.Solo(deviceId, new ContentConfig(), 100);
        Groups.Add(group);

        return group;
    }

    /// <summary>
    /// Убирает устройство из его группы, например когда устройство забыли; опустевшая группа пропадает.
    /// </summary>
    public void RemoveFromGroups(string deviceId)
    {
        foreach (var group in Groups)
            group.DeviceIds.Remove(deviceId);

        Groups.RemoveAll(g => g.DeviceIds.Count == 0);
    }

    /// <summary>
    /// Собирает устройства в новую группу. Устройства уходят из прежних групп; новая группа играет то же,
    /// что играла группа первого устройства, с той же яркостью.
    /// </summary>
    public GroupConfig MakeGroup(IReadOnlyCollection<string> deviceIds, string name, string color)
    {
        var source = deviceIds.Select(GroupOf).FirstOrDefault(g => g != null);

        var group = new GroupConfig
        {
            Id = GroupConfig.NewId(),
            Name = name,
            Color = color,
            Brightness = source?.Brightness ?? 100,
            DefaultContent = source?.DefaultContent.Clone() ?? new ContentConfig()
        };

        foreach (var id in deviceIds)
            RemoveFromGroups(id);

        // Устройства группы идут в том же порядке, что и в списке устройств
        group.DeviceIds = Devices.Select(d => d.Id).Where(id => deviceIds.Contains(id)).ToList();
        Groups.Add(group);

        return group;
    }

    /// <summary>
    /// Распускает группу: каждое её устройство становится группой-одиночкой и продолжает играть то же, что играла группа.
    /// </summary>
    public void Ungroup(GroupConfig group)
    {
        Groups.Remove(group);

        foreach (var id in group.DeviceIds)
            Groups.Add(SoloCopyOf(group, id));
    }

    /// <summary>
    /// Убирает устройство из его группы: оно становится группой-одиночкой и продолжает играть то же, что играла группа.
    /// Если в группе больше никого не осталось, группа пропадает.
    /// </summary>
    public void TakeOutOfGroup(string deviceId)
    {
        var group = GroupOf(deviceId);

        if (group == null || group.IsSolo)
            return;

        RemoveFromGroups(deviceId);
        Groups.Add(SoloCopyOf(group, deviceId));
    }

    private static GroupConfig NewScene()
    {
        return new GroupConfig { Id = "scene", Name = "Вся сцена" };
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
    /// Группа-одиночка с теми же содержимым и яркостью, что у группы.
    /// </summary>
    private static GroupConfig SoloCopyOf(GroupConfig group, string deviceId)
    {
        return GroupConfig.Solo(deviceId, group.DefaultContent.Clone(), group.Brightness);
    }

    /// <summary>
    /// В режиме «Устройства» у каждого известного устройства ровно одна запись, записей забытых устройств нет.
    /// </summary>
    private void NormalizeDeviceLighting()
    {
        var devices = Devices.Select(d => d.Id).ToHashSet();
        var taken = new HashSet<string>();

        DeviceLighting = (DeviceLighting ?? new List<GroupConfig>())
            .Where(g => g?.DeviceIds is { Count: 1 } && devices.Contains(g.DeviceIds[0]) && taken.Add(g.DeviceIds[0]))
            .ToList();

        foreach (var lighting in DeviceLighting)
        {
            if (string.IsNullOrWhiteSpace(lighting.Id))
                lighting.Id = GroupConfig.NewId();

            lighting.IsSolo = true;
            lighting.Name = "";
            lighting.Color = "";
            lighting.Brightness = Math.Clamp(lighting.Brightness, 0, 100);
            lighting.DefaultContent ??= new ContentConfig();
            lighting.DefaultContent.Normalize();
        }

        foreach (var device in Devices)
            EnsureDeviceLighting(device.Id);
    }

    /// <summary>
    /// Каждое известное устройство — ровно в одной группе, в группе-одиночке — ровно одно устройство,
    /// пустых групп и ссылок на неизвестные устройства нет.
    /// </summary>
    private void NormalizeGroups()
    {
        var devices = Devices.Select(d => d.Id).ToHashSet();
        var taken = new HashSet<string>();
        var ids = new HashSet<string>();

        Groups = (Groups ?? new List<GroupConfig>()).Where(g => g != null).ToList();

        foreach (var group in Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || !ids.Add(group.Id))
            {
                group.Id = GroupConfig.NewId();
                ids.Add(group.Id);
            }

            // Устройство, которое уже попало в группу выше, из остальных групп уходит
            group.DeviceIds = (group.DeviceIds ?? new List<string>()).Where(id => devices.Contains(id) && taken.Add(id)).ToList();

            // Лишние устройства группы-одиночки ниже получат свои группы-одиночки
            if (group.IsSolo && group.DeviceIds.Count > 1)
            {
                taken.ExceptWith(group.DeviceIds.Skip(1));
                group.DeviceIds.RemoveRange(1, group.DeviceIds.Count - 1);
            }

            group.Name ??= "";
            group.Color ??= "";
            group.Brightness = Math.Clamp(group.Brightness, 0, 100);
            group.DefaultContent ??= new ContentConfig();
            group.DefaultContent.Normalize();
        }

        Groups.RemoveAll(g => g.DeviceIds.Count == 0);

        foreach (var device in Devices)
            EnsureGroup(device.Id);

        // Вся сцена — не группа из списка: у неё постоянный идентификатор, нет своих устройств и обводки
        var scene = Scene ?? NewScene();
        var defaults = NewScene();

        scene.Id = defaults.Id;
        scene.IsSolo = false;
        scene.Name = defaults.Name;
        scene.Color = "";
        scene.DeviceIds = new List<string>();
        scene.Brightness = Math.Clamp(scene.Brightness, 0, 100);
        scene.DefaultContent ??= new ContentConfig();
        scene.DefaultContent.Normalize();

        Scene = scene;
    }
}
