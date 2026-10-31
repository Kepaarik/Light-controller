using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Devices.Tuya;

namespace LightSpace.Engine;

/// <summary>
/// Все известные устройства: поиск и подключение, включение и выключение подсветки, ключи устройств.
/// Методы вызываются из одного потока (потока окна); кадры в выводы устройств кладёт поток кадров.
/// </summary>
public sealed class DeviceManager : IDisposable
{
    private readonly AppConfig _config;
    private readonly DeviceDiscovery _discovery = new();
    private readonly List<ManagedDevice> _devices;

    private bool _isLightingEnabled;

    public DeviceManager(AppConfig config)
    {
        _config = config;
        _isLightingEnabled = config.LightingEnabled;

        // Устройства из прошлых запусков видны сразу, ещё до поиска
        _devices = config.Devices.Select(d => new ManagedDevice(d)).ToList();
    }

    /// <summary>
    /// Состав списка или состояние устройств изменились.
    /// </summary>
    public event EventHandler? DevicesChanged;

    /// <summary>
    /// Известные устройства: найденные сейчас и найденные в прошлые запуски.
    /// </summary>
    public IReadOnlyList<ManagedDevice> Devices => _devices;

    public bool IsSearching { get; private set; }

    /// <summary>
    /// Ищет устройства всеми способами подключения и подключает найденные. Возвращает ошибки способов, которые не сработали.
    /// </summary>
    public async Task<IReadOnlyList<string>> DiscoverAsync(CancellationToken ct = default)
    {
        if (IsSearching)
            return Array.Empty<string>();

        IsSearching = true;

        // Старые соединения закрываем до поиска: BLE-ленты не видны, пока к ним подключены, а COM-порт и USB заняты
        foreach (var device in _devices)
            device.Disconnect();

        DevicesChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var result = await _discovery.DiscoverAllAsync(ct);

            foreach (var device in result.Devices.OrderBy(d => d.Protocol).ThenBy(d => d.Name))
                Attach(device);

            return result.Errors;
        }
        finally
        {
            IsSearching = false;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Выключает подсветку всех устройств или включает её обратно с последними кадрами.
    /// </summary>
    public void SetLightingEnabled(bool isEnabled)
    {
        _isLightingEnabled = isEnabled;

        foreach (var device in _devices)
            device.Output?.SetEnabled(isEnabled);
    }

    /// <summary>
    /// Задаёт сетевой ключ устройства и набор его точек данных (нужно Tuya).
    /// </summary>
    public void SetNetworkKey(ManagedDevice device, string? localKey, TuyaDpScheme dataPoints)
    {
        device.Settings.LocalKey = string.IsNullOrWhiteSpace(localKey) ? null : localKey.Trim();
        device.Settings.DataPoints = dataPoints.ToString();

        ApplySettings(device);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Отключает устройство и убирает его из списка и настроек; при следующем поиске оно найдётся заново.
    /// </summary>
    public void Forget(ManagedDevice device)
    {
        device.Disconnect();
        _devices.Remove(device);
        _config.Devices.Remove(device.Settings);
        _config.RemoveFromGroups(device.Id);

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Набор точек данных Tuya из настроек устройства; по умолчанию — современный.
    /// </summary>
    public static TuyaDpScheme DataPointsOf(ManagedDevice device)
    {
        return Enum.TryParse<TuyaDpScheme>(device.Settings.DataPoints, out var scheme) ? scheme : TuyaDpScheme.V2;
    }

    public void Dispose()
    {
        foreach (var device in _devices)
            device.Disconnect();
    }

    private void Attach(LedDevice device)
    {
        var managed = _devices.FirstOrDefault(d => d.Id == device.Id);

        if (managed == null)
        {
            var settings = new DeviceConfig { Id = device.Id };

            _config.Devices.Add(settings);

            // Новое устройство ни в одну группу не входит — оно само себе группа
            _config.EnsureGroup(settings.Id);

            managed = new ManagedDevice(settings);
            _devices.Add(managed);
        }
        else if (managed.Device != null)
        {
            // Одно и то же устройство нашлось дважды — второе соединение не нужно
            device.Dispose();
            return;
        }

        managed.Connect(device, _isLightingEnabled);

        // Кадры подключённое устройство получает от потока кадров, как только окно отдаст ему новый снимок
        ApplySettings(managed);
    }

    /// <summary>
    /// Передаёт драйверу ручные настройки устройства из конфига.
    /// </summary>
    private static void ApplySettings(ManagedDevice device)
    {
        if (device.Device is TuyaDevice tuya)
        {
            tuya.LocalKey = device.Settings.LocalKey;
            tuya.DpScheme = DataPointsOf(device);
        }
    }
}
