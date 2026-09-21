using ZapperRadio.Core.Audio;
using ZapperRadio.Core.Playback;

namespace ZapperRadio.Core.Tests;

public class SoundHistoryTests
{
    /// <summary>A history from letters as in the recorded streams: M = music, S = speech, anything else unknown.</summary>
    private static SoundHistory Heard(string windows)
    {
        var history = new SoundHistory();
        foreach (var window in windows)
        {
            history.Add(window switch { 'M' => Sound.Music, 'S' => Sound.Speech, _ => Sound.Unknown });
        }

        return history;
    }

    [Theory]
    [InlineData(0.00f, 0.95f, Sound.Music)]
    [InlineData(0.99f, 0.00f, Sound.Speech)]
    [InlineData(0.38f, 0.36f, Sound.Speech)]
    [InlineData(0.05f, 0.10f, Sound.Unknown)]
    public void LabelsAWindowByItsHighestScore(float speech, float music, Sound expected)
    {
        Assert.Equal(expected, SoundHistory.Label(speech, music));
    }

    [Theory]
    // Songs, including one with a short spoken bit.
    [InlineData("MMMMMMMMMM", Sound.Music)]
    [InlineData("MMSMMMMM", Sound.Music)]
    // Recorded ad breaks of Qmusic and JOE.
    [InlineData("MSSSSSmsMm", Sound.Speech)]
    [InlineData("MssMsMmSSSSS", Sound.Speech)]
    [InlineData("MMSSMM", Sound.Speech)]
    // Too little heard, or one word over the music.
    [InlineData("MM", Sound.Unknown)]
    [InlineData("MMMMMS", Sound.Unknown)]
    [InlineData("MM-M-M", Sound.Unknown)]
    public void WeighsTheRecentWindows(string windows, Sound expected)
    {
        Assert.Equal(expected, Heard(windows).Current);
    }

    [Fact]
    public void ClearForgetsWhatWasHeard()
    {
        var history = Heard("SSSSSS");

        history.Clear();

        Assert.Equal(Sound.Unknown, history.Current);
        Assert.Equal(0, history.ConsecutiveMusic);
    }

    [Fact]
    public void CountsMusicInARowBeyondTheWindowsKept()
    {
        Assert.Equal(14, Heard("SS" + new string('M', 14)).ConsecutiveMusic);
        Assert.Equal(0, Heard("MMMMS").ConsecutiveMusic);
    }

    [Fact]
    public void UnmarkedAdBreak_WithoutListeningTheOverdueSongDecides()
    {
        var adBreak = new UnmarkedAdBreak();

        Assert.False(adBreak.Update(isSongOverdue: false, isListening: false, new SoundHistory()));
        Assert.True(adBreak.Update(isSongOverdue: true, isListening: false, new SoundHistory()));
        Assert.True(adBreak.IsActive);
    }

    [Fact]
    public void UnmarkedAdBreak_MusicMeansTheSongGoesOn()
    {
        var adBreak = new UnmarkedAdBreak();

        adBreak.Update(isSongOverdue: true, isListening: true, Heard("MMMMMM"));

        Assert.False(adBreak.IsActive);
    }

    [Fact]
    public void UnmarkedAdBreak_StartsWithSpeechAndLastsThroughJingles()
    {
        var adBreak = new UnmarkedAdBreak();
        var sound = Heard("MMMMMM");
        adBreak.Update(isSongOverdue: true, isListening: true, sound);

        // The recorded unmarked break of 538, after its song ran over.
        foreach (var window in "mSSSSSMMMMMMmmsSmSSmmmmSsmsSSSSMsMMmMMMSSSMSSSMSmSSSS")
        {
            sound.Add(window switch { 'M' or 'm' => Sound.Music, 'S' or 's' => Sound.Speech, _ => Sound.Unknown });
            adBreak.Update(isSongOverdue: true, isListening: true, sound);
            if (window == 'S' && sound.Current == Sound.Speech)
            {
                Assert.True(adBreak.IsActive);
            }
        }

        Assert.True(adBreak.IsActive);
    }

