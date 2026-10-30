using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Devices.Tuya;
using LightSpace.Engine.Output;

namespace LightSpace.Engine;

/// <summary>
/// Все известные устройства: поиск и подключение, включение и выключение подсветки, ручной цвет, ключи устройств.
/// Методы вызываются из одного потока (потока окна); кадры в выводы устройств можно класть из любого.
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
    /// Задаёт устройству один цвет и запоминает его; не подключённое устройство получит цвет при следующем подключении.
    /// </summary>
    public void SetColor(ManagedDevice device, RgbColor color, int brightness)
    {
        device.Settings.Color = color.ToHex();
        device.Settings.Brightness = Math.Clamp(brightness, 0, 100);

        SendSavedColor(device);
    }

    /// <summary>
    /// Задаёт сетевой ключ устройства и набор его точек данных (нужно Tuya), затем повторяет сохранённый цвет.
    /// </summary>
    public void SetNetworkKey(ManagedDevice device, string? localKey, TuyaDpScheme dataPoints)
    {
        device.Settings.LocalKey = string.IsNullOrWhiteSpace(localKey) ? null : localKey.Trim();
        device.Settings.DataPoints = dataPoints.ToString();

        ApplySettings(device);
        SendSavedColor(device);
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

        ApplySettings(managed);
        SendSavedColor(managed);
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

    private static void SendSavedColor(ManagedDevice device)
    {
        if (device.Output == null || !device.Output.Device.IsReady || !TryParseColor(device.Settings.Color, out var color))
            return;

        device.Output.Submit(OutputFrame.Solid(color, device.Settings.Brightness));
    }

    private static bool TryParseColor(string? hex, out RgbColor color)
    {
        color = RgbColor.White;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            color = RgbColor.Parse(hex);
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            // Цвет в файле настроек могли испортить руками — такое устройство просто не получает сохранённый цвет
            return false;
        }
    }
}
