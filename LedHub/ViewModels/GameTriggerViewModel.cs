using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Services;
using LedHub.Services.Games;

namespace LedHub.ViewModels;

/// <summary>
/// Переключатель события игры: включённое показывается вспышкой или своим цветом, выключенное — нет.
/// </summary>
public sealed class GameTriggerViewModel : ObservableObject
{
    private readonly GamesConfig _config;
    private readonly Action _save;
    private readonly string _key;

    public GameTriggerViewModel(GameKind kind, GameTrigger trigger, GamesConfig config, Action save)
    {
        _config = config;
        _save = save;
        _key = GameTriggers.ConfigKey(kind, trigger.Key);
        Title = trigger.Title;
        Description = trigger.Description;
    }

    public string Title { get; }

    public string Description { get; }

    public bool IsEnabled
    {
        get => !_config.DisabledTriggers.Contains(_key);
        set
        {
            if (IsEnabled == value)
                return;

            // Подсветка читает список на каждом кадре в другом потоке — подменяем его целиком, а не меняем на месте
            var disabled = new HashSet<string>(_config.DisabledTriggers);

            if (value)
                disabled.Remove(_key);
            else
                disabled.Add(_key);

            _config.DisabledTriggers = disabled;
            _save();
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// Вариант для выпадающего списка настроек игры.
/// </summary>
public sealed record GameChoice<T>(T Value, string Title);
