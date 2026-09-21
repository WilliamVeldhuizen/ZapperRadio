# Design notes

Why the larger features work the way they do. The README says what they do; this is the
reasoning behind them, the classes involved and the limits that were accepted, kept for
whoever changes them later. Ordered by the version they shipped in.

## Play history (1.10.0)

A rolling 12-hour history of everything every favorite played, as a third tab next to Stations
and Favorite tracks, with the title tidied up (`TrackTitle`) and a heart per entry.

It is deliberately a list of titles, not of audio. `PlayHistory` stores what the streams
announced, which costs nothing beyond what the relay already reads, so the rewind and recording
features on the roadmap still have to bring their own buffer; the two are not the same feature
at different resolutions.

## Lock screen, media keys and global hotkeys (1.11.0)

`Shell/SystemMediaControls` owns one `SystemMediaTransportControls` for the app and feeds it from
what the window shows: the station, the song and the station logo in the volume flyout and on the
lock screen, with play, pause, next and previous, which is also what the media keys of a keyboard
or a headset press. Next and previous walk the favorites (`Core/Playback/FavoriteRing`), so the
media keys zap. The per-player overlay stays off in `StationStream`, because twenty players would
each claim the card.

A WinUI 3 desktop app has no view to ask, so the controls are obtained for the window handle
through `ISystemMediaTransportControlsInterop`; .NET does not marshal an IInspectable interface,
so its one method is called through the vtable.

`Shell/GlobalHotkeys` claims `Ctrl+Alt+P`, `Ctrl+Alt+M`, `Ctrl+Alt+Right` and `Ctrl+Alt+Left`
with `RegisterHotKey` and watches for WM_HOTKEY by chaining the window procedure. They are on
`Ctrl+Alt` rather than on the `Ctrl+Space` and `Ctrl+M` of the window, because claiming those
system wide would take them away from every other app. A combination another app already holds is
named in the settings instead of failing, and the whole set can be switched off there.

## Loudness normalization across stations (1.12.0)

`Core/Audio/Loudness` measures how loud audio is the EBU R128 / ITU-R BS.1770 way: the samples are
K-weighted (the high shelf of the head and the RLB high pass) and the mean square of overlapping
400 ms blocks is averaged, leaving the blocks below -70 LUFS out. It runs on the PCM
`SoundClassifier` already decodes for YAMNet, so it costs nothing but the arithmetic, and only on
the windows YAMNet calls music: what a station does to its music is what makes it louder than the
next one, while ads and presenters are mixed at a level of their own.

`Core/Audio/StationLoudness` turns those windows into one number per station. A histogram rather
than a list, because a favorite streams for hours, and gated as BS.1770 prescribes, so a quiet
intro does not drag the level down. After about a minute of music (12 windows) it gives a gain
towards -14 LUFS, the level streaming services normalize to, clamped to -12..+6 dB.

`StationStream` owns the estimate, so it survives a reconnect, and applies `volume * gain` to its
own player; `RadioEngine` keeps the loudness measured in an earlier run, which `AppSettings` stores
per station so the correction is there from the first second of the next run. The settings show
the measurement per favorite with a button to measure again (`StationStream.Remeasure` starts a
fresh histogram and keeps the old correction until it has a new estimate), and a switch for the
whole thing.

The one real limit is that `MediaPlayer.Volume` stops at 1, so a station that needs a boost cannot
get one with the volume slider at the top.

## The song clock (1.14.0)

The unmarked ad break detection described in the README used to time a song from the moment its
title came in: `StationStream.WatchSongEndAsync` took `DateTime.UtcNow` there and set
`_songEndTimer` to `title arrival + length + SongOverrun`, 30 seconds. That assumes the title and
the song start together, and plenty of stations do not work that way. Their playout system
announces the next item while the current one is still fading, or over the jingle in between, so
the title runs 10 to 20 seconds ahead of the audio. The clock then started too early and the 30
seconds of slack quietly shrank to 10: the song was marked overdue while it was still playing, and
the first presenter or station ident after it was enough for `UnmarkedAdBreak` to call a break
that was not one.

`Core/Audio/SongClock` anchors the clock to the audio instead. A title starts the clock but not
the timer; the first two windows of music in a row after it (about 10 seconds, because one window
alone is noise) say where the song really began, and the clock is set back to the start of that
run. A title that arrives late, while the song is already playing, cannot move the song forward,
so the clock never starts later than the title. Streams that are not classified at all (HLS, which
skips the relay) and streams where 45 seconds pass without either music or speech - a quiet or
instrumental intro - fall back to timing from the title as they did before, nudged by the 5-second
watchdog rather than by a window.

With the clock on the song itself, `Overrun` means what it says again and came down from 30 to 20
seconds, which makes the real breaks show up sooner too. Two smaller holes went with it: a station
that replaces the song title with its own name or the name of the program between the song and the
ads no longer resets the clock (only a title shaped like "Artist - Title" is a new song), and only
such a title counts as evidence of a song in `Channel.StateOf`, so a station name during a break is
no longer mistaken for music.

## Links out to Spotify and YouTube (1.15.0)

