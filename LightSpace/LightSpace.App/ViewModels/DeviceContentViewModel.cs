using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core.Config;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Эффект одного устройства вне его страницы — в профиле или в сцене: галерея эффектов приложения и встроенные эффекты
/// самого устройства. Выбор в галерее включает эффект приложения, выбор встроенного эффекта — встроенный; все правки
/// сразу попадают в содержимое.
/// </summary>
public sealed class DeviceContentViewModel : ObservableObject
{
    private readonly Action _changed;

    private ContentConfig? _content;

    /// <summary>
    /// findDevice находит известное устройство: встроенные эффекты есть только у подключённого. changed вызывается
    /// после каждой правки.
    /// </summary>
    public DeviceContentViewModel(Func<string, ManagedDevice?> findDevice, Action changed)
    {
        _changed = changed;

        Editor = new EffectEditorViewModel(() => Play(ContentKind.Effect));
        Builtin = new BuiltinEditorViewModel(findDevice, () => Play(ContentKind.Builtin));
    }

    /// <summary>
    /// Галерея эффектов приложения.
    /// </summary>
    public EffectEditorViewModel Editor { get; }

    /// <summary>
    /// Встроенные эффекты устройства, их скорость и цвет.
    /// </summary>
    public BuiltinEditorViewModel Builtin { get; }

    /// <summary>
    /// Играет встроенный эффект устройства — видны его настройки.
    /// </summary>
    public bool IsBuiltinPlaying => _content?.Kind == ContentKind.Builtin;

    /// <summary>
    /// Открывает для правки содержимое устройства deviceId; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content, string? deviceId)
    {
        _content = content;

        Editor.Edit(content);
        Builtin.Edit(content, deviceId);

        ShowPlaying();
    }

    /// <summary>
    /// В галерее или во встроенных эффектах что-то выбрали или поменяли: устройство играет то, что правили.
    /// </summary>
    private void Play(ContentKind kind)
    {
        if (_content == null)
            return;

        _content.Kind = kind;

        ShowPlaying();
        _changed();
    }

    /// <summary>
    /// Выделена плитка только того, что играет.
    /// </summary>
    private void ShowPlaying()
    {
        Editor.IsPlaying = _content?.Kind == ContentKind.Effect;
        Builtin.IsPlaying = _content?.Kind == ContentKind.Builtin;

        OnPropertyChanged(nameof(IsBuiltinPlaying));
    }
}
