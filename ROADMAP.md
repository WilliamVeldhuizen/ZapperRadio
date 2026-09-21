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

## 7. More languages

Every string in the app is English today, written out where it is used. The station list is worldwide
and most of its listeners are not, so the player should speak the language Windows is set to, starting
with the ones the favorites are in: Dutch, German, French and Spanish.

The work is mostly mechanical: an `x:Uid` on each XAML element and a `Resources.resw` per language,
a `ResourceLoader` for the strings that are built in code (`StatusTexts`, `SongTexts`,
`JumpListCommand.Title`, the error messages and the settings texts), and the installer texts on top
of that. The dates and times are pinned to `en-US` in `FavoriteTrack`, `PlayedTrack` and
`MainViewModel`, which should follow the chosen language instead.

Two things are not mechanical. `MainViewModel.AllCountries` is the text "All countries" *and* the
value the country box is compared against to mean "no filter", so as soon as it is translated the
comparisons stop matching for anyone who switches language; it needs a sentinel of its own, separate
from what is shown. And the country and genre names come from the station list in English, so either
they stay English while the rest of the window is translated, or a mapping per language is kept for
the few dozen countries that matter. Neither is hard, but both decide how finished the result feels.

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

## Suggested order

3 is next: it pairs with the media keys and is about a day, and 8 follows it straight away, because
a player that starts with Windows wants somewhere quiet to start into. Then 1, because it is the
feature that cannot be copied without also keeping every stream open.
