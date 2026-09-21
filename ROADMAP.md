# Roadmap

Ideas for what to build next, ranked by payoff per effort. The list is deliberately biased
towards features that only this player can offer, because it keeps every favorite streaming
and already decodes and classifies their audio.

Nothing here is promised or scheduled; it is a working list. Items leave it once they ship: what
they do is then in the README, and why they work the way they do in
[docs/design-notes.md](docs/design-notes.md).

## 1. Live rewind / instant replay (30-60 s ring buffer per favorite)

Every favorite is already decoded for the sound classifier, so keeping the last minute of PCM
per station in a ring buffer costs little extra. It makes it possible to jump back to the start
of the song you zapped into, or to replay what you just missed - across all 20 favorites at once,
which no other radio player can do. Hooks into `IcyProxy`'s audio callback and `StationStream`.
The largest effort on this list and the biggest differentiator.

The play history covers the titles of everything the favorites played, but it is a list of titles
and not of audio, so this has to bring its own buffer.

## 2. Auto-record the current song

`IcyProxy` already knows exactly when a title changes, so track boundaries come for free. A
"record this song" button (and an optional "record everything I heart") writes the segment from
the ring buffer onwards to `%MUSIC%\ZapperRadio\Artist - Title.mp3`, tagged. Together with the
rewind buffer it can record a song that was already halfway through when you noticed it. Intended
for personal use of a broadcast, which the README should say plainly.

## 3. Tray icon and minimize to tray

The app is built to keep running, yet it only lives in the taskbar. A tray icon showing the
current song in its tooltip, left-click to mute and unmute, right-click for the favorites (the
same content as `TaskbarJumpList` builds) and a "close to tray" option turn it into a background
app instead of a window. Pairs with the media keys and the global hotkeys of 1.11.0.

## 4. Bandwidth and power guard

The permanent 2 to 6 Mbit/s of background streaming is the one real cost of the design. An eco
mode keeps only the top few favorites open on a metered connection or on battery below a set
percentage, and re-opens the rest on Wi-Fi or AC power. A live "currently using about 3.2 Mbit/s"
readout in the settings makes the cost visible instead of implied. Uses `NetworkInformation` and
the system power status, mostly inside `RadioEngine`.

## 5. Smarter zap rules

The zapper is the identity of the app, so give it knobs:

- Per favorite: never zap to this one, or only zap to these. A news station should not be a music fallback.
- A disliked songs list, so it zaps away from a title that was thumbed down.
- Skip the news at the top of the hour, which is predictable and time based.
- After a break ends: return to the station you came from, or stay where you landed.

All of this belongs in `AdBreakZapper` and `AppSettings`, the UI-free and fully tested core, so it
is cheap to build and cheap to test.

## 6. Import and export of favorites, and favorite sets

A shareable JSON (or `.m3u`) of the favorites makes it possible to move machines, keep a backup or
publish a preset. Alongside it, named favorite sets (Work, Weekend, Dance) keep the cap of 20 open
streams while removing the ceiling as a practical limit: only the active set streams.

## 7. Translated country and genre names

The app speaks ten languages since 1.18.0, but the country and genre names come from the station
list in English and stay that way, so the country box is the one part of the window that does not
follow the language. A mapping per language for the few dozen countries that matter would make it
read like the rest of the window.

## 8. Start with Windows

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
is where this meets 3: with a tray icon it should start into the tray rather than the taskbar, so the
two are best built together.

## 9. Guided tour on first launch

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

## 10. Production ready

No new features: this is the work that makes what exists safe to ship to people who cannot ask the author
what went wrong. Ranked by payoff per effort. Auto-update and its release pipeline are built (1.19.0), so what
is left of it is proving it works on a real release: 4 comes as soon as a second release exists, and 2 can
start now.

1. **Code-sign the app and the installer.** The Setup.exe and the 280 or so files in the package are unsigned,
   which hits SmartScreen warnings and looks suspicious to antivirus software. Azure Trusted Signing or a
   certificate, passed to `vpk pack --signParams` (or `--azureTrustedSignFile`) in `build-installer.ps1`.

2. **Crash handling and logging.** There is no unhandled-exception handling and no log file, and `AppUpdater`
   (like `IcyProxy` and `StationStream`) swallows exceptions: a failed check only shows its message in the
   settings, and a failure to start the updater on close is not reported at all. Hook `Application.UnhandledException`,
   `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`, write a rolling log to
   `%LOCALAPPDATA%`, and add an "Open log folder" button in settings so a bug report carries something useful.

