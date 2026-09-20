# Roadmap

Ideas for what to build next, ranked by payoff per effort. The list is deliberately biased
towards features that only this player can offer, because it keeps every favorite streaming
and already decodes and classifies their audio.

Nothing here is promised or scheduled; it is a working list.

## 1. Live rewind / instant replay (30-60 s ring buffer per favorite)

Every favorite is already decoded for the sound classifier, so keeping the last minute of PCM
per station in a ring buffer costs little extra. It makes it possible to jump back to the start
of the song you zapped into, or to replay what you just missed - across all 20 favorites at once,
which no other radio player can do. Hooks into `IcyProxy`'s audio callback and `StationStream`.
The largest effort on this list and the biggest differentiator.

## 2. Auto-record the current song

`IcyProxy` already knows exactly when a title changes, so track boundaries come for free. A
"record this song" button (and an optional "record everything I heart") writes the segment from
the ring buffer onwards to `%MUSIC%\ZapperRadio\Artist - Title.mp3`, tagged. Together with the
rewind buffer it can record a song that was already halfway through when you noticed it. Intended
for personal use of a broadcast, which the README should say plainly.

## 3. Loudness normalization across stations - built in 1.12.0

`Core/Audio/Loudness` measures how loud audio is the EBU R128 / ITU-R BS.1770 way: the samples are K-weighted
(the high shelf of the head and the RLB high pass) and the mean square of overlapping 400 ms blocks is averaged,
leaving the blocks below -70 LUFS out. It runs on the PCM `SoundClassifier` already decodes for YAMNet, so it
costs nothing but the arithmetic, and only on the windows YAMNet calls music: what a station does to its music
is what makes it louder than the next one, while ads and presenters are mixed at a level of their own.

`Core/Audio/StationLoudness` turns those windows into one number per station. A histogram rather than a list,
because a favorite streams for hours, and gated as BS.1770 prescribes, so a quiet intro does not drag the level
down. After about a minute of music (12 windows) it gives a gain towards -14 LUFS, the level streaming services
normalize to, clamped to -12..+6 dB.

`StationStream` owns the estimate, so it survives a reconnect, and applies `volume * gain` to its own player;
`RadioEngine` keeps the loudness measured in an earlier run, which `AppSettings` stores per
station so the correction is there from the first second of the next run. The settings show the measurement per
favorite with a button to measure again (`StationStream.Remeasure` starts a fresh histogram and keeps the old
correction until it has a new estimate), and a switch for the whole thing. The one real limit is that
`MediaPlayer.Volume` stops at 1, so a station that needs a boost cannot get one with the volume slider at the top.

## 4. Lock screen, media keys and global hotkeys - built in 1.11.0

`Shell/SystemMediaControls` owns one `SystemMediaTransportControls` for the app and feeds it from what
the window shows: the station, the song and the station logo in the volume flyout and on the lock
screen, with play, pause, next and previous, which is also what the media keys of a keyboard or a
headset press. Next and previous walk the favorites (`Core/Playback/FavoriteRing`), so the media keys
zap. The per-player overlay stays off in `StationStream`, because twenty players would each claim the
card. A WinUI 3 desktop app has no view to ask, so the controls are obtained for the window handle
through `ISystemMediaTransportControlsInterop`; .NET does not marshal an IInspectable interface, so
its one method is called through the vtable.

`Shell/GlobalHotkeys` claims `Ctrl+Alt+P`, `Ctrl+Alt+M`, `Ctrl+Alt+Right` and `Ctrl+Alt+Left` with
`RegisterHotKey` and watches for WM_HOTKEY by chaining the window procedure. They are on `Ctrl+Alt`
rather than on the `Ctrl+Space` and `Ctrl+M` of the window, because claiming those system wide would
take them away from every other app. A combination another app already holds is named in the settings
instead of failing, and the whole set can be switched off there.

## 5. Tray icon and minimize to tray

The app is built to keep running, yet it only lives in the taskbar. A tray icon showing the
current song in its tooltip, left-click to mute and unmute, right-click for the favorites (the
same content as `TaskbarJumpList` builds) and a "close to tray" option turn it into a background
app instead of a window. Pairs with the media key work above.

## 6. Bandwidth and power guard

The permanent 2 to 6 Mbit/s of background streaming is the one real cost of the design. An eco
mode keeps only the top few favorites open on a metered connection or on battery below a set
percentage, and re-opens the rest on Wi-Fi or AC power. A live "currently using about 3.2 Mbit/s"
readout in the settings makes the cost visible instead of implied. Uses `NetworkInformation` and
the system power status, mostly inside `RadioEngine`.

