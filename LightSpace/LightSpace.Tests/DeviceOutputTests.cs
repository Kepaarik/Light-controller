using LightSpace.Core;
using LightSpace.Engine.Output;

namespace LightSpace.Tests;

/// <summary>
/// Вывод на устройство: какие одинаковые кадры уходят на устройство, а какие пропускаются.
/// </summary>
public sealed class DeviceOutputTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(2);

    [Fact]
    public void SameFrameIsSkippedByDefault()
    {
        using var output = new DeviceOutput(new FakeLed("a"), isEnabled: true);

        var sent = SubmitSameFrameThreeTimes(output);

        Assert.Equal(1, sent);
    }

    [Fact]
    public void SameFrameIsSentToDeviceThatCannotSkipIt()
    {
        using var output = new DeviceOutput(new EveryFrameLed("a"), isEnabled: true);

        var sent = SubmitSameFrameThreeTimes(output);

        Assert.Equal(3, sent);
    }

    /// <summary>
    /// Кладёт один и тот же кадр три раза, каждый раз дожидаясь, пока ящик опустеет. Возвращает, сколько кадров ушло.
    /// </summary>
    private static long SubmitSameFrameThreeTimes(DeviceOutput output)
    {
        var frame = OutputFrame.Solid(new RgbColor(255, 0, 0), 100);

        for (var i = 0; i < 3; i++)
        {
            var before = output.Diagnostics.SentFrames;

            output.Submit(frame);

            // Пропущенный кадр счётчик не меняет — ждём либо отправки, либо интервала устройства с запасом
            var deadline = DateTime.UtcNow + (i == 0 ? WaitLimit : TimeSpan.FromMilliseconds(300));

            while (output.Diagnostics.SentFrames == before && DateTime.UtcNow < deadline)
                Thread.Sleep(5);
        }

        return output.Diagnostics.SentFrames;
    }

    /// <summary>
    /// Устройство, которому нужен каждый кадр, даже одинаковый.
    /// </summary>
    private sealed class EveryFrameLed : FakeLed
    {
        public EveryFrameLed(string id) : base(id)
        {
        }

        public override bool CanSkipSameFrames => false;
    }
}
