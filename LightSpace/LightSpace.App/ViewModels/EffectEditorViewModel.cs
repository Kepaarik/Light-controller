using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Редактор содержимого-эффекта: галерея плиток с живыми превью и настройки выбранного эффекта. Правит одно содержимое —
/// «По умолчанию» группы или содержимое сценария; все правки сразу попадают в него.
/// </summary>
public sealed partial class EffectEditorViewModel : ObservableObject
{
    private readonly Action _changed;

    private ContentConfig? _content;
    private EffectTileViewModel _selectedTile;

    /// <summary>
    /// changed вызывается после каждой правки: выбора эффекта и изменения его настроек.
    /// </summary>
    public EffectEditorViewModel(Action changed)
    {
        _changed = changed;

        // Настройки плиток и выбранный эффект берутся у содержимого, когда его открывают
        Tiles = EffectLibrary.All
            .Select(e => new EffectTileViewModel(e, EffectValues.Empty))
            .ToList();

        _selectedTile = Tiles[0];
    }

    /// <summary>
    /// Плитки галереи по разделам: цвета, движение, природа.
    /// </summary>
    public IReadOnlyList<EffectTileViewModel> Tiles { get; }

    /// <summary>
    /// Эффект, который играет в открытом содержимом.
    /// </summary>
    public EffectTileViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            // Галерея сбрасывает выбор в null, когда пересобирает плитки, — играющий эффект от этого не меняется
            if (value == null || _content == null || !SetProperty(ref _selectedTile, value))
                return;

            _content.EffectId = value.Definition.Id;
            BuildParameters();
            _changed();
        }
    }

    /// <summary>
    /// Настройки выбранного эффекта.
    /// </summary>
    public ObservableCollection<EffectParameterViewModel> Parameters { get; } = new();

    /// <summary>
    /// Открывает содержимое для правки; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content)
    {
        _content = content;

        if (content == null)
        {
            Parameters.Clear();
            return;
        }

        // Превью плиток рисуются с настройками эффектов этого содержимого
        foreach (var tile in Tiles)
        {
            tile.Values = EffectValues.From(content.Effects.GetValueOrDefault(tile.Definition.Id));
            tile.Advance(0);
        }

        _selectedTile = Tiles.FirstOrDefault(t => t.Definition.Id == content.EffectId) ?? Tiles[0];
        OnPropertyChanged(nameof(SelectedTile));
        BuildParameters();
    }

    /// <summary>
    /// Продвигает превью всех плиток на seconds секунд.
    /// </summary>
    public void Advance(double seconds)
    {
        foreach (var tile in Tiles)
            tile.Advance(seconds);
    }

    /// <summary>
    /// Возвращает все настройки выбранного эффекта к значениям по умолчанию.
    /// </summary>
    [RelayCommand]
    private void ResetParameters()
    {
        foreach (var parameter in Parameters)
            parameter.Reset();

        OnParameterChanged();
    }

    /// <summary>
    /// Пересобирает настройки под выбранный эффект; правки пишутся в его сохранённые настройки.
    /// </summary>
    private void BuildParameters()
    {
        Parameters.Clear();

        if (_content == null)
            return;

        var values = ValuesConfigOf(_content, _selectedTile.Definition);

        foreach (var parameter in _selectedTile.Definition.Parameters)
        {
            EffectParameterViewModel item = parameter.Kind switch
            {
                EffectParameterKind.Color => new ColorParameterViewModel(parameter, values, OnParameterChanged),
                EffectParameterKind.Choice => new ChoiceParameterViewModel(parameter, values, OnParameterChanged),
                EffectParameterKind.Toggle => new ToggleParameterViewModel(parameter, values, OnParameterChanged),
                _ => new NumberParameterViewModel(parameter, values, OnParameterChanged)
            };

            Parameters.Add(item);
        }
    }

    private void OnParameterChanged()
    {
        if (_content == null)
            return;

        // Превью плитки рисуется с теми же настройками, что и эффект на устройствах
        _selectedTile.Values = EffectValues.From(ValuesConfigOf(_content, _selectedTile.Definition));

        _changed();
    }

    /// <summary>
    /// Сохранённые настройки эффекта; заводятся пустыми, если эффект ещё не настраивали.
    /// </summary>
    private static EffectValuesConfig ValuesConfigOf(ContentConfig content, EffectDefinition effect)
    {
        var effects = content.Effects;

        if (!effects.TryGetValue(effect.Id, out var values))
        {
            values = new EffectValuesConfig();
            effects[effect.Id] = values;
        }

        return values;
    }
}
