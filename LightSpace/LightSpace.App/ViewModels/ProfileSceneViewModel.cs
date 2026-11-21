using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Scene;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Сцена профиля: какие устройства в неё входят, где они стоят на холсте, какой эффект играют, куда он идёт
/// и с какой яркостью. Все правки сразу попадают в настройки профиля; на устройствах сцена включится, когда профиль применят.
/// </summary>
public sealed partial class ProfileSceneViewModel : ObservableObject
{
    // Эффект новой сцены: на нём сразу видно, как цвет переходит с устройства на устройство
    private const string NewSceneEffectId = "rainbow";

    // Момент эффекта, с которого начинается превью: не самое начало, чтобы картинка сразу была «живой»
    private const double PreviewStartTime = 3.3;

    private readonly ProfileConfig _profile;
    private readonly Func<string, ManagedDevice?> _findDevice;
    private readonly Action _changed;

    private double _time = PreviewStartTime;

    /// <summary>
    /// findDevice находит известное устройство по идентификатору; changed вызывается после каждой правки сцены.
    /// </summary>
    public ProfileSceneViewModel(ProfileConfig profile, Func<string, ManagedDevice?> findDevice, Action changed)
    {
        _profile = profile;
        _findDevice = findDevice;
        _changed = changed;

        Editor = new EffectEditorViewModel(OnEditorChanged);

        Reload();
    }

    /// <summary>
    /// Галерея эффектов сцены.
    /// </summary>
    public EffectEditorViewModel Editor { get; }

    /// <summary>
    /// Как эффект ложится на устройства сцены.
    /// </summary>
    public IReadOnlyList<SceneChoice<SceneSpread>> Spreads { get; } = new[]
    {
        new SceneChoice<SceneSpread>(SceneSpread.Space, "По пространству"),
        new SceneChoice<SceneSpread>(SceneSpread.Synced, "Синхронно")
    };

    /// <summary>
    /// Куда идёт эффект сцены.
    /// </summary>
    public IReadOnlyList<SceneChoice<SceneDirection>> Directions { get; } = new[]
    {
        new SceneChoice<SceneDirection>(SceneDirection.LeftToRight, "Слева направо"),
        new SceneChoice<SceneDirection>(SceneDirection.RightToLeft, "Справа налево"),
        new SceneChoice<SceneDirection>(SceneDirection.TopToBottom, "Сверху вниз"),
        new SceneChoice<SceneDirection>(SceneDirection.BottomToTop, "Снизу вверх"),
        new SceneChoice<SceneDirection>(SceneDirection.FromCenter, "Из центра")
    };

    /// <summary>
    /// У профиля есть сцена.
    /// </summary>
    public bool HasScene => _profile.Scene != null;

    /// <summary>
    /// Устройства профиля: какие из них входят в сцену.
    /// </summary>
    public ObservableCollection<SceneMemberViewModel> Members { get; } = new();

    /// <summary>
    /// Устройства сцены на холсте.
    /// </summary>
    public ObservableCollection<SceneDeviceViewModel> Devices { get; } = new();

    public bool HasDevices => Devices.Count > 0;

    /// <summary>
    /// Устройство, выбранное на холсте: его можно повернуть и поменять ему размер; null — ничего не выбрано.
    /// </summary>
    [ObservableProperty]
    private SceneDeviceViewModel? _selectedDevice;

    public bool HasSelectedDevice => SelectedDevice != null;

    partial void OnSelectedDeviceChanged(SceneDeviceViewModel? oldValue, SceneDeviceViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.IsSelected = false;

        if (newValue != null)
            newValue.IsSelected = true;

        OnPropertyChanged(nameof(HasSelectedDevice));
    }

    /// <summary>
    /// Кадр превью пересчитан: холст рисует устройства заново.
    /// </summary>
    public event Action? Rendered;

