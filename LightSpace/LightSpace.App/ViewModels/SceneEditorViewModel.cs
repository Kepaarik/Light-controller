using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Controls;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Редактор содержимого-сцены: холст сцены с живым кадром, устройствами на своих местах и границами слоёв,
/// список слоёв сверху вниз и настройки выбранного слоя с его эффектом. Все правки сразу попадают в содержимое.
/// </summary>
public sealed partial class SceneEditorViewModel : ObservableObject
{
    // Сетка точек, которой холст показывает кадр всей сцены
    private const int PreviewColumns = 100;
    private const int PreviewRows = 60;

    private static readonly IReadOnlyList<EffectPixel?> PreviewPoints = PreviewGrid();

    private readonly AppConfig _config;
    private readonly DeviceManager _manager;
    private readonly Action _changed;
    private readonly ColorBitmap _preview = new(PreviewColumns, PreviewRows);
    private readonly SceneClock _clock = new();

    private ContentConfig? _content;
    private SceneLayerViewModel? _selectedLayer;

    /// <summary>
    /// changed вызывается после каждой правки слоёв: их состава, порядка, границ, настроек и эффектов.
    /// config — откуда холст берёт яркость группы каждого устройства.
    /// </summary>
    public SceneEditorViewModel(AppConfig config, DeviceManager manager, Action changed)
    {
        _config = config;
        _manager = manager;
        _changed = changed;

        Effect = new EffectEditorViewModel(OnEffectChanged);

        _manager.DevicesChanged += (_, _) => RefreshDevices();
        RefreshDevices();
    }

    /// <summary>
    /// Галерея и настройки эффекта выбранного слоя.
    /// </summary>
    public EffectEditorViewModel Effect { get; }

    /// <summary>
    /// Слои сверху вниз: верхний ложится поверх остальных.
    /// </summary>
    public ObservableCollection<SceneLayerViewModel> Layers { get; } = new();

    /// <summary>
    /// Устройства на своих местах на сцене с живыми цветами сцены.
    /// </summary>
    public ObservableCollection<SceneDeviceViewModel> Devices { get; } = new();

    /// <summary>
    /// Кадр всей сцены для подложки холста.
    /// </summary>
    public Brush Preview => _preview.Brush;

    /// <summary>
    /// Режимы смешивания для сегментов.
    /// </summary>
    public IReadOnlyList<LayerBlendOption> Blends => LayerBlendOption.All;

    /// <summary>
    /// Слой, чьи границы тянут на холсте и чьи настройки открыты ниже.
    /// </summary>
    public SceneLayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (_selectedLayer == value)
                return;

            if (_selectedLayer != null)
                _selectedLayer.IsSelected = false;

            _selectedLayer = value;