## 7. Play history - built in 1.10.0

A rolling 12-hour history of everything every favorite played, as a third tab next to Stations and
Favorite tracks, with the title tidied up (`TrackTitle`) and a heart per entry. It is deliberately
kept separate from the rewind and recording features above: `PlayHistory` is a list of titles, not
of audio, so 1 and 2 still have to bring their own buffer.

## 8. Links out to Spotify and YouTube - built in 1.15.0

A favorite track could only be copied as text. A find button now sits next to the song that is
playing, in both windows, and next to every favorite track and every entry in the play history,
with Spotify and YouTube behind it. Apple Music was deliberately left out: two services cover
where the songs actually go, and each extra one is another row in a menu that has to stay a glance.

`Core/Models/TrackLinks` builds the links and is the whole of the logic: the words of the title
with the separator dropped, escaped into `spotify:search:`, `open.spotify.com/search/` or
`youtube.com/results`. A stream title is not a track id, so the link searches rather than opens
the song, which has the pleasant side effect of covering the stations that send "Title - Artist"
instead of "Artist - Title", because a search does not care about the order. `MainWindow` asks
`Launcher.QueryUriSupportAsync` whether `spotify:` has a handler before using it, so Spotify opens
in its app when it is installed and in its web player when it is not, without Windows offering to
go looking for one in the Store.

The iTunes Search API that `TrackDurations` already calls could pin the exact track rather than a
search, and would give an Apple Music link for free, but it costs a lookup per click and misses
often enough that a search is the better answer for a radio title.

## 9. Smarter zap rules

The zapper is the identity of the app, so give it knobs:

- Per favorite: never zap to this one, or only zap to these. A news station should not be a music fallback.
- A disliked songs list, so it zaps away from a title that was thumbed down.
- Skip the news at the top of the hour, which is predictable and time based.
- After a break ends: return to the station you came from, or stay where you landed.

All of this belongs in `AdBreakZapper` and `AppSettings`, the UI-free and fully tested core, so it
is cheap to build and cheap to test.

## 10. Import and export of favorites, and favorite sets

A shareable JSON (or `.m3u`) of the favorites makes it possible to move machines, keep a backup or
publish a preset. Alongside it, named favorite sets (Work, Weekend, Dance) keep the cap of 20 open
streams while removing the ceiling as a practical limit: only the active set streams.

## 11. More languages - built in 1.18.0

The station list is worldwide and most of its listeners do not read English, so the player now speaks the
language Windows is set to. There are ten: English, Chinese (simplified), Spanish, Portuguese (Brazil),
French, German, Japanese, Ukrainian, Italian and Dutch: mostly the widest-spoken languages of the Windows desktop,
with Dutch and Ukrainian chosen by request. Hindi and Bengali are spoken by more people but come after these,
and Arabic and Urdu would first need the layout checked right to left. Adding a language is a folder of
texts and a line in a list.

`Strings\<language>\Resources.resw` holds every text. The XAML gets its texts through an `x:Uid` on each
element, so `MainWindow.xaml` has no English left in it, and the texts built in code (`StatusTexts`,
`SongTexts`, `LoudnessTexts`, the jump list, the error messages) go through `Localizer.Get` and
`Localizer.Format`, one place that wraps the `ResourceLoader`. `Localizer.Use` runs first thing in the
`MainViewModel` constructor, before the window is built from its XAML, because that is when the `x:Uid`s are
looked up. It takes the language from `AppSettings.Language` and otherwise from the first language in the
Windows list that the app has, with `nl-BE` and `pt-PT` falling under Dutch and Portuguese and traditional
Chinese falling through to the next language rather than being shown in the wrong script.

The language is chosen once and stays for the run. A window built from XAML and its bindings is not redrawn
in another language, so the language box in the settings only says "restart to switch". That is also what
makes the two problems from the plan cheap: `MainViewModel.AllCountries` is now an instance property set from
the language at the start, and it is stored in the settings as `null` and never as text, so a country choice
survives switching languages without needing a sentinel of its own. The dates and numbers, which were pinned
to `en-US`, follow the language: `Localizer.Use` sets the current culture, and `FavoriteTrack`,
`PlayedTrack` and the catalog line only ask for the short date and the clock. `Localizer.Use` also sets the
culture of the threads that come later, which keeps the number formats of one window consistent.