    [Fact]
    public void UnmarkedAdBreak_EndsAfterAMinuteOfMusicOrANewSong()
    {
        var adBreak = new UnmarkedAdBreak();
        var sound = Heard("SSSSSS");
        adBreak.Update(isSongOverdue: true, isListening: true, sound);
        Assert.True(adBreak.IsActive);

        for (var i = 1; i < UnmarkedAdBreak.MusicWindowsToEnd; i++)
        {
            sound.Add(Sound.Music);
            adBreak.Update(isSongOverdue: true, isListening: true, sound);
        }

        Assert.True(adBreak.IsActive);
        sound.Add(Sound.Music);
        Assert.True(adBreak.Update(isSongOverdue: true, isListening: true, sound));
        Assert.False(adBreak.IsActive);

        adBreak.Update(isSongOverdue: true, isListening: true, Heard("SSSSSS"));
        Assert.True(adBreak.IsActive);
        Assert.True(adBreak.Update(isSongOverdue: false, isListening: true, Heard("SSSSSS")));
        Assert.False(adBreak.IsActive);
    }

    [Theory]
    [InlineData(true, true, true, Sound.Music, ChannelState.Ad)]
    [InlineData(false, false, true, Sound.Music, ChannelState.Unavailable)]
    [InlineData(false, true, true, Sound.Unknown, ChannelState.Song)]
    [InlineData(false, true, false, Sound.Music, ChannelState.Song)]
    // A title while someone talks: a presenter, the news or a program name.
    [InlineData(false, true, true, Sound.Speech, ChannelState.Speech)]
    [InlineData(false, true, false, Sound.Speech, ChannelState.Speech)]
    [InlineData(false, true, false, Sound.Unknown, ChannelState.Unknown)]
    public void ChannelState_WeighsTheSoundAgainstTheTitle(bool isInAdBreak, bool isLive, bool hasTitle, Sound sound, ChannelState expected)
    {
        Assert.Equal(expected, Channel.StateOf(isInAdBreak, isLive, hasTitle, sound));
    }

    [Theory]
    [InlineData("MMMMMM", 0)]
    // A break that began two windows ago, and one with a jingle between the talking.
    [InlineData("MMMMSS", 2)]
    [InlineData("MMMSuS", 3)]
    // Still talking after a jingle: the stretch runs up to now.
    [InlineData("MMMSSu", 3)]
    // A stray word in the song does not drag the start of a later break back to it.
    [InlineData("SMMMMS", 1)]
    // A jingle before the first word is not counted: it may as well be the end of the song.
    [InlineData("MMuuSS", 2)]
    public void SpeechStretch_SaysWhereTheTalkingBegan(string windows, int expected)
    {
        Assert.Equal(expected * SongClock.Window, Heard(windows).SpeechStretch);
    }

    [Fact]
    public void SpeechStretch_StartsWhereTheTalkingBeginsInItsFirstWindow()
    {
        var sound = Heard("MMMM");
        sound.Add(Sound.Speech, speechFrom: 0.6);
        sound.Add(Sound.Speech, speechFrom: 0.2);

        // The song still played for three of the five seconds of the first window of talking.
        Assert.Equal(1.4 * SongClock.Window, sound.SpeechStretch);
    }

    [Fact]
    public void SpeechStretch_IgnoresWhereTalkingBeginsInAWindowOfMusic()
    {
        var sound = Heard("MMMM");
        sound.Add(Sound.Music, speechFrom: 0.8);
        sound.Add(Sound.Speech);

        Assert.Equal(SongClock.Window, sound.SpeechStretch);
    }

    [Theory]
    // Talking throughout, or from the start after one stray frame of music.
    [InlineData("SSSSSSSSSS", 0.0)]
    [InlineData("MSSSSSSSSS", 0.1)]
    // The end of a song, then a presenter.
    [InlineData("MMMMMMSSSS", 0.6)]
    // A word over the fade-out does not pull the start to it.
    [InlineData("MMSMMMSSSS", 0.6)]
    public void SpeechFrom_SplitsTheWindowWhereTheTalkingBegins(string frames, double expected)
    {
        var speech = frames.Select(f => f == 'S' ? 0.9f : 0.1f).ToList();
        var music = frames.Select(f => f == 'M' ? 0.9f : 0.1f).ToList();

        Assert.Equal(expected, SoundHistory.SpeechFrom(speech, music), precision: 6);
    }
}
