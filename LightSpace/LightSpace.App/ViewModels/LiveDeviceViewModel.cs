using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.App.Controls;
using LightSpace.Core;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Найденное устройство в левом меню и в шапке его страницы: название, значок, состояние связи и живые цвета,
/// которые сейчас уходят на него.
/// </summary>
public sealed partial class LiveDeviceViewModel : ObservableObject
{
    private readonly ManagedDevice _device;
    private readonly ColorBitmap _bitmap;

    // Состояние связи, которое сейчас показано: меняется само, без пересборки устройств
    private DeviceStatus _status;

    /// <summary>
    /// Устройство само играет встроенный эффект: его цвета приложению неизвестны.
    /// </summary>
    [ObservableProperty]
    private bool _isBuiltinPlaying;

    public LiveDeviceViewModel(ManagedDevice device)
    {
        var capabilities = device.Capabilities;

        _device = device;
        _status = device.Status;

        Id = device.Id;
        Name = device.Name;
        IsConnected = device.Device != null;
        IsKeyboard = capabilities?.IsKeyboard == true;
        IsStrip = capabilities is { IsAddressable: true, IsKeyboard: false };

        KindText = capabilities switch
        {
            null => "Не подключено",
            { IsKeyboard: true } => "Клавиатура",
            { IsAddressable: true } => $"Лента, {capabilities.LedCount} светодиодов",
            _ => "Один цвет на всё устройство"
        };

        // У ленты — по точке на светодиод; у клавиатуры и устройства одного цвета — одна точка
        _bitmap = new ColorBitmap(IsStrip ? capabilities!.LedCount : 1, 1);
        _bitmap.Write(Array.Empty<RgbColor>());
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// Устройство подключено; неподключённому настраивать нечего — команды до него не дойдут.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Подсветка устройства включена; выключенное не светится, и живых цветов у него нет.
    /// </summary>
    public bool IsOn => _device.Settings.IsOn;

    /// <summary>
    /// Что это за устройство, или что оно не подключено.
    /// </summary>
    public string KindText { get; }

    public bool IsKeyboard { get; }

    /// <summary>
    /// Адресная лента: её можно повесить за монитор.
    /// </summary>
    public bool IsStrip { get; }

    /// <summary>
    /// Значок устройства в левом меню: выбранный пользователем или по типу устройства.
    /// </summary>
    public string Icon => _device.Settings.Icon ?? (IsKeyboard ? DeviceIcons.Keyboard : DeviceIcons.Strip);

    /// <summary>
    /// Состояние связи с устройством.
    /// </summary>
    public DeviceStatus Status => _status;

    public string StatusTitle => _status switch
    {
        DeviceStatus.Connected => "Подключено",
        DeviceStatus.NeedsSetup => "Нужна настройка",
        DeviceStatus.Failing => "Нет связи",
        _ => "Не найдено"
    };

    /// <summary>
    /// Полоса живых цветов устройства.
    /// </summary>
    public Brush Preview => _bitmap.Brush;

    /// <summary>
    /// Показывает цвета, ушедшие на устройство, с их яркостью; null — устройство сейчас не светится.
    /// </summary>
    public void Show(IReadOnlyList<RgbColor>? colors, int brightness)
    {
        // У клавиатуры одна точка — средний цвет клавиш
        if (IsKeyboard && colors is { Count: > 0 })
            colors = new[] { Average(colors) };

        _bitmap.Write(colors ?? Array.Empty<RgbColor>(), brightness);
    }

    /// <summary>
    /// Показывает новое состояние связи, если оно сменилось.
    /// </summary>
    public void RefreshStatus()
    {
        if (_device.Status == _status)
            return;

        _status = _device.Status;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusTitle));
    }

    private static RgbColor Average(IReadOnlyList<RgbColor> colors)
    {
        int r = 0, g = 0, b = 0;

        foreach (var color in colors)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        return new RgbColor((byte)(r / colors.Count), (byte)(g / colors.Count), (byte)(b / colors.Count));
    }
}
