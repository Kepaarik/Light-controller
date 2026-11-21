using CommunityToolkit.Mvvm.ComponentModel;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Устройство профиля в составе сцены: входит оно в сцену или играет своё.
/// </summary>
public sealed class SceneMemberViewModel : ObservableObject
{
    private readonly Action<SceneMemberViewModel, bool> _toggled;
    private bool _isIncluded;

    /// <summary>
    /// toggled вызывается, когда устройство включают в сцену или убирают из неё.
    /// </summary>
    public SceneMemberViewModel(string deviceId, string name, bool isIncluded, Action<SceneMemberViewModel, bool> toggled)
    {
        DeviceId = deviceId;
        Name = name;
        _isIncluded = isIncluded;
        _toggled = toggled;
    }

    public string DeviceId { get; }

    public string Name { get; }

    /// <summary>
    /// Устройство играет сцену вместо своей подсветки.
    /// </summary>
    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!SetProperty(ref _isIncluded, value))
                return;

            _toggled(this, value);
        }
    }
}
