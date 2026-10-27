using LedHub.Core;
using static LedHub.Services.Effects.EffectMath;

namespace LedHub.Services.Effects;

/// <summary>
/// Все эффекты приложения. Каждый считает цвет точки по её месту и времени, поэтому один и тот же эффект
/// играет и вдоль одной ленты, и по всей сцене — тогда волны, огонь и круги идут по реальным местам лент.
/// </summary>
public static class EffectLibrary
{
    private static readonly RgbColor[] SunriseColors =
    {
        Black, RgbColor.Parse("#3A0500"), RgbColor.Parse("#FF3D00"), RgbColor.Parse("#FFA040"), RgbColor.Parse("#FFE6C4")
    };

    public static IReadOnlyList<EffectDefinition> All { get; } = new[]
    {
        // Цвета
        new EffectDefinition("rainbow", "Радуга", EffectCategory.Colors,
            "Радуга бежит по лентам; на сцене — через все устройства по их местам",
            new[] { Speed(), Scale(), Direction(), Saturation() },
            (f, p) => RgbColor.FromHsv(f.Time * 60 + f.Along(p) * 360 / f.Number("scale"), f.Number("saturation") / 100, 1)),

        new EffectDefinition("color-cycle", "Смена цветов", EffectCategory.Colors,
            "Все ленты плавно и одновременно проходят по кругу цветов",
            new[] { Speed(), Saturation() },
            (f, _) => RgbColor.FromHsv(f.Time * 45, f.Number("saturation") / 100, 1)),

        new EffectDefinition("gradient", "Градиент", EffectCategory.Colors,
            "Плавный переход между тремя вашими цветами, медленно плывёт",
            new[]
            {
                Speed(0.5), Scale(), Direction(),
                ColorOf("color1", "Цвет 1", "#FF3B8B"), ColorOf("color2", "Цвет 2", "#7C6CFF"), ColorOf("color3", "Цвет 3", "#3BE8FF")
            },
            (f, p) => LoopPalette(f.Along(p) / f.Number("scale") - f.Time * 0.25,
                f.Color("color1"), f.Color("color2"), f.Color("color3"))),

        new EffectDefinition("breathing", "Дыхание", EffectCategory.Colors,
            "Цвет медленно разгорается и гаснет",
            new[] { Speed(), ColorOf("color", "Цвет", "#5B7BFF"), EffectParameter.Number("depth", "Глубина", 10, 100, 95, "{0:0} %") },
            (f, _) =>
            {
                var breath = (1 - Math.Cos(f.Time * Math.PI / 2)) / 2;
                return Dim(f.Color("color"), 1 - f.Number("depth") / 100 * (1 - breath));
            }),

        new EffectDefinition("wipe", "Заливка", EffectCategory.Colors,
            "Цвета по очереди заливают ленты от края до края",
            new[]
            {
                Speed(0.5), Direction(),
                ColorOf("color1", "Цвет 1", "#FF3B5C"), ColorOf("color2", "Цвет 2", "#3B7BFF"), ColorOf("color3", "Цвет 3", "#FFD23B")
            },
            (f, p) =>
            {
                var cycle = f.Time * 0.3;
                var index = (int)Math.Floor(cycle);
                var current = Pick(index, f.Color("color1"), f.Color("color2"), f.Color("color3"));
                var next = Pick(index + 1, f.Color("color1"), f.Color("color2"), f.Color("color3"));
                var front = Frac(cycle) * 1.2 - 0.1;

                return Mix(current, next, Smooth((front - f.Along(p)) / 0.08 + 0.5));
            }),

        // Движение
        new EffectDefinition("wave", "Волна", EffectCategory.Motion,
            "Мягкие волны света катятся в выбранную сторону",
            new[] { Speed(), Scale(), Direction(), ColorOf("color", "Цвет", "#3BE8FF"), ColorOf("background", "Фон", "#06142E") },
            (f, p) =>
            {
                var wave = (1 + Math.Cos(2 * Math.PI * (f.Along(p) / f.Number("scale") - f.Time / 4))) / 2;
                return Mix(f.Color("background"), f.Color("color"), wave * wave);
            }),

        new EffectDefinition("comet", "Комета", EffectCategory.Motion,
            "Яркая голова с затухающим хвостом",
            new[]
            {
                Speed(), Direction(), ColorOf("color", "Цвет", "#FFF3B0"),
                EffectParameter.Number("tail", "Длина хвоста", 0.05, 0.8, 0.3, "{0:0.00}"),
                EffectParameter.Toggle("rainbow", "Радужный хвост", false)
            },
            (f, p) =>
            {
                var behind = Frac(f.Time / 3 - f.Along(p));
                var length = f.Number("tail");
                if (behind >= length)
                    return Black;

                var color = f.Flag("rainbow") ? RgbColor.FromHsv(f.Time * 40 + behind * 300, 1, 1) : f.Color("color");
                return Dim(color, Math.Pow(1 - behind / length, 2));
            }),

        new EffectDefinition("scanner", "Сканер", EffectCategory.Motion,
            "Огонёк бегает туда и обратно, как у машины из «Рыцаря дорог»",
            new[]
            {
                Speed(), Direction(), ColorOf("color", "Цвет", "#FF2A2A"),
                EffectParameter.Number("width", "Ширина", 0.02, 0.4, 0.1, "{0:0.00}")
            },
            (f, p) =>
            {
                var distance = (f.Along(p) - PingPong(f.Time * 0.25)) / f.Number("width");
                return Dim(f.Color("color"), Math.Exp(-distance * distance * 2));
            }),

        new EffectDefinition("chase", "Театр огней", EffectCategory.Motion,
            "Бегущие огни, как на старой вывеске",
            new[]
            {
                Speed(), Direction(), ColorOf("color", "Цвет", "#FFC46B"), ColorOf("background", "Фон", "#000000"),
                EffectParameter.Number("count", "Частота огней", 3, 40, 12, "{0:0}")
            },
            (f, p) => Frac(f.Along(p) * f.Number("count") - f.Time * 2) < 0.35 ? f.Color("color") : f.Color("background")),

        new EffectDefinition("meteor", "Метеоритный дождь", EffectCategory.Motion,
            "Светящиеся капли с хвостами падают сверху вниз",
            new[]
            {
                Speed(), ColorOf("color", "Цвет", "#9EE7FF"),
                EffectParameter.Number("count", "Количество", 2, 30, 10, "{0:0}"),
                EffectParameter.Number("tail", "Длина хвоста", 0.05, 0.6, 0.25, "{0:0.00}"),
                EffectParameter.Toggle("rainbow", "Разноцветные", false)
            },
            (f, p) =>
            {
                var tail = f.Number("tail");
                double behind;
                int column;

                if (f.IsLinear)
                {
                    // На одной ленте метеоры просто бегут вдоль неё друг за другом
                    var spacing = Math.Max(1, f.Number("count") / 4);
                    var position = f.Time * 0.5 - p.X * spacing;
                    column = (int)Math.Floor(position);
                    behind = Frac(position);
                }
                else
                {
                    column = (int)Math.Floor(p.X / f.Width * f.Number("count"));
                    var speed = 0.35 + Hash(column, 8) * 0.5;
                    var head = Frac(f.Time * speed * 0.5 + Hash(column, 9)) * (1 + tail);
                    behind = head - p.Y / f.Height;
                }

                if (behind < 0 || behind >= tail)
                    return Black;

                var color = f.Flag("rainbow") ? RgbColor.FromHsv(Hash(column, 10) * 360, 0.8, 1) : f.Color("color");
                return Dim(color, Math.Pow(1 - behind / tail, 2));
            }),

        new EffectDefinition("ripple", "Капли", EffectCategory.Motion,
            "Круги расходятся из случайных мест, как от капель на воде",
            new[]
            {
                Speed(), ColorOf("color", "Цвет", "#4FC3FF"),
                EffectParameter.Number("count", "Капель сразу", 1, 8, 3, "{0:0}"),
                EffectParameter.Toggle("rainbow", "Разноцветные", true)
            },
            (f, p) =>
            {
                const double period = 2.2;
                var drops = (int)f.Number("count");
                var result = Black;

                for (var j = 0; j < drops; j++)
                {
                    var local = f.Time / period + (double)j / drops;
                    var cycle = (int)Math.Floor(local);
                    var age = local - cycle;

                    var centerX = Hash(cycle, j, 21) * f.Width;
                    var centerY = Hash(cycle, j, 22) * f.Height;
                    var distance = f.IsLinear
                        ? Math.Abs(p.X - centerX)
                        : Math.Sqrt((p.X - centerX) * (p.X - centerX) + (p.Y - centerY) * (p.Y - centerY));

                    var ring = Math.Exp(-Math.Pow((distance - age * 0.7) / 0.05, 2)) * (1 - age);
                    var color = f.Flag("rainbow") ? RgbColor.FromHsv(Hash(cycle, j, 23) * 360, 0.8, 1) : f.Color("color");

                    result = Max(result, Dim(color, ring));
                }

                return result;
            }),

        new EffectDefinition("police", "Полиция", EffectCategory.Motion,
            "Две половины перемигиваются красным и синим",
            new[] { Speed(), Direction(), ColorOf("color1", "Цвет 1", "#FF1A1A"), ColorOf("color2", "Цвет 2", "#1A3BFF") },
            (f, p) =>
            {
                var isFirstHalf = f.Along(p) < 0.5;
                var isSwapped = (int)Math.Floor(f.Time * 2) % 2 == 1;
                var flash = Frac(f.Time * 2);
                var isLit = flash < 0.18 || flash is > 0.28 and < 0.46;
                var color = isFirstHalf ^ isSwapped ? f.Color("color1") : f.Color("color2");

                return isLit ? color : Dim(color, 0.06);
            }),

        new EffectDefinition("strobe", "Стробоскоп", EffectCategory.Motion,
            "Резкие вспышки — для вечеринки",
            new[] { Speed(), ColorOf("color", "Цвет", "#FFFFFF"), EffectParameter.Number("duty", "Длина вспышки", 10, 90, 50, "{0:0} %") },
            (f, _) => Frac(f.Time * 4) < f.Number("duty") / 100 ? f.Color("color") : Black),

        new EffectDefinition("heartbeat", "Сердцебиение", EffectCategory.Motion,
            "Двойной удар, на сцене расходится от центра",
            new[] { Speed(), ColorOf("color", "Цвет", "#FF2A4A") },
            (f, p) =>
            {
                var t = Frac(f.Time * 0.8 - f.Radial(p) * 0.35);
                var beat = Math.Exp(-Math.Pow((t - 0.08) / 0.035, 2)) + 0.65 * Math.Exp(-Math.Pow((t - 0.26) / 0.045, 2));

                return Dim(f.Color("color"), 0.06 + 0.94 * Math.Min(1, beat));
            }),

        new EffectDefinition("plasma", "Плазма", EffectCategory.Motion,
            "Переливы цвета, как на экране старой демо-сцены",
            new[] { Speed(), Scale(), Saturation() },
            (f, p) =>
            {
                var scale = f.Number("scale");
                var x = p.X * 10 / scale;
                var y = p.Y * 10 / scale;
                var t = f.Time;
                var value = Math.Sin(x + t) + Math.Sin((y + t) / 2) + Math.Sin((x + y + t) / 2) + Math.Sin(Math.Sqrt(x * x + y * y + 1) + t);

                return RgbColor.FromHsv(value * 45 + t * 20, f.Number("saturation") / 100, 1);
            }),

        // Природа
        new EffectDefinition("twinkle", "Звёзды", EffectCategory.Nature,
            "Светодиоды по одному загораются и гаснут на тёмном фоне",
            new[]
            {
                Speed(), ColorOf("color", "Звёзды", "#FFF6D8"), ColorOf("background", "Небо", "#0B0E26"),
                EffectParameter.Number("density", "Сколько звёзд", 2, 40, 12, "{0:0} %"),
                EffectParameter.Toggle("rainbow", "Цветные звёзды", false)
            },
            (f, p) =>
            {
                var period = 1.2 + Hash(p.Seed, 1) * 1.6;
                var local = f.Time / period + Hash(p.Seed, 2);
                var cycle = (int)Math.Floor(local);

                if (Hash(p.Seed, cycle, 3) >= f.Number("density") / 100)
                    return f.Color("background");

                var glow = Math.Pow(Math.Sin(Math.PI * (local - cycle)), 2);
                var color = f.Flag("rainbow") ? RgbColor.FromHsv(Hash(p.Seed, cycle, 4) * 360, 0.75, 1) : f.Color("color");

                return Mix(f.Color("background"), color, glow);
            }),

        new EffectDefinition("confetti", "Конфетти", EffectCategory.Nature,
            "Короткие вспышки случайных цветов",
            new[] { Speed(), EffectParameter.Number("density", "Сколько вспышек", 5, 60, 25, "{0:0} %"), Saturation() },
            (f, p) =>
            {
                var local = f.Time * 0.9 + Hash(p.Seed, 5);
                var cycle = (int)Math.Floor(local);

                if (Hash(p.Seed, cycle, 6) >= f.Number("density") / 100)
                    return Black;

                var color = RgbColor.FromHsv(Hash(p.Seed, cycle, 7) * 360, f.Number("saturation") / 100, 1);
                return Dim(color, Math.Pow(1 - (local - cycle), 3));
            }),

        new EffectDefinition("fire", "Огонь", EffectCategory.Nature,
            "Языки пламени; на сцене огонь поднимается снизу вверх",
            new[]
            {
                Speed(), Scale(),
                EffectParameter.Number("height", "Высота пламени", 0.3, 1.5, 0.8, "{0:0.0}"),
                EffectParameter.Choice("palette", "Пламя", 0,
                    new EffectChoice(0, "Огонь"), new EffectChoice(1, "Газовое"), new EffectChoice(2, "Колдовское"))
            },
            (f, p) =>
            {
                var scale = f.Number("scale");
                double heat;

                if (f.IsLinear)
                {
                    heat = 0.2 + 0.8 * Fbm(p.X * 10 / scale, f.Time * 1.8, 0.5);
                }
                else
                {
                    // Низ сцены — у Y, равного высоте: там пламя самое горячее
                    var noise = Fbm(p.X * 7 / scale, p.Y * 5 / scale + f.Time * 1.6, f.Time * 0.5);
                    heat = noise * 1.5 - (1 - p.Y / f.Height) / f.Number("height") + 0.05;
                }

                return HeatColor(heat, (int)f.Number("palette"));
            }),

        new EffectDefinition("candle", "Свеча", EffectCategory.Nature,
            "Тёплый неровный свет, как от живого огня",
            new[] { Speed(), ColorOf("color", "Цвет", "#FFB04A"), EffectParameter.Number("flicker", "Мерцание", 0, 100, 45, "{0:0} %") },
            (f, p) =>
            {
                var noise = 0.7 * Noise(f.Time * 3, 0.5, 1.5) + 0.3 * Noise(f.Time * 6, p.X * 6, 2.5);
                return Dim(f.Color("color"), 1 - f.Number("flicker") / 100 * (1 - noise) * 1.4);
            }),

        new EffectDefinition("aurora", "Северное сияние", EffectCategory.Nature,
            "Медленные переливающиеся полосы, зелёные и фиолетовые",
            new[]
            {
                Speed(0.6), Scale(),
                ColorOf("color1", "Цвет 1", "#2BFFA0"), ColorOf("color2", "Цвет 2", "#2B8BFF"), ColorOf("color3", "Цвет 3", "#B03BFF")
            },
            (f, p) =>
            {
                var scale = f.Number("scale");
                var shape = Fbm(p.X * 2 / scale + f.Time * 0.08, p.Y * 1.2 / scale, f.Time * 0.15);
                var tint = Noise(p.X * 1.3 / scale - f.Time * 0.05, p.Y / scale + 3, f.Time * 0.1 + 7);
                var glow = Smooth((shape - 0.32) / 0.4);

                // На сцене сияние ярче вверху, как настоящее
                if (!f.IsLinear)
                    glow *= 0.35 + 0.65 * (1 - p.Y / f.Height);

                return Dim(Palette(tint, f.Color("color1"), f.Color("color2"), f.Color("color3")), glow);
            }),

        new EffectDefinition("ocean", "Океан", EffectCategory.Nature,
            "Глубокая синева с бликами и пеной",
            new[]
            {
                Speed(0.7), Scale(),
                ColorOf("color1", "Глубина", "#001A4D"), ColorOf("color2", "Вода", "#0077FF"), ColorOf("color3", "Пена", "#9FF3FF")
            },
            (f, p) =>
            {
                var scale = f.Number("scale");
                var noise = Fbm(p.X * 3 / scale + f.Time * 0.15, p.Y * 3 / scale - f.Time * 0.1, f.Time * 0.2);

                return Palette(Smooth((noise - 0.2) / 0.6), f.Color("color1"), f.Color("color2"), f.Color("color3"));
            }),

        new EffectDefinition("lava", "Лава-лампа", EffectCategory.Nature,
            "Мягкие цветные пятна медленно плавают и сливаются",
            new[]
            {
                Speed(0.5), Scale(),
                EffectParameter.Number("count", "Пятен", 2, 8, 4, "{0:0}"),
                ColorOf("color1", "Цвет 1", "#FF3B5C"), ColorOf("color2", "Цвет 2", "#FFB03B"), ColorOf("background", "Фон", "#22072E")
            },
            (f, p) =>
            {
                var count = (int)f.Number("count");
                var scale = f.Number("scale");
                double field = 0, red = 0, green = 0, blue = 0;

                for (var j = 0; j < count; j++)
                {
                    var a = f.Time * 0.25 * (0.6 + Hash(j, 31)) + Hash(j, 32) * 6.283;
                    var b = f.Time * 0.2 * (0.6 + Hash(j, 33)) + Hash(j, 34) * 6.283;
                    var dx = p.X - f.Width * (0.5 + 0.42 * Math.Sin(a));
                    var dy = f.IsLinear ? 0 : p.Y - f.Height * (0.5 + 0.42 * Math.Cos(b));
                    var radius = 0.13 * scale * (0.7 + 0.6 * Hash(j, 35));
                    var weight = radius * radius / (dx * dx + dy * dy + 1e-4);
                    var color = Mix(f.Color("color1"), f.Color("color2"), Hash(j, 36));

                    field += weight;
                    red += color.R * weight;
                    green += color.G * weight;
                    blue += color.B * weight;
                }

                var blob = new RgbColor(ToByte(red / field), ToByte(green / field), ToByte(blue / field));
                return Mix(f.Color("background"), blob, Smooth((field - 0.5) / 0.9));
            }),

        new EffectDefinition("lightning", "Гроза", EffectCategory.Nature,
            "Тёмное небо и внезапные вспышки молний в разных местах",
            new[]
            {
                Speed(), ColorOf("color", "Молния", "#CFE3FF"), ColorOf("background", "Небо", "#0B1030"),
                EffectParameter.Number("frequency", "Частота", 1, 10, 4, "{0:0}")
            },
            (f, p) =>
            {
                var frequency = f.Number("frequency");
                var slot = (int)Math.Floor(f.Time * frequency);
                var local = Frac(f.Time * frequency);
                var zone = (int)Math.Floor(p.X / f.Width * 3);
                double flash = 0;

                if (Hash(slot, zone, 41) < 0.3)
                    flash = local < 0.06 ? 1 : local < 0.12 ? 0.25 : local < 0.18 ? 0.85 : Math.Exp(-(local - 0.18) * 12);

                var sky = Dim(f.Color("background"), 0.6 + 0.4 * Noise(p.X * 3, p.Y * 3, f.Time * 0.5));
                return Mix(sky, f.Color("color"), flash);
            }),

        new EffectDefinition("sunrise", "Рассвет", EffectCategory.Nature,
            "Свет медленно разгорается от тёмно-красного до тёплого белого — мягкое пробуждение",
            new[] { EffectParameter.Number("duration", "Длительность", 1, 30, 10, "{0:0} мин") },
            (f, p) =>
            {
                var progress = Math.Clamp(f.Time / (f.Number("duration") * 60), 0, 1);
                var local = f.IsLinear ? progress : Math.Clamp(progress * 1.25 - (1 - p.Y / f.Height) * 0.25, 0, 1);

                return Palette(local, SunriseColors);
            }),

        // Музыка
        new EffectDefinition("music-pulse", "Пульс от центра", EffectCategory.Music,
            "Чем громче музыка, тем дальше от центра (монитора) расходится свет",
            new[] { Gain(), ColorOf("color", "Цвет", "#7C6CFF"), EffectParameter.Toggle("rainbow", "Радуга", true) },
            (f, p) =>
            {
                var distance = f.Radial(p);
                var lit = Smooth((f.Audio.Level * 1.15 - distance) / 0.12);
                var color = f.Flag("rainbow") ? RgbColor.FromHsv(distance * 220 + f.Time * 25, 1, 1) : f.Color("color");

                return Dim(color, 0.04 + 0.96 * lit);
            }) { UsesAudio = true },

        new EffectDefinition("music-rings", "Круги от ударов", EffectCategory.Music,
            "Каждый удар баса запускает круг, который расходится от центра по всей сцене",
            new[] { Gain(), Speed(), ColorOf("color", "Цвет", "#3BE8FF"), EffectParameter.Toggle("rainbow", "Разноцветные", true) },
            (f, p) =>
            {
                var distance = f.Distance(p);
                var result = Dim(f.Flag("rainbow") ? RgbColor.FromHsv(f.Audio.Hue, 1, 1) : f.Color("color"), f.Audio.Level * 0.15);

                foreach (var beat in f.Audio.Beats)
                {
                    var age = f.Time - beat.Time;
                    if (age is < 0 or > 2.5)
                        continue;

                    var ring = Math.Exp(-Math.Pow((distance - age * 0.55) / 0.06, 2)) * (1 - age / 2.5);
                    var color = f.Flag("rainbow") ? RgbColor.FromHsv(beat.Hue, 1, 1) : f.Color("color");

                    result = Max(result, Dim(color, ring));
                }

                return result;
            }) { UsesAudio = true },

        new EffectDefinition("music-eq", "Эквалайзер", EffectCategory.Music,
            "Столбики спектра по всей сцене: басы слева, высокие справа; на ленте — яркость полос",
            new[] { Gain(), Saturation() },
            (f, p) =>
            {
                var x = p.X / f.Width;
                var level = f.Audio.BandAt(x);
                var saturation = f.Number("saturation") / 100;

                if (f.IsLinear)
                    return RgbColor.FromHsv(x * 270, saturation, level);

                var height = 1 - p.Y / f.Height;
                return height <= level
                    ? RgbColor.FromHsv(x * 270 - height * 30, saturation, 1)
                    : RgbColor.FromHsv(x * 270, saturation, 0.03);
            }) { UsesAudio = true },

        new EffectDefinition("music-spectrum", "Спектр", EffectCategory.Music,
            "Полосы спектра вдоль выбранного направления, каждая светится своей громкостью",
            new[] { Gain(), Direction(), Saturation() },
            (f, p) =>
            {
                var position = f.Along(p);
                return RgbColor.FromHsv(position * 270 + f.Time * 10, f.Number("saturation") / 100, f.Audio.BandAt(position));
            }) { UsesAudio = true },

        new EffectDefinition("music-beat", "Радуга по битам", EffectCategory.Music,
            "Цвет перескакивает по радуге на каждом ударе, яркость — по громкости",
            new[] { Gain(), Scale(), Direction() },
            (f, p) => RgbColor.FromHsv(f.Audio.Hue + f.Along(p) * 90 / f.Number("scale"), 1, 0.1 + 0.9 * f.Audio.Level)) { UsesAudio = true }
    };

