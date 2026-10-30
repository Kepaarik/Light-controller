using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Services;
using LedHub.Services.Games;

namespace LedHub.ViewModels;

/// <summary>
/// Игра в списке «Мои игры»: подсвечивать ли её и что включать при запуске — игровую подсветку или пресет.
/// </summary>
public sealed partial class GameProfileViewModel : ObservableObject
{
    private const string GameLighting = "";

    private readonly GameProfile _profile;
    private readonly ObservableCollection<SceneConfig> _scenes;
    private readonly string _lightingTitle;
    private readonly Action _save;
    private readonly Action<GameProfileViewModel> _changed;

    /// <summary>
    /// Варианты «что включать»: игровая подсветка и все пресеты.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<GameChoice<string>> _options = [];

    /// <summary>
    /// Игра знает, куда присылать события; у прочих игр подключать нечего.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    private bool _isConnected;

    /// <summary>
    /// changed — вызывается, когда пользователь включил или выключил игру или выбрал другой пресет.
    /// </summary>
    public GameProfileViewModel(GameKind kind, string title, string description, string lightingTitle, string? note,
        GameProfile profile, ObservableCollection<SceneConfig> scenes, Action save, Action<GameProfileViewModel> changed)
    {
        Kind = kind;
        Title = title;
        Description = description;
        Note = note;
        _lightingTitle = lightingTitle;
        _profile = profile;
        _scenes = scenes;
        _save = save;
        _changed = changed;

        _scenes.CollectionChanged += (_, _) => RefreshOptions();
        RefreshOptions();
    }

    public GameKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>
    /// Что ещё нужно сделать в самой игре; null — ничего.
    /// </summary>
    public string? Note { get; }

    public bool ShowNote => IsEnabled && Note != null;

    /// <summary>
    /// Игре нужен файл настроек, чтобы присылать события.
    /// </summary>
    public bool NeedsConnection => Kind != GameKind.Other;

    public string? ConnectionText => !NeedsConnection || !IsEnabled
        ? null
        : IsConnected ? "Подключена" : "Не найдена в библиотеках Steam";

    public bool IsEnabled
    {
        get => _profile.IsEnabled;
        set
        {
            if (_profile.IsEnabled == value)
                return;

            _profile.IsEnabled = value;
            _save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowNote));
            OnPropertyChanged(nameof(ConnectionText));
            _changed(this);
        }
    }

    /// <summary>
    /// Выбранный вариант: имя пресета или пустая строка — игровая подсветка.
    /// </summary>
    public string? Preset
    {
        get => Scene?.Name ?? GameLighting;
        set
        {
            // Список вариантов пересобирается при изменении пресетов, и на это время выпадающий список сбрасывает выбор в null
            if (value == null || value == Preset)
                return;

            _profile.Preset = value;
            _save();
            OnPropertyChanged();
            _changed(this);
        }
    }

    /// <summary>
    /// Пресет, который включается при запуске игры; null — игровая подсветка или пресет удалён.
    /// </summary>
    public SceneConfig? Scene => _profile.Preset.Length == 0
        ? null
        : _scenes.FirstOrDefault(s => s.Name == _profile.Preset);

    private void RefreshOptions()
    {
        Options = new[] { new GameChoice<string>(GameLighting, _lightingTitle) }
            .Concat(_scenes.Select(s => new GameChoice<string>(s.Name, s.Name)))
            .ToList();

        OnPropertyChanged(nameof(Preset));
    }
}