What is not done: the installer is still English. A WiX MSI is built per culture, and the release workflow
publishes one file per architecture under a name the README links to, so translating it means either an
installer per language or the language transforms an MSI can carry, and neither is worth it for the handful of
dialogs before the app is running. The country and genre names come from the station list in English and
stay that way; a mapping per language for the few dozen countries that matter would make the country box read
like the rest of the window and is the obvious next step.
## 12. Start the song clock when the song starts, not when its title arrives - built in 1.14.0

The unmarked ad break detection of item 4 in the README used to time a song from the moment its title
came in: `StationStream.WatchSongEndAsync` took `DateTime.UtcNow` there and set `_songEndTimer` to
`title arrival + length + SongOverrun`, 30 seconds. That assumes the title and the song start together,
and plenty of stations do not work that way. Their playout system announces the next item while the
current one is still fading, or over the jingle in between, so the title runs 10 to 20 seconds ahead of
the audio. The clock then started too early and the 30 seconds of slack quietly shrank to 10: the song
was marked overdue while it was still playing, and the first presenter or station ident after it was
enough for `UnmarkedAdBreak` to call a break that was not one.

`Core/Audio/SongClock` anchors the clock to the audio instead. A title starts the clock but not the
timer; the first two windows of music in a row after it (about 10 seconds, because one window alone is
noise) say where the song really began, and the clock is set back to the start of that run. A title that
arrives late, while the song is already playing, cannot move the song forward, so the clock never starts
later than the title. Streams that are not classified at all (HLS, which skips the relay) and streams
where 45 seconds pass without either music or speech - a quiet or instrumental intro - fall back to
timing from the title as they did before, nudged by the 5-second watchdog rather than by a window.

With the clock on the song itself, `Overrun` means what it says again and came down from 30 to 20
seconds, which makes the real breaks show up sooner too. Two smaller holes went with it: a station that
replaces the song title with its own name or the name of the program between the song and the ads no
longer resets the clock (only a title shaped like "Artist - Title" is a new song), and only such a title
counts as evidence of a song in `Channel.StateOf`, so a station name during a break is no longer mistaken
for music.

## 13. Start with Windows

The app is meant to be on all day, and a radio you have to remember to open is a radio you forget.
A switch in the settings, next to the global hotkeys, that lets Windows start the player with the
session, and a second one beside it for starting quietly: minimized, and muted until you ask for
sound, so a machine that boots does not start playing at whatever volume it was left at.

