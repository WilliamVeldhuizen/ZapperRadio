using ZapperRadio.Core.Settings;

namespace ZapperRadio.Core.Tests;

public class AppSettingsTests
{
    private static AppSettings LoadFrom(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zapperradio-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            return new SettingsStore(path).Load();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ZapsToTheSongStartWithFiveMinutesByDefault()
    {
        var settings = LoadFrom("{}");

        Assert.True(settings.ZapToSongStart);
        Assert.Equal(5, settings.TimeShiftMinutes);
    }

    [Fact]
    public void AnOlderFileWithTheBufferOff_KeepsItOff_AndGetsALengthForWhenItIsSwitchedOn()
    {
        var settings = LoadFrom("""{ "TimeShiftMinutes": 0 }""");

        Assert.False(settings.ZapToSongStart);
        Assert.Equal(5, settings.TimeShiftMinutes);
    }

    [Fact]
    public void KeepsTheLengthThatWasChosen()
    {
        var settings = LoadFrom("""{ "ZapToSongStart": false, "TimeShiftMinutes": 10 }""");

        Assert.False(settings.ZapToSongStart);
        Assert.Equal(10, settings.TimeShiftMinutes);
    }

    [Fact]
    public void ALengthThatIsNotAChoiceFallsBackToFiveMinutes()
    {
        Assert.Equal(5, LoadFrom("""{ "TimeShiftMinutes": 7 }""").TimeShiftMinutes);
    }
}
