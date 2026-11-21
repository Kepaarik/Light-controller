using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Engine;
using LightSpace.Engine.Effects;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Устройство в профиле: что профиль включает на нём и с какой яркостью.
/// </summary>
public sealed partial class ProfileDeviceViewModel : ObservableObject
{
    private readonly ManagedDevice? _device;
    private readonly Action _changed;
    private readonly Action<ProfileDeviceViewModel> _remove;
    private readonly Action<ProfileDeviceViewModel>? _edit;

    /// <summary>
    /// device — устройство из списка известных; null, если его с тех пор забыли.
    /// changed вызывается после каждой правки, remove — по просьбе убрать устройство из профиля,
    /// edit — по просьбе выбрать эффект устройства в профиле.
    /// </summary>
    public ProfileDeviceViewModel(DeviceLightingConfig lighting, ManagedDevice? device, Action changed, Action<ProfileDeviceViewModel> remove,
        Action<ProfileDeviceViewModel>? edit = null)
    {
        Lighting = lighting;
        _device = device;
        _changed = changed;
        _remove = remove;
        _edit = edit;

        Name = device?.Name ?? "Забытое устройство";
        Icon = device?.Settings.Icon ?? (device?.Capabilities?.IsKeyboard == true ? DeviceIcons.Keyboard : DeviceIcons.Strip);
        IsKnown = device != null;
    }

    /// <summary>
    /// Яркость и содержимое устройства в профиле.
    /// </summary>
    public DeviceLightingConfig Lighting { get; }

    public string Name { get; }

    public string Icon { get; }

    /// <summary>
    /// Устройство есть среди известных; забытому профиль ничего не включит.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Что профиль включает на устройстве, например «Эффект · Радуга» или «Музыка».
    /// </summary>
    public string ContentText => DescribeContent(Lighting.Content, Lighting.DeviceId, _device);

    /// <summary>
    /// Содержимое устройства в профиле поменяли: описание показывает новое.
    /// </summary>
    public void RefreshContent()
    {
        OnPropertyChanged(nameof(ContentText));
    }

    /// <summary>
    /// Яркость устройства в профиле, 0–100 %.
    /// </summary>
    public double Brightness
    {
        get => Lighting.Brightness;
        set
        {
            var brightness = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (brightness == Lighting.Brightness)
                return;

            Lighting.Brightness = brightness;
            OnPropertyChanged();
            _changed();
        }
    }

    [RelayCommand]
    private void Remove()
    {
        _remove(this);
    }

    [RelayCommand]
    private void Edit()
    {
        _edit?.Invoke(this);
    }

    /// <summary>
    /// Короткое описание содержимого для списка устройств профиля.
    /// </summary>
    public static string DescribeContent(ContentConfig content, string deviceId, ManagedDevice? device)
    {
        return content.Kind switch
        {
            ContentKind.Effect => $"Эффект · {EffectLibrary.FindOrSolid(content.EffectId).Title}",
            ContentKind.Music => "Музыка",
            ContentKind.Screen => "Экран",
            ContentKind.Builtin => DescribeBuiltin(content.Builtin, deviceId, device),
            _ => "—"
        };
    }

    // Название встроенного эффекта известно, только пока устройство подключено: набор эффектов сообщает контроллер
    private static string DescribeBuiltin(BuiltinConfig builtin, string deviceId, ManagedDevice? device)
    {
        var effectId = builtin.Effects.GetValueOrDefault(deviceId);

        var title = device?.Device is IBuiltinEffectsDevice builtinDevice
            ? builtinDevice.BuiltinEffects.FirstOrDefault(e => e.Id == effectId)?.Title
            : null;

        return title != null ? $"Встроенный · {title}" : "Встроенный эффект";
    }
}