3. **CI builds the WinUI app on pull requests.** CI only runs the Core tests, and only on pushes to main; the
   app project first compiles in the release job itself. Build both architectures on PRs, turn on
   warnings-as-errors, add NuGet caching and Dependabot, and guard the release so it cannot be published from a
   broken build or from a commit that did not bump the version.

4. **Test the update path end to end.** Done by hand so far: a Setup built locally installs, starts, finds
   no newer release on GitHub and reports "up to date", a graceful close exits the process, an uninstall
   removes the install folder, and a second version packs a 0.2 MB delta against the first. Not done, because it needs two real
   releases: install 1.19.0 from the release, publish the next version, and check that it downloads (as a
   delta), installs on close, restarts from the button and keeps the settings, on both architectures.
   GitHub's unauthenticated API allows 60 requests an hour per IP, which shared networks can hit, so consider
   hosting the feed on zapperradio.com or a CDN. A beta channel would let a release be staged first.

5. **Version and protect user data.** Auto-update pushes new builds to everyone, so `settings.json` and the
   history file need a schema version and a migration path. `AppSettings.Save` already writes to a temp file
   and moves it into place; check what a corrupt or half-written file does on load. It should be backed up and
   replaced by defaults, not crash the app.

6. **Soak test.** The app runs 20 decoded streams plus YAMNet at once, so run it for 24 hours or more and watch
   memory, handles and CPU. Also test network drops, sleep and wake, and a change of network. Check that a
   reconnect backs off and that every request has a timeout.

7. **Legal and metadata.** `PRIVACY.md`, `THIRD-PARTY-NOTICES.md`, the notices in `licenses/`, the exe
   metadata and a versioned user agent are in place. Still open:

   - **Installed apps.** Check what the Velopack install shows there (name, publisher, icon) on a machine that
     has one. Velopack takes the publisher from `--packAuthors`; it has no link to an about page.
   - **rb2rs.** The station list comes from a bare directory listing on `rb2rs.freemyip.com` that only serves plain
     `http`, with no terms and no contact on it. Ask its owner whether the app may use it, and whether it can be
     served over `https`, or mirror the list yourself.
   - **radio-browser.info.** Its documentation asks clients to find the servers with a DNS lookup of
     `all.api.radio-browser.info` instead of a fixed list, and to send a `/json/url` request for every station a
     user plays, which is what marks stations as popular. The app does neither. The second is a new request per
     play, so it needs a line in `PRIVACY.md` as well.
   - **Windows App SDK license.** Section 3 makes the app's own terms the place where end users agree to
     conditions that protect Microsoft at least as much as the Windows App SDK license does, and asks for an
     indemnity. The MIT license's disclaimer of warranty and liability is the only such text now; decide whether
     that is enough, or add a short terms page to the Setup program.
   - **Website statistics.** The website counts visitors with GoatCounter (`zapperradio.goatcounter.com`) and
     `PRIVACY.md` has a section on it. There is no data processing agreement (verwerkersovereenkomst) with
     GoatCounter, and its privacy policy does not offer one. It states that it stores no IP address, no full user
     agent and no cookies, but the IP address is briefly processed in memory. Decide whether that is enough, or ask
     its operator for a DPA. Check in the GoatCounter settings that nothing else is enabled (such as collecting
     more than the defaults), and that the data retention fits the privacy text.

8. **Runtime prerequisites and installer behavior.** The release build is self-contained (`dotnet publish
   --self-contained true`), so .NET and the Windows App SDK are in the package and Velopack's `--framework`
   is not needed. The price is about 105 MB per architecture, which deltas keep out of the updates but not out
   of the first download. Test a clean-machine install on x64 and arm64, and upgrades from an old MSI or
   WinRadioPlayer install. The MSI (up to 1.18) installs for all users and has no update check, so its users
   only learn about the change from the README and the website, and have to uninstall it by hand once; there
   is no migration step and the app cannot see the old install from the new one. Uninstall cleanup is checked
   on x64, including the auto-start entry. `vpk pack` leaves out the PDBs by default; keep them as release
   artifacts, so a stack trace from a crash can be read.

9. **Accessibility and UI-layer tests.** Screen reader names (`AutomationProperties`), keyboard-only use, high
   contrast and 150-200% scaling. Give `AppUpdater` a test seam, an update source interface a fake feed can
   drive, because it is the riskiest new code and has no tests. Add a short manual checklist for the parts CI
   cannot cover.

## Suggested order

3 is next: it pairs with the media keys and is about a day, and 8 follows it straight away, because
a player that starts with Windows wants somewhere quiet to start into. Then 1, because it is the
feature that cannot be copied without also keeping every stream open.

Production ready (10) runs alongside: crash logging (2) can start now, the update test (4) follows the
second release, and signing (1) is what to do before the app is pointed at people who do not know the
author.
