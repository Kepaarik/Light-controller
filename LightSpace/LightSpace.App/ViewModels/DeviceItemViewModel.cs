using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Devices;
using LightSpace.Devices.Tuya;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Устройство в списке вкладки «Устройства»: состояние связи, возможности, ключ и диагностика вывода.
/// </summary>
public sealed partial class DeviceItemViewModel : ObservableObject
{
    // Свойства, которые меняются сами по себе, без действий пользователя: связь, адрес, возможности, диагностика
    private static readonly string[] LiveProperties =
    {
        nameof(Name), nameof(Protocol), nameof(Address), nameof(Status), nameof(StatusTitle), nameof(StatusText), nameof(IsConnected),
        nameof(KindText), nameof(LedCountText), nameof(FrameRateText), nameof(Keys), nameof(HasKeys),
        nameof(SentFramesText), nameof(DroppedFramesText), nameof(LastSendText), nameof(LastErrorText)
    };

    private readonly DeviceManager _manager;
    private readonly Action _settingsChanged;

    [ObservableProperty]
    private string _localKey;

    [ObservableProperty]
    private TuyaDpScheme _dataPoints;

    public DeviceItemViewModel(ManagedDevice device, DeviceManager manager, Action settingsChanged)
    {
        Device = device;
        _manager = manager;
        _settingsChanged = settingsChanged;

        _localKey = device.Settings.LocalKey ?? "";
        _dataPoints = DeviceManager.DataPointsOf(device);
    }

    public ManagedDevice Device { get; }

    public string Name => Device.Name;

    public string Protocol => Device.Protocol;

    public string Address => Device.Address;

    public DeviceStatus Status => Device.Status;

    public bool IsConnected => Device.Device != null;

    public string StatusTitle => Status switch
    {
        DeviceStatus.Connected => "Подключено",
        DeviceStatus.NeedsSetup => "Нужна настройка",
        DeviceStatus.Failing => "Нет связи",
        _ => "Не найдено"
    };

    /// <summary>
    /// Чего не хватает устройству или почему не дошла команда; пусто, если всё в порядке.
    /// </summary>
    public string StatusText => Device.StatusText ?? "";

    /// <summary>
    /// Что это за устройство по возможностям: клавиатура, адресная лента или лента одного цвета.
    /// </summary>
    public string KindText => Device.Capabilities switch
    {
        null => "Станет известно после подключения",
        { IsKeyboard: true } => "Клавиатура, своя подсветка каждой клавиши",
        { IsAddressable: true } => "Адресная лента, свой цвет каждого светодиода",
        _ => "Один цвет на всё устройство"
    };

    public string LedCountText => Device.Capabilities is { IsAddressable: true } capabilities ? capabilities.LedCount.ToString() : "—";

    public string FrameRateText => Device.Capabilities is { } capabilities ? $"до {capabilities.MaxFramesPerSecond} к/с" : "—";

    public IReadOnlyList<KeyboardKey> Keys => Device.Capabilities?.Keys ?? Array.Empty<KeyboardKey>();

    public bool HasKeys => Keys.Count > 0;

    /// <summary>
    /// Устройству нужен локальный ключ для управления по сети (Tuya).
    /// </summary>
    public bool NeedsNetworkKey => Device.Device is TuyaDevice || Device.Id.StartsWith("tuya:", StringComparison.Ordinal);

    public IReadOnlyList<TuyaDpScheme> DataPointSchemes { get; } = Enum.GetValues<TuyaDpScheme>();

    public string SentFramesText => Device.Output?.Diagnostics.SentFrames.ToString("N0") ?? "—";

    public string DroppedFramesText => Device.Output?.Diagnostics.DroppedFrames.ToString("N0") ?? "—";

    public string LastSendText => Device.Output?.Diagnostics is { LastSentAt: { } at } diagnostics
        ? $"{diagnostics.LastSendDuration.TotalMilliseconds:0} мс, в {at:HH:mm:ss}"
        : "Ещё не было";

    public string LastErrorText => Device.Output?.Diagnostics is { LastError: { } error, LastErrorAt: { } at }
        ? $"{at:HH:mm:ss} — {error}"
        : "Нет";

    /// <summary>
    /// Обновляет всё, что меняется без участия пользователя; поля ввода не трогает, чтобы не сбить набор.
    /// </summary>
    public void Refresh()
    {
        foreach (var property in LiveProperties)
            OnPropertyChanged(property);
    }

    [RelayCommand]
    private void SaveNetworkKey()
    {
        _manager.SetNetworkKey(Device, LocalKey, DataPoints);
        _settingsChanged();

        Refresh();
    }
}
