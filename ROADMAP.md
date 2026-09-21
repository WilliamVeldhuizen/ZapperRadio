# Roadmap

Ideas for what to build next, ranked by payoff per effort. The list is deliberately biased
towards features that only this player can offer, because it keeps every favorite streaming
and already decodes and classifies their audio.

Nothing here is promised or scheduled; it is a working list. Items leave it once they ship: what
they do is then in the README, and why they work the way they do in
[docs/design-notes.md](docs/design-notes.md).

## 1. Time-shift buffer: zap without landing halfway into a song

The weak spot of zapping is where it lands: the player jumps to a favorite that is playing a song, but
in the middle of it, on average about 90 seconds in. Keeping the last few minutes of every favorite
lets a zap start the song on the other station from its beginning, automatically and without a button,
so a listener never falls into the middle of a song. This is not a feature next to the zapper but part
of making it work well, and it fixes the other weak spot at the same time: today the classifier hears
a break starting on the live stream, so the first second or two of it always slips through before the
zap. Played from a buffer, the break is already known before it is heard, and the zap can be cut
exactly at its boundary, with nothing of it leaking through.

The price is that the listener is behind the broadcast, but the delay does not add up: it is at most
the length of the song a zap landed in (on average about 90 seconds, never more than four minutes or
so), it is set anew by every zap rather than added to, and every break that is skipped or shortened
gives time back, so staying on a station creeps back towards live. What suffers is truly live content,
the news on the hour, traffic, a phone-in, which is what the zapper leaves anyway. A "jump to live"
button covers the times someone wants it.

Buffer the compressed bytes as they come in, not the decoded PCM: `IcyProxy` already hands every chunk
of audio, with the metadata stripped out, to its `onAudio` callback, and only what is played back from
the buffer needs decoding. PCM is about ten times larger than the source (44.1 kHz 16-bit stereo is
176 KB/s, so 5 minutes for 20 favorites is about 1 GB), while the stream itself is kbit/s ÷ 8 × seconds:
5 minutes at 128 kbit/s is 4.8 MB per station, about 100 MB for 20 favorites, and 240 MB if all of
them were 320 kbit/s. For jumping to the start of a song, 4 to 5 minutes per station is the size
needed; 30 to 60 seconds would cover only a third of the landings.

Three things for the implementation: allocate each buffer once when a stream starts and reuse it,
because every array over 85 KB lands on the Large Object Heap and a ring buffer that is created over
and over fragments it; size the buffer in time rather than in bytes, from the bitrate in the ICY
headers, so a 320 kbit/s station keeps as many minutes as a 128 kbit/s one; and make the length a
setting (2, 5 or 10 minutes) with the estimated memory next to it.

Replaying what you just missed, by hand, and recording a song from the buffer are deliberately left
out: they hang next to the zapper instead of making it better.

## 2. Smarter zap rules

The zapper is the identity of the app, so give it knobs:

- Per favorite: never zap to this one, or only zap to these. A news station should not be a music fallback.
- After a break ends: return to the station you came from, or stay where you landed.
- A crossfade of a few hundred milliseconds instead of a hard cut, both when zapping away and when
  zapping back. Every favorite is its own `MediaPlayer` already playing muted, so it is a matter of
  ramping one volume down while the other comes up, in `RadioEngine` and `StationStream`.
- A tab of its own for these settings, named **Zapper**, next to Stations, Favorite tracks and Play
  history, instead of adding them to the settings dialog.

The rules belong in `AdBreakZapper` and `AppSettings`, the UI-free and fully tested core, so they
are cheap to build and cheap to test.

## 3. Better search

Finding a station among the ~52,000 in the list is where a new user starts, and it is what decides
which favorites the zapper gets to work with.

- **A better interface.** Still open how: the design is not decided yet.
- **Cluster the stations of one broadcaster.** Many stations come with a row of variants, such as the
  non-stop or theme channels of a main station, which now show up as separate, near-identical rows.
  Group them under the main station, which can be expanded to pick a variant.
