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
    /// Частоты кадров, из которых можно выбрать.
    /// </summary>
    public static readonly IReadOnlyList<int> FrameRates = new[] { 20, 30, 60 };

    /// <summary>
    /// Версия формата, в котором записан файл настроек.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Подсветка всех устройств включена.
    /// </summary>
    public bool LightingEnabled { get; set; } = true;

    /// <summary>
    /// Сколько кадров в секунду уходит на устройства: 20, 30 или 60.
    /// </summary>
    public int FrameRate { get; set; } = 30;

    /// <summary>
    /// Длительность плавного перехода между сценариями, в миллисекундах.
    /// </summary>
    public int TransitionMilliseconds { get; set; } = 400;

    /// <summary>
    /// Через сколько секунд после потери фокуса снимается фокусный сценарий.
    /// </summary>
    public double FocusReleaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Запускать приложение вместе с Windows.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Закрытие окна прячет его в трей, а не завершает приложение.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    /// Устройства, которые уже находились хотя бы раз, в порядке их появления.
    /// </summary>
    public List<DeviceConfig> Devices { get; set; } = new();

    /// <summary>
    /// Группы устройств, включая группы-одиночки: каждое известное устройство входит ровно в одну группу.
    /// </summary>
    public List<GroupConfig> Groups { get; set; } = new();

    /// <summary>
    /// Все сценарии; группы ссылаются на них по идентификаторам.
    /// </summary>
    public List<ScenarioConfig> Scenarios { get; set; } = new();

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        if (!FrameRates.Contains(FrameRate))
            FrameRate = 30;

        TransitionMilliseconds = Math.Clamp(TransitionMilliseconds, 0, 5000);
        FocusReleaseDelaySeconds = Math.Clamp(FocusReleaseDelaySeconds, 0, 30);

        // Устройство без идентификатора не узнать при поиске, а повторы одного устройства путали бы его настройки
        Devices = Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .ToList();

        foreach (var device in Devices)
            device.Placement?.Normalize();

        // Пустые разделы в файле дали бы null вместо содержимого и настроек эффектов
        NormalizeScenarios();
        NormalizeGroups();
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
    /// что играла группа первого устройства, с той же яркостью и теми же сценариями.
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
            ScenarioIds = source?.ScenarioIds.ToList() ?? new List<string>(),
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

    /// <summary>
    /// Удаляет сценарий: он пропадает из списка сценариев и из всех групп, где стоял.
    /// </summary>
    public void RemoveScenario(ScenarioConfig scenario)
    {
        Scenarios.Remove(scenario);

        foreach (var group in Groups)
            group.ScenarioIds.Remove(scenario.Id);
    }

    /// <summary>
    /// Группа-одиночка с теми же содержимым, яркостью и сценариями, что у группы.
    /// </summary>
    private static GroupConfig SoloCopyOf(GroupConfig group, string deviceId)
    {
        var solo = GroupConfig.Solo(deviceId, group.DefaultContent.Clone(), group.Brightness);

        solo.ScenarioIds = group.ScenarioIds.ToList();

        return solo;
    }

    private void NormalizeScenarios()
    {
        Scenarios ??= new List<ScenarioConfig>();

        // Сценарий без идентификатора не найти из групп, а повтор идентификатора путал бы группы
        Scenarios = Scenarios
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id))
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .ToList();

        foreach (var scenario in Scenarios)
        {
            scenario.Name ??= "";
            scenario.Condition ??= new ScenarioCondition();
            scenario.Condition.ExePath ??= "";
            scenario.Content ??= new ContentConfig();
            scenario.Content.Normalize();

            // Режим, которого нет в этой сборке, мог попасть в файл из другой версии или из правки руками
            if (!Enum.IsDefined(scenario.Condition.Mode))
                scenario.Condition.Mode = ConditionMode.Focused;
        }
    }

    /// <summary>
    /// Каждое известное устройство — ровно в одной группе, в группе-одиночке — ровно одно устройство,
    /// пустых групп и ссылок на неизвестные устройства и сценарии нет.
    /// </summary>
    private void NormalizeGroups()
    {
        var devices = Devices.Select(d => d.Id).ToHashSet();
        var scenarios = Scenarios.Select(s => s.Id).ToHashSet();
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

            group.ScenarioIds = (group.ScenarioIds ?? new List<string>()).Where(scenarios.Contains).Distinct().ToList();
            group.Name ??= "";
            group.Color ??= "";
            group.Brightness = Math.Clamp(group.Brightness, 0, 100);
            group.DefaultContent ??= new ContentConfig();
            group.DefaultContent.Normalize();
        }

        Groups.RemoveAll(g => g.DeviceIds.Count == 0);

        foreach (var device in Devices)
            EnsureGroup(device.Id);
    }
}