            if (value != null)
                value.IsSelected = true;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLayer));

            Effect.Edit(value?.Settings.Effect);
        }
    }

    public bool HasSelectedLayer => _selectedLayer != null;

    /// <summary>
    /// Подсказка к полям границ слоя: размер всей сцены в тех же единицах.
    /// </summary>
    public string SceneSizeText => $"Вся сцена — {SceneArea.Width:0} × {SceneArea.Height:0}. Число применяется, когда поле теряет фокус.";

    /// <summary>
    /// Открывает содержимое для правки; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content)
    {
        _content = content;

        Layers.Clear();

        foreach (var layer in content?.Layers ?? new List<SceneLayerConfig>())
            Layers.Add(new SceneLayerViewModel(layer, _changed));

        UpdateOrder();

        // Сразу открыт верхний слой; эффект открывается и тогда, когда слоёв нет, чтобы редактор не держал чужое содержимое
        _selectedLayer = Layers.FirstOrDefault();

        if (_selectedLayer != null)
            _selectedLayer.IsSelected = true;

        OnPropertyChanged(nameof(SelectedLayer));
        OnPropertyChanged(nameof(HasSelectedLayer));
        Effect.Edit(_selectedLayer?.Settings.Effect);
    }

    /// <summary>
    /// Пересобирает устройства холста: их состав и места на сцене могли смениться.
    /// </summary>
    public void RefreshDevices()
    {
        Devices.Clear();

        foreach (var device in _manager.Devices)
            Devices.Add(new SceneDeviceViewModel(device));
    }

    /// <summary>
    /// Продвигает сцену на seconds секунд: рисует новый кадр на холсте, на устройствах и на плитках галереи.
    /// </summary>
    public void Advance(double seconds)
    {
        Effect.Advance(seconds);

        if (_content == null)
            return;

        var layers = SceneLayer.ListOf(_content);

        _clock.Advance(layers, seconds);
        _preview.Write(SceneRenderer.Render(layers, _clock, PreviewPoints));

        // Устройство на холсте светится с яркостью своей группы, как и на самом деле
        foreach (var device in Devices)
            device.Show(SceneRenderer.Render(layers, _clock, device.ScenePoints), _config.GroupOf(device.Id)?.Brightness ?? 100);
    }

    /// <summary>
    /// Верхний видимый слой, в границы которого попадает точка сцены.
    /// </summary>
    public SceneLayerViewModel? LayerAt(double x, double y)
    {
        return Layers.FirstOrDefault(l => l.IsVisible && l.Settings.Bounds.Contains(x, y));
    }

    /// <summary>
    /// Слой, который потянут мышью из точки сцены: выбранный, если точка в его границах, — даже когда он под другими, —
    /// иначе верхний видимый слой в этой точке.
    /// </summary>
    public SceneLayerViewModel? DraggedLayerAt(double x, double y)
    {
        if (_selectedLayer != null && _selectedLayer.Settings.Bounds.Contains(x, y))
            return _selectedLayer;

        return LayerAt(x, y);
    }

    /// <summary>
    /// Границы слоя дотянули мышью — они уходят движку.
    /// </summary>
    public void CommitBounds()
    {
        _changed();
    }

    /// <summary>
    /// Новый слой над всеми: заливка в середине сцены.
    /// </summary>
    [RelayCommand]
    private void AddLayer()
    {
        if (_content == null)
            return;

        var settings = SceneLayerConfig.Covering(new ContentConfig());

        settings.SetBounds(new SceneRect(SceneArea.Width / 4, SceneArea.Height / 4, SceneArea.Width / 2, SceneArea.Height / 2));
        _content.Layers.Insert(0, settings);

        var layer = new SceneLayerViewModel(settings, _changed);

        Layers.Insert(0, layer);
        UpdateOrder();

        SelectedLayer = layer;
        _changed();
    }

    /// <summary>
    /// Удаляет слой; выбранным становится соседний.
    /// </summary>
    [RelayCommand]
    private void RemoveLayer(SceneLayerViewModel? layer)
    {
        if (_content == null || layer == null)
            return;

        var index = Layers.IndexOf(layer);

        _content.Layers.Remove(layer.Settings);
        Layers.Remove(layer);
        UpdateOrder();

        if (layer == _selectedLayer)
            SelectedLayer = Layers.Count == 0 ? null : Layers[Math.Min(index, Layers.Count - 1)];

        _changed();
    }

    /// <summary>
    /// Перетаскивание в списке слоёв: слой встаёт на новое место, порядок наложения меняется сразу.
    /// </summary>
    [RelayCommand]
    private void MoveLayer(ReorderMove? move)
    {
        if (_content == null || move == null || move.From >= Layers.Count || move.To >= Layers.Count)
            return;

        var settings = _content.Layers[move.From];

        _content.Layers.RemoveAt(move.From);
        _content.Layers.Insert(move.To, settings);

        Layers.Move(move.From, move.To);
        UpdateOrder();
        _changed();
    }

    [RelayCommand]
    private void SelectLayer(SceneLayerViewModel? layer)
    {
        SelectedLayer = layer;
    }

    /// <summary>
    /// Растягивает выбранный слой на всю сцену.
    /// </summary>
    [RelayCommand]
    private void FillScene()
    {
        if (_selectedLayer == null)
            return;

        _selectedLayer.SetBounds(SceneRect.Whole);
        _changed();
    }

    /// <summary>
    /// У выбранного слоя сменили эффект или его настройки.
    /// </summary>
    private void OnEffectChanged()
    {
        _selectedLayer?.OnEffectChanged();
        _changed();
    }

    /// <summary>
    /// Верхний слой списка лежит на холсте поверх остальных.
    /// </summary>
    private void UpdateOrder()
    {
        for (var i = 0; i < Layers.Count; i++)
            Layers[i].ZIndex = Layers.Count - i;
    }

    /// <summary>
    /// Точки сетки холста на сцене, по строкам слева направо.
    /// </summary>
    private static IReadOnlyList<EffectPixel?> PreviewGrid()
    {
        var points = new EffectPixel?[PreviewColumns * PreviewRows];

        for (var row = 0; row < PreviewRows; row++)
        {
            for (var column = 0; column < PreviewColumns; column++)
            {
                var index = row * PreviewColumns + column;

                points[index] = new EffectPixel((column + 0.5) * SceneArea.Width / PreviewColumns,
                    (row + 0.5) * SceneArea.Height / PreviewRows, index);
            }
        }

        return points;
    }
}