- **Preprocess the top stations per country.** `StationPopularity` now asks the radio-browser API for a
  country's most clicked stations the moment the country is picked, and caches the answer for a day.
  Preparing those rankings (the position and whatever else is worth showing) ahead of time would make
  the first pick instant and let it work offline too.

## 4. Tray icon and minimize to tray

The app is built to keep running, yet it only lives in the taskbar. A tray icon showing the
current song in its tooltip, left-click to mute and unmute, right-click for the favorites (the
same content as `TaskbarJumpList` builds) and a "close to tray" option turn it into a background
app instead of a window. Pairs with the media keys and the global hotkeys of 1.11.0.

## 5. Guided tour on first launch

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

## 6. Production ready

No new features: this is the work that makes what exists safe to ship to people who cannot ask the author
what went wrong. Ranked by payoff per effort. Auto-update and its release pipeline are built (1.19.0).

1. **Code-sign the app and the installer.** The Setup.exe and the 280 or so files in the package are unsigned,
   which hits SmartScreen warnings and looks suspicious to antivirus software. Azure Trusted Signing or a
   certificate, passed to `vpk pack --signParams` (or `--azureTrustedSignFile`) in `build-installer.ps1`.

2. **Crash handling and logging.** There is no unhandled-exception handling and no log file, and `AppUpdater`
   (like `IcyProxy` and `StationStream`) swallows exceptions: a failed check only shows its message in the
   settings, and a failure to start the updater on close is not reported at all. Hook `Application.UnhandledException`,
   `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`, write a rolling log to
   `%LOCALAPPDATA%`, and add an "Open log folder" button in settings so a bug report carries something useful.

3. **Version and protect user data.** Auto-update pushes new builds to everyone, so `settings.json` and the
   history file need a schema version and a migration path. `AppSettings.Save` already writes to a temp file
   and moves it into place; check what a corrupt or half-written file does on load. It should be backed up and
   replaced by defaults, not crash the app.

4. **Legal and metadata.** `PRIVACY.md`, `THIRD-PARTY-NOTICES.md`, the notices in `licenses/`, the exe
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

5. **Runtime prerequisites and installer behavior.** The release build is self-contained (`dotnet publish
   --self-contained true`), so .NET and the Windows App SDK are in the package and Velopack's `--framework`
   is not needed. The price is about 105 MB per architecture, which deltas keep out of the updates but not out
   of the first download. Test a clean-machine install on x64 and arm64, and upgrades from an old MSI or
   WinRadioPlayer install. The MSI (up to 1.18) installs for all users and has no update check, so its users
   only learn about the change from the README and the website, and have to uninstall it by hand once; there
   is no migration step and the app cannot see the old install from the new one. Uninstall cleanup is checked
   on x64, including the auto-start entry. `vpk pack` leaves out the PDBs by default; keep them as release
   artifacts, so a stack trace from a crash can be read.

## 7. Bandwidth and power guard

The permanent 2 to 6 Mbit/s of background streaming is the one real cost of the design. An eco
mode keeps only the top few favorites open on a metered connection or on battery below a set
percentage, and re-opens the rest on Wi-Fi or AC power. A live "currently using about 3.2 Mbit/s"
readout in the settings makes the cost visible instead of implied. Uses `NetworkInformation` and
the system power status, mostly inside `RadioEngine`.

## 8. Translated country and genre names

The app speaks ten languages since 1.18.0, but the country and genre names come from the station
list in English and stay that way, so the country box is the one part of the window that does not
follow the language. A mapping per language for the few dozen countries that matter would make it
read like the rest of the window.

## Suggested order

4 is next: it pairs with the media keys and is about a day. Then 1, because it is the feature that
cannot be copied without also keeping every stream open.

Production ready (6) runs alongside: crash logging (2) can start now, and signing (1) is what to do
before the app is pointed at people who do not know the author.
