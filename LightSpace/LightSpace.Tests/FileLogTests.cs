using System.Globalization;
using System.IO;
using LightSpace.App.Services;

namespace LightSpace.Tests;

/// <summary>
/// Журнал приложения: у каждой строки дата и время, чтобы по журналу было видно, когда что случилось.
/// </summary>
public sealed class FileLogTests
{
    [Fact]
    public void EveryLineStartsWithDateAndTime()
    {
        var text = new StringWriter();
        var listener = new FileLog.TimedTraceListener(text);
        var before = DateTime.Now;

        listener.WriteLine("первая");
        listener.WriteLine("вторая");
        listener.Flush();

        var after = DateTime.Now;
        var lines = text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" первая", lines[0]);
        Assert.EndsWith(" вторая", lines[1]);

        foreach (var line in lines)
        {
            var time = DateTime.ParseExact(line[..23], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            Assert.InRange(time, before.AddMilliseconds(-1), after.AddMilliseconds(1));
        }
    }
}
