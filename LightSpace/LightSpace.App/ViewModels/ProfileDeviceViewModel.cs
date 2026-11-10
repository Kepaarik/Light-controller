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
    private readonly Action _changed;
    private readonly Action<ProfileDeviceViewModel> _remove;

    /// <summary>
    /// device — устройство из списка известных; null, если его с тех пор забыли.
    /// changed вызывается после каждой правки, remove — по просьбе убрать устройство из профиля.
    /// </summary>
    public ProfileDeviceViewModel(GroupConfig lighting, ManagedDevice? device, Action changed, Action<ProfileDeviceViewModel> remove)
    {
        Lighting = lighting;
        _changed = changed;
        _remove = remove;

        var deviceId = lighting.DeviceIds[0];

        Name = device?.Name ?? "Забытое устройство";
        Icon = device?.Settings.Icon ?? (device?.Capabilities?.IsKeyboard == true ? DeviceIcons.Keyboard : DeviceIcons.Strip);
        IsKnown = device != null;
        ContentText = DescribeContent(lighting.DefaultContent, deviceId, device);
    }

    /// <summary>
    /// Яркость и содержимое устройства в профиле.
    /// </summary>
    public GroupConfig Lighting { get; }

    public string Name { get; }

    public string Icon { get; }

    /// <summary>
    /// Устройство есть среди известных; забытому профиль ничего не включит.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Что профиль включает на устройстве, например «Эффект · Радуга» или «Музыка».
    /// </summary>
    public string ContentText { get; }

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
            ContentKind.Scene => "Сцена",
            ContentKind.Game => "Игра",
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