    /// <summary>
    /// Как эффект ложится на устройства; null — сцены нет.
    /// </summary>
    public SceneChoice<SceneSpread>? SelectedSpread
    {
        get => _profile.Scene is { } scene ? Spreads.First(s => s.Value == scene.Spread) : null;
        set
        {
            if (value == null || _profile.Scene is not { } scene || scene.Spread == value.Value)
                return;

            scene.Spread = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSpaceSpread));
            OnSceneChanged();
        }
    }

    /// <summary>
    /// Эффект идёт по холсту: места устройств на холсте важны.
    /// </summary>
    public bool IsSpaceSpread => _profile.Scene?.Spread == SceneSpread.Space;

    /// <summary>
    /// Куда идёт эффект; null — сцены нет.
    /// </summary>
    public SceneChoice<SceneDirection>? SelectedDirection
    {
        get => _profile.Scene is { } scene ? Directions.First(d => d.Value == scene.Direction) : null;
        set
        {
            if (value == null || _profile.Scene is not { } scene || scene.Direction == value.Value)
                return;

            scene.Direction = value.Value;
            OnPropertyChanged();
            OnSceneChanged();
        }
    }

    /// <summary>
    /// Яркость всех устройств сцены, 0–100 %.
    /// </summary>
    public double Brightness
    {
        get => _profile.Scene?.Brightness ?? 100;
        set
        {
            if (_profile.Scene is not { } scene)
                return;

            var brightness = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (brightness == scene.Brightness)
                return;

            scene.Brightness = brightness;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Короткое описание сцены для подсказки в меню трея; null — сцены нет.
    /// </summary>
    public string? SummaryText => _profile.Scene is { } scene
        ? $"Сцена: {EffectLibrary.FindOrSolid(scene.Effect.EffectId).Title}, устройств {scene.Devices.Count}, {scene.Brightness} %"
        : null;

    /// <summary>
    /// Собирает сцену из подключённых и известных устройств профиля: они встают на холсте в ряд слева направо.
    /// </summary>
    [RelayCommand]
    private void Build()
    {
        if (_profile.Scene != null)
            return;

        var scene = new SceneConfig { Effect = new ContentConfig { Kind = ContentKind.Effect, EffectId = NewSceneEffectId } };
        var deviceIds = _profile.DeviceLighting.Select(l => l.DeviceId).Where(id => _findDevice(id) != null).ToList();

        for (var i = 0; i < deviceIds.Count; i++)
        {
            scene.Devices.Add(new SceneDeviceConfig
            {
                DeviceId = deviceIds[i],
                X = (i + 0.5) / deviceIds.Count,
                Y = 0.5,
                Size = Math.Clamp(0.9 / deviceIds.Count, SceneDeviceConfig.MinSize, SceneDeviceConfig.MaxSize)
            });
        }

        _profile.Scene = scene;

        Reload();
        _changed();
    }

    /// <summary>
    /// Убирает сцену из профиля: её устройства снова играют то, что профиль включает на каждом из них.
    /// </summary>
    [RelayCommand]
    private void Remove()
    {
        if (_profile.Scene == null)
            return;

        _profile.Scene = null;

        Reload();
        _changed();
    }

    /// <summary>
    /// Поворачивает выбранное устройство на четверть оборота.
    /// </summary>
    [RelayCommand]
    private void RotateSelected()
    {
        if (SelectedDevice == null)
            return;

        SelectedDevice.Rotate();
        OnPlacementChanged();
    }

    /// <summary>
    /// Место, размер или поворот устройства на холсте поменяли: превью и сохранённая сцена показывают новое.
    /// </summary>
    public void OnPlacementChanged()
    {
        Render();
        _changed();
    }

    /// <summary>
    /// Устройство убрали из профиля: из сцены оно тоже уходит.
    /// </summary>
    public void RemoveDevice(string deviceId)
    {
        if (_profile.Scene?.Devices.RemoveAll(d => d.DeviceId == deviceId) > 0)
            Reload();
        else
            ReloadMembers();
    }

    /// <summary>
    /// Продвигает превью на seconds секунд с учётом скорости эффекта и пересчитывает цвета устройств.
    /// </summary>
    public void Advance(double seconds)
    {
        if (_profile.Scene is not { } scene)
            return;

        var effect = EffectLibrary.FindOrSolid(scene.Effect.EffectId);

        _time += seconds * effect.Number(ValuesOf(scene, effect), "speed");
        Render();
    }

    /// <summary>
    /// Перечитывает сцену из настроек профиля, например после записи в профиль текущей подсветки.
    /// </summary>
    public void Reload()
    {
        var scene = _profile.Scene;

        SelectedDevice = null;
        Devices.Clear();

        foreach (var placement in scene?.Devices ?? new List<SceneDeviceConfig>())
            Devices.Add(NewDevice(placement));

        ReloadMembers();

        Editor.Edit(scene?.Effect);
        Editor.IsPlaying = scene != null;

        OnPropertyChanged(nameof(HasScene));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(SelectedSpread));
        OnPropertyChanged(nameof(IsSpaceSpread));
        OnPropertyChanged(nameof(SelectedDirection));
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(SummaryText));

        Render();
    }

    /// <summary>
    /// Состав сцены: известные устройства профиля и устройства, которые уже стоят в сцене.
    /// </summary>
    private void ReloadMembers()
    {
        Members.Clear();

        if (_profile.Scene is not { } scene)
            return;

        var deviceIds = _profile.DeviceLighting.Select(l => l.DeviceId)
            .Concat(scene.Devices.Select(d => d.DeviceId))
            .Distinct();

        foreach (var deviceId in deviceIds)
        {
            if (_findDevice(deviceId) is not { } device)
                continue;

            var isIncluded = scene.Devices.Any(d => d.DeviceId == deviceId);

            Members.Add(new SceneMemberViewModel(deviceId, device.Name, isIncluded, OnMemberToggled));
        }
    }

    private SceneDeviceViewModel NewDevice(SceneDeviceConfig placement)
    {
        var device = _findDevice(placement.DeviceId);

        return new SceneDeviceViewModel(placement, device?.Name ?? "Забытое устройство", device?.Geometry ?? DeviceGeometry.Single(placement.DeviceId),
            OnPlacementChanged);
    }

    /// <summary>
    /// Устройство включили в сцену или убрали из неё. Новое устройство встаёт в середину холста.
    /// </summary>
    private void OnMemberToggled(SceneMemberViewModel member, bool isIncluded)
    {
        if (_profile.Scene is not { } scene)
            return;

        if (isIncluded)
        {
            var placement = new SceneDeviceConfig { DeviceId = member.DeviceId };

            scene.Devices.Add(placement);
            Devices.Add(NewDevice(placement));
        }
        else
        {
            scene.Devices.RemoveAll(d => d.DeviceId == member.DeviceId);

            var device = Devices.FirstOrDefault(d => d.DeviceId == member.DeviceId);

            if (device != null)
            {
                if (SelectedDevice == device)
                    SelectedDevice = null;

                Devices.Remove(device);
            }
        }

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(SummaryText));
        OnSceneChanged();
    }

    /// <summary>
    /// В галерее выбрали эффект или поменяли его настройки.
    /// </summary>
    private void OnEditorChanged()
    {
        Editor.IsPlaying = true;

        OnPropertyChanged(nameof(SummaryText));
        OnSceneChanged();
    }

    private void OnSceneChanged()
    {
        Render();
        _changed();
    }

    /// <summary>
    /// Цвета каждого устройства в текущий момент превью — тем же рендером и в том же пространстве, что на устройствах.
    /// </summary>
    private void Render()
    {
        if (_profile.Scene is not { } scene)
            return;

        var effect = EffectLibrary.FindOrSolid(scene.Effect.EffectId);
        var values = ValuesOf(scene, effect);

        foreach (var device in Devices)
            device.Colors = EffectRenderer.Render(effect, values, SceneSpace.Of(device.Own, device.Placement, scene.Spread, scene.Direction), _time);

        Rendered?.Invoke();
    }

    private static EffectValues ValuesOf(SceneConfig scene, EffectDefinition effect)
    {
        return EffectValues.From(scene.Effect.Effects.GetValueOrDefault(effect.Id));
    }
}
