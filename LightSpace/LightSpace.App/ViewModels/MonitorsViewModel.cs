using System.Collections.ObjectModel;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Sources;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Страница «Мониторы»: у каждого монитора своя лента, ориентация и раскладка. Когда группа ленты играет экран,
/// лента берёт цвета краёв своего монитора.
/// </summary>
public sealed class MonitorsViewModel
{
    private readonly AppConfig _config;
    private readonly DeviceManager _deviceManager;
    private readonly Action _changed;

    /// <summary>
    /// changed — привязка или раскладка поменялась: движку нужен новый снимок, настройки надо сохранить.
    /// </summary>
    public MonitorsViewModel(AppConfig config, DeviceManager deviceManager, Action changed)
    {
        _config = config;
        _deviceManager = deviceManager;
        _changed = changed;

        Refresh();
    }

    public ObservableCollection<MonitorItemViewModel> Items { get; } = new();

    /// <summary>
    /// Перечитывает мониторы и устройства: их могли подключить или отключить.
    /// </summary>
    public void Refresh()
    {
        var devices = new List<MonitorDeviceOption> { new(null, "Без ленты", 0) };

        devices.AddRange(_deviceManager.Devices.Select(d => new MonitorDeviceOption(d.Id, d.Name,
            d.Capabilities is { IsAddressable: true } capabilities ? capabilities.LedCount : 0)));

        Items.Clear();

        foreach (var info in Monitors.List())
            Items.Add(new MonitorItemViewModel(info, ConfigOf(info.Key), devices, OnDeviceChanged, _changed));
    }

    /// <summary>
    /// Настройки монитора; у нового монитора они появляются при первом показе.
    /// </summary>
    private MonitorConfig ConfigOf(string monitorKey)
    {
        var monitor = _config.Monitors.FirstOrDefault(m => m.MonitorKey == monitorKey);

        if (monitor == null)
        {
            monitor = new MonitorConfig { MonitorKey = monitorKey };
            _config.Monitors.Add(monitor);
        }

        return monitor;
    }

    /// <summary>
    /// Лента висит только вокруг одного монитора: выбранная для этого снимается с остальных.
    /// </summary>
    private void OnDeviceChanged(MonitorItemViewModel changed)
    {
        if (changed.Config.DeviceId == null)
            return;

        foreach (var monitor in _config.Monitors.Where(m => m != changed.Config && m.DeviceId == changed.Config.DeviceId))
            monitor.DeviceId = null;

        foreach (var item in Items.Where(i => i != changed && i.Config.DeviceId == null && i.Device.Id != null))
            item.ClearDevice();
    }
}