A favorite track could only be copied as text. A find button now sits next to the song that is
playing, in both windows, and next to every favorite track and every entry in the play history,
with Spotify and YouTube behind it. Apple Music was deliberately left out: two services cover where
the songs actually go, and each extra one is another row in a menu that has to stay a glance.

`Core/Models/TrackLinks` builds the links and is the whole of the logic: the words of the title
with the separator dropped, escaped into `spotify:search:`, `open.spotify.com/search/` or
`youtube.com/results`. A stream title is not a track id, so the link searches rather than opens the
song, which has the pleasant side effect of covering the stations that send "Title - Artist"
instead of "Artist - Title", because a search does not care about the order. `MainWindow` asks
`Launcher.QueryUriSupportAsync` whether `spotify:` has a handler before using it, so Spotify opens
in its app when it is installed and in its web player when it is not, without Windows offering to
go looking for one in the Store.

The iTunes Search API that `TrackDurations` already calls could pin the exact track rather than a
search, and would give an Apple Music link for free, but it costs a lookup per click and misses
often enough that a search is the better answer for a radio title.

## Start with Windows (1.17.0)

The app is meant to be on all day, and a radio you have to remember to open is a radio you forget,
so a switch at the top of the settings lets Windows start it when the user signs in.

The app is unpackaged (`WindowsPackageType` is `None`), so there is no `StartupTask` manifest
extension to declare. `Shell/StartupRegistration` writes a `ZapperRadio` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` naming the running executable. The app writes
and removes it itself rather than leaving it to the installer, because it is a preference and not
part of being installed, and it is per user, so it needs no admin rights and does not turn itself
on for everyone on the machine.

The registry is the source of truth: the switch reads the key back at start rather than trusting a
setting, because the Startup Apps page of Windows can switch the entry off behind the app's back,
and a switch that says "on" while Windows disagrees is worse than no switch.
`DisableForThisInstall` removes the entry on uninstall, but only when it starts the copy that is
being removed, so it never breaks the entry of another install, such as an old MSI next to it.

## More languages (1.18.0)

The station list is worldwide and most of its listeners do not read English, so the player speaks
the language Windows is set to. There are ten: English, Chinese (simplified), Spanish, Portuguese
(Brazil), French, German, Japanese, Ukrainian, Italian and Dutch: mostly the widest-spoken
languages of the Windows desktop, with Dutch and Ukrainian chosen by request. Hindi and Bengali are
spoken by more people but come after these, and Arabic and Urdu would first need the layout checked
right to left. Adding a language is a folder of texts and a line in a list.

`Strings\<language>\Resources.resw` holds every text. The XAML gets its texts through an `x:Uid` on
each element, so `MainWindow.xaml` has no English left in it, and the texts built in code
(`StatusTexts`, `SongTexts`, `LoudnessTexts`, the jump list, the error messages) go through
`Localizer.Get` and `Localizer.Format`, one place that wraps the `ResourceLoader`. `Localizer.Use`
runs first thing in the `MainViewModel` constructor, before the window is built from its XAML,
because that is when the `x:Uid`s are looked up. It takes the language from `AppSettings.Language`
and otherwise from the first language in the Windows list that the app has, with `nl-BE` and
`pt-PT` falling under Dutch and Portuguese and traditional Chinese falling through to the next
language rather than being shown in the wrong script.

The language is chosen once and stays for the run. A window built from XAML and its bindings is not
redrawn in another language, so the language box in the settings only says "restart to switch".
That is also what made the two hard parts of the plan cheap: `MainViewModel.AllCountries` is an
instance property set from the language at the start, and it is stored in the settings as `null`
and never as text, so a country choice survives switching languages without needing a sentinel of
its own. The dates and numbers, which were pinned to `en-US`, follow the language: `Localizer.Use`
sets the current culture, and `FavoriteTrack`, `PlayedTrack` and the catalog line only ask for the
short date and the clock. `Localizer.Use` also sets the culture of the threads that come later,
which keeps the number formats of one window consistent.

Two things were left out. The installer stayed English: at the time it was a WiX MSI, which is
built per culture, and the release workflow publishes one file per architecture under a name the
README links to, so translating it meant either an installer per language or the language
transforms an MSI can carry, and neither was worth it for the handful of dialogs before the app is
running. And the country and genre names come from the station list in English and stay that way;
translating them is on the roadmap.

## Updates with Velopack (1.19.0)

`release.yml` runs `vpk pack` per architecture through `build-installer.ps1`, on the channels
`win-x64` and `win-arm64`, after a `vpk download github` so the delta package can be made against
the previous release. The release gets the two Setup files under fixed names for the README links,
this version's packages, and the releases feeds, and `Updates/AppUpdater` reads those feeds from
the GitHub releases, so an update is nothing more than a new release. The WiX MSI is gone.

The app is installed per user in `%LOCALAPPDATA%\ZapperRadioApp`, apart from the settings in
`%LOCALAPPDATA%\ZapperRadio`, because uninstalling deletes the whole install folder and the
favorites should survive it.

The price is the people who still have the MSI (up to 1.18): it installs for all users and has no
update check, so they only learn about the change from the README and the website, and have to
uninstall it by hand once. There is no migration step, and the app cannot see the old install from
the new one.
