# Roadmap

Ideas for what to build next, ranked by payoff per effort. The list is deliberately biased
towards features that only this player can offer, because it keeps every favorite streaming
and already decodes and classifies their audio.

Nothing here is promised or scheduled; it is a working list. Items leave it once they ship: what
they do is then in the README, and why they work the way they do in
[docs/design-notes.md](docs/design-notes.md).

## 1. Guided tour on first launch

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

## 2. Production ready

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

## 3. Bandwidth and power guard

The permanent 2 to 6 Mbit/s of background streaming is the one real cost of the design. An eco
mode keeps only the top few favorites open on a metered connection or on battery below a set
percentage, and re-opens the rest on Wi-Fi or AC power. A live "currently using about 3.2 Mbit/s"
readout in the settings makes the cost visible instead of implied. Uses `NetworkInformation` and
the system power status, mostly inside `RadioEngine`.

## 4. Ad markers that run ahead of the audio

Not yet confirmed: an ad marker in the stream title counts from the moment it comes in, and
is not dated back like a break heard as speech. Some stations send their titles 10 to 20 seconds
ahead of the audio (see the song clock in the design notes). If a station sends its ad marker that
far ahead too, the zap away from it still comes too early. Worth fixing only once a station is
seen doing it; the fix would be to date the marker back to where the music stops, in
`StreamTimeline.StartOf`.

## 5. Better search

Finding a station among the ~52,000 in the list is where a new user starts, and it is what decides
which favorites the zapper gets to work with.

- **Cluster the stations of one broadcaster.** Many stations come with a row of variants, such as the
  non-stop or theme channels of a main station, which now show up as separate, near-identical rows.
  Group them under the main station, which can be expanded to pick a variant.
- **Preprocess the top stations per country.** `StationPopularity` now asks the radio-browser API for a
  country's most clicked stations the moment the country is picked, and caches the answer for a day.
  Preparing those rankings (the position and whatever else is worth showing) ahead of time would make
  the first pick instant and let it work offline too.

## 6. Translated country and genre names

The app speaks ten languages since 1.18.0, but the country and genre names come from the station
list in English and stay that way, so the country box is the one part of the window that does not
follow the language. A mapping per language for the few dozen countries that matter would make it
read like the rest of the window.

## 7. Learn from the listeners: train the break detection locally, improve it together

A large item for the longer term, and only worth starting once the app has users. The music, speech
and ad break detection is YAMNet, a general sound classifier, with hand-written rules on top. Every
station mixes its breaks differently, and a wrong call is exactly what makes the zapper annoying:
zapped away in the middle of a song, or left listening to an ad.

The listener is the one who notices, so let them say so: a zap that should not have happened, or a
break that was missed, becomes a label on the audio around it. Locally, those labels train a small
layer on top of YAMNet's output for that user's own stations, so the detection gets better on the PC
it runs on without anything leaving it. Together, the labels that users choose to share become
training data for the model that ships with the app, so every station someone corrected is recognized
better for everyone.

The place to give those labels is the **Zapper** tab: a list of the last automatic zaps
(station, time, the reason it zapped, and where it landed), each of which can be graded. A zap is
marked as right, or put in a category of what went wrong: **too early** (the music was still
playing), **too late** (part of the ad was heard), or **not an ad break** (speech, a jingle or a
quiet song taken for a break). A category for a break the zapper missed completely needs a way to
point at a moment that is not in the list, such as a button while listening. The category says more
than a plain thumbs down: too early and too late are about where the boundary was put, not about
whether there was a break, and each one trains a different part of the detection.

What needs deciding before it is built: sharing is opt-in and never the default; what is shared
should be the labels with the classifier's numbers, not the audio itself, which is copyrighted and
may contain people's voices; where it is collected and who can see it; and a section in `PRIVACY.md`
for a request the app does not make today.