    public static EffectDefinition? Find(string id)
    {
        return All.FirstOrDefault(e => e.Id == id);
    }

    public static string CategoryTitle(EffectCategory category) => category switch
    {
        EffectCategory.Colors => "Цвета",
        EffectCategory.Motion => "Движение",
        EffectCategory.Nature => "Природа",
        _ => "Музыка с компьютера"
    };

    private static EffectParameter Speed(double value = 1) =>
        EffectParameter.Number("speed", "Скорость", 0.1, 4, value, "{0:0.0}×");

    private static EffectParameter Scale(double value = 1) =>
        EffectParameter.Number("scale", "Размер узора", 0.2, 4, value, "{0:0.0}×");

    private static EffectParameter Saturation() =>
        EffectParameter.Number("saturation", "Насыщенность", 0, 100, 100, "{0:0} %");

    private static EffectParameter Gain() =>
        EffectParameter.Number("gain", "Чувствительность", 0.5, 3, 1, "{0:0.0}×");

    private static EffectParameter ColorOf(string key, string title, string hex) =>
        EffectParameter.Color(key, title, hex);

    private static EffectParameter Direction() =>
        EffectParameter.Choice("direction", "Направление", 0,
            new EffectChoice(0, "→ Вправо"),
            new EffectChoice(1, "← Влево"),
            new EffectChoice(2, "↓ Вниз"),
            new EffectChoice(3, "↑ Вверх"),
            new EffectChoice(4, "От центра"),
            new EffectChoice(5, "К центру"));

    /// <summary>
    /// Цвет по «температуре» пламени от 0 (темно) до 1 (почти белое).
    /// </summary>
    private static RgbColor HeatColor(double heat, int palette)
    {
        heat = Math.Clamp(heat, 0, 1);

        var hue = palette switch
        {
            1 => 235 - 45 * heat,
            2 => 115 + 45 * heat,
            _ => 50 * heat
        };

        var saturation = 1 - Smooth((heat - 0.75) * 3) * 0.7;
        return RgbColor.FromHsv(hue, saturation, Smooth(heat * 2.2));
    }

    private static RgbColor Pick(int index, RgbColor first, RgbColor second, RgbColor third)
    {
        var position = ((index % 3) + 3) % 3;

        return position switch
        {
            0 => first,
            1 => second,
            _ => third
        };
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}