The installer is a plain MSI and the app is unpackaged (`WindowsPackageType` is `None`), so there is
no `StartupTask` manifest extension to declare. It is a value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` naming the installed executable, written and
removed by the app itself rather than by the installer, because it is a preference and not part of
being installed, and per user, so it neither needs the elevation the MSI has nor turns itself on for
everyone on the machine. The switch should read the key back rather than trust the setting: the
Startup tab of Task Manager can switch an entry off behind the app's back, and a switch that says
"on" while Windows disagrees is worse than no switch.

A `--minimized` argument carries the quiet start from the registry value into `App.OnLaunched`, which
is where this meets 5: with a tray icon it should start into the tray rather than the taskbar, so the
two are best built together.

## 14. Guided tour on first launch

A first-time window is a grid of stations and an empty favorites list, with nothing that says what
makes this player different from any other. A short, dismissable tour on the very first launch -
a few coach marks in sequence, not a modal wizard - points at the pieces that are not obvious from
looking: heart a station to favorite it, the zap button, the loudness switch in settings, the find
button that links out to Spotify and YouTube, and the compact window.

`AppSettings` gains a `HasSeenTour` flag (default `false`), checked once in `App.OnLaunched` /
`MainWindow` alongside the other first-run state, and set the moment the tour ends or is skipped, so
it never shows again and never blocks a settings-file-less fresh install from being reset back into
one either. A "show the tour again" entry in settings covers the case of an update landing a new
tour step later.

## 15. Production ready

No new features: this is the work that makes what exists safe to ship to people who cannot ask the author
what went wrong. Ranked by payoff per effort. Auto-update and its release pipeline are built (1.19.0), so what
is left of it is proving it works on a real release: 5 comes as soon as a second release exists, and 3 can
start now.

1. **Velopack release pipeline - built in 1.19.0.** `release.yml` runs `vpk pack` per architecture through
   `build-installer.ps1`, on the channels `win-x64` and `win-arm64`, after a `vpk download github` so the
   delta package can be made against the previous release. The release gets the two Setup files under fixed
   names for the README links, this version's packages, and the releases feeds. The WiX MSI is gone. The app
   is installed per user in `%LOCALAPPDATA%\ZapperRadioApp`, apart from the settings in
   `%LOCALAPPDATA%\ZapperRadio`, because uninstalling deletes the whole install folder. What is left is the
   people who still have the MSI (up to 1.18): it installs for all users and has no update check, so they only
   learn about the change from the README and the website, and have to uninstall it by hand once. There is no
   migration step and the app cannot see the old install from the new one.

2. **Code-sign the app and the installer.** The Setup.exe and the 280 or so files in the package are unsigned,
   which hits SmartScreen warnings and looks suspicious to antivirus software. Azure Trusted Signing or a
   certificate, passed to `vpk pack --signParams` (or `--azureTrustedSignFile`) in `build-installer.ps1`.

3. **Crash handling and logging.** There is no unhandled-exception handling and no log file, and `AppUpdater`
   (like `IcyProxy` and `StationStream`) swallows exceptions: a failed check only shows its message in the
   settings, and a failure to start the updater on close is not reported at all. Hook `Application.UnhandledException`,
   `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`, write a rolling log to
   `%LOCALAPPDATA%`, and add an "Open log folder" button in settings so a bug report carries something useful.

4. **CI builds the WinUI app on pull requests.** CI only runs the Core tests, and only on pushes to main; the
   app project first compiles in the release job itself. Build both architectures on PRs, turn on
   warnings-as-errors, add NuGet caching and Dependabot, and guard the release so it cannot be published from a
   broken build or from a commit that did not bump the version.

5. **Test the update path end to end.** Done by hand so far: a Setup built locally installs, starts, finds
   no newer release on GitHub and reports "up to date", a graceful close exits the process, an uninstall
   removes the install folder, and a second version packs a 0.2 MB delta against the first. Not done, because it needs two real
   releases: install 1.19.0 from the release, publish the next version, and check that it downloads (as a
   delta), installs on close, restarts from the button and keeps the settings, on both architectures.
   GitHub's unauthenticated API allows 60 requests an hour per IP, which shared networks can hit, so consider
   hosting the feed on zapperradio.com or a CDN. A beta channel would let a release be staged first.

6. **Version and protect user data.** Auto-update pushes new builds to everyone, so `settings.json` and the
   history file need a schema version and a migration path. `AppSettings.Save` already writes to a temp file
   and moves it into place; check what a corrupt or half-written file does on load. It should be backed up and
   replaced by defaults, not crash the app.

7. **Soak test.** The app runs 20 decoded streams plus YAMNet at once, so run it for 24 hours or more and watch
   memory, handles and CPU. Also test network drops, sleep and wake, and a change of network. Check that a
   reconnect backs off and that every request has a timeout.

8. **Legal and metadata.** A privacy note that lists the network calls: the update check, the station
   directory, logos and popularity data. Third-party notices for NAudio, Velopack, the Windows App SDK and
   YAMNet, and a check of the terms of the station and logo data. Fix the small things: look at what Installed
   apps shows for the Velopack install (the MSI's `ARPURLINFOABOUT` link to `rb2rs.freemyip.com` went with the
   MSI), and the exe has no company, copyright or file description.

9. **Runtime prerequisites and installer behavior.** The release build is self-contained (`dotnet publish
   --self-contained true`), so .NET and the Windows App SDK are in the package and Velopack's `--framework`
   is not needed. The price is about 105 MB per architecture, which deltas keep out of the updates but not out
   of the first download. Test a clean-machine install on x64 and arm64, and upgrades from an old MSI or
   WinRadioPlayer install (a manual uninstall, see 1). Uninstall cleanup is checked on x64, including the
   auto-start entry. `vpk pack` leaves out the PDBs by default; keep them as release artifacts, so a stack
   trace from a crash can be read.

10. **Accessibility and UI-layer tests.** Screen reader names (`AutomationProperties`), keyboard-only use, high
    contrast and 150-200% scaling. Give `AppUpdater` a test seam, an update source interface a fake feed can
    drive, because it is the riskiest new code and has no tests. Add a short manual checklist for the parts CI
    cannot cover.

## Suggested order

With 3, 4, 8, 11 and 12 built, 5 is next: it pairs with the media keys and is about a day, and 13 follows
it straight away, because a player that starts with Windows wants somewhere quiet to start into. Then
1, because it is the feature that cannot be copied without also keeping every stream open.

Production ready (15) runs alongside: 1 is built, so crash logging (3) can start now, the update test (5)
follows the second release, and signing (2) is what to do before the app is pointed at people who do not know
the author.
