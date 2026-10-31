namespace LightSpace.Core.Config;

/// <summary>
/// Что играет в содержимом.
/// </summary>
public enum ContentKind
{
    /// <summary>Эффект из галереи: каждое устройство показывает его целиком в своём пространстве.</summary>
    Effect,

    /// <summary>Сцена со слоями: эффекты слоёв ложатся на устройства по их местам на сцене.</summary>
    Scene,

    /// <summary>Экран: каждый светодиод повторяет цвет картинки монитора под своим местом на сцене.</summary>
    Screen,

    /// <summary>Музыка: свет следует за звуком компьютера или микрофона.</summary>
    Music,

    /// <summary>Игра: полоски здоровья и маны или брони и события матча Dota 2 и CS2; другие игры — картинка монитора.</summary>
    Game
}

/// <summary>
/// Содержимое, которое играет на устройствах: эффект из галереи и сохранённые настройки эффектов.
/// </summary>
public sealed class ContentConfig
{
    /// <summary>
    /// Что играет: эффект из галереи, сцена со слоями, экран, музыка или игра.
    /// </summary>
    public ContentKind Kind { get; set; } = ContentKind.Effect;

    /// <summary>
    /// Идентификатор эффекта, который сейчас играет.
    /// </summary>
    public string EffectId { get; set; } = "solid";

    /// <summary>
    /// Настройки эффектов по их идентификаторам; эффект, которого здесь нет, играет с настройками по умолчанию.
    /// Настройки остаются, когда выбирают другой эффект, и возвращаются вместе с ним.
    /// </summary>
    public Dictionary<string, EffectValuesConfig> Effects { get; set; } = new();

    /// <summary>
    /// Слои сцены сверху вниз: верхний слой ложится поверх нижних. Играют, только когда содержимое — сцена;
    /// остаются, когда выбирают эффект, и возвращаются вместе со сценой.
    /// </summary>
    public List<SceneLayerConfig> Layers { get; set; } = new();

    /// <summary>
    /// Настройки экрана. Играют, только когда содержимое — экран; остаются, когда выбирают другой тип.
    /// </summary>
    public ScreenConfig Screen { get; set; } = new();

    /// <summary>
    /// Настройки музыки. Играют, только когда содержимое — музыка; остаются, когда выбирают другой тип.
    /// </summary>
    public MusicConfig Music { get; set; } = new();

    /// <summary>
    /// Настройки игры. Играют, только когда содержимое — игра; остаются, когда выбирают другой тип.
    /// </summary>
    public GameConfig Game { get; set; } = new();

    /// <summary>
    /// Независимая копия содержимого: правки копии не меняют оригинал.
    /// </summary>
    public ContentConfig Clone()
    {
        return new ContentConfig
        {
            Kind = Kind,
            EffectId = EffectId,
            Effects = Effects.Where(e => e.Value != null).ToDictionary(e => e.Key, e => new EffectValuesConfig
            {
                Numbers = new Dictionary<string, double>(e.Value.Numbers),
                Colors = new Dictionary<string, string>(e.Value.Colors)
            }),
            Layers = Layers.Select(l => l.Clone()).ToList(),
            Screen = Screen.Clone(),
            Music = Music.Clone(),
            Game = Game.Clone()
        };
    }

    /// <summary>
    /// Приводит содержимое к допустимому: пустые разделы в файле дали бы null вместо эффекта и его настроек.
    /// </summary>
    public void Normalize()
    {
        EffectId ??= "solid";
        Effects ??= new Dictionary<string, EffectValuesConfig>();

        // Тип, которого нет в этой сборке, мог попасть в файл из другой версии или из правки руками
        if (!Enum.IsDefined(Kind))
            Kind = ContentKind.Effect;

        Layers = (Layers ?? new List<SceneLayerConfig>()).Where(l => l != null).ToList();

        var ids = new HashSet<string>();

        foreach (var layer in Layers)
        {
            layer.Normalize();

            // Слой без идентификатора или с повтором путал бы часы эффектов слоёв
            if (string.IsNullOrWhiteSpace(layer.Id) || !ids.Add(layer.Id))
            {
                layer.Id = GroupConfig.NewId();
                ids.Add(layer.Id);
            }
        }

        // В файлах до экрана и музыки этих разделов нет — тогда они получают настройки по умолчанию
        Screen ??= new ScreenConfig();
        Screen.Normalize();

        Music ??= new MusicConfig();
        Music.Normalize();

        // В файлах до игр этого раздела нет — тогда он получает настройки по умолчанию
        Game ??= new GameConfig();
        Game.Normalize();
    }
}

/// <summary>
/// Значения настроек одного эффекта, которые выставил пользователь; чего здесь нет — берётся по умолчанию.
/// </summary>
public sealed class EffectValuesConfig
{
    /// <summary>
    /// Числа, переключатели (0 или 1) и выбор из вариантов по ключам настроек.
    /// </summary>
    public Dictionary<string, double> Numbers { get; set; } = new();

    /// <summary>
    /// Цвета в формате #RRGGBB по ключам настроек.
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = new();
}
