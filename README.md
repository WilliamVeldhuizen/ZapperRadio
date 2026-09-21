# ZapperRadio

**Instant station switching, no ads when you tune in.**

Internet radio for Windows (.NET 10 + WinUI 3 / Windows App SDK).

## Download

Windows 10 (version 2004) or later:

- [**ZapperRadio-x64-Setup.exe**](https://github.com/WilliamVeldhuizen/ZapperRadio/releases/latest/download/ZapperRadio-x64-Setup.exe) for most PCs
- [ZapperRadio-arm64-Setup.exe](https://github.com/WilliamVeldhuizen/ZapperRadio/releases/latest/download/ZapperRadio-arm64-Setup.exe) for ARM devices (e.g. Snapdragon laptops)

All versions are on the [Releases](https://github.com/WilliamVeldhuizen/ZapperRadio/releases) page. The installer is not digitally signed, so Windows SmartScreen may warn you: choose **More info** → **Run anyway**.

It installs for your user only, without asking for administrator rights, and keeps itself up to date (see below). Versions up to 1.18 came as an MSI that installs for all users and cannot update itself; uninstall that one from **Installed apps** before you install this one. Your favorites and settings are kept.

## Why

Many stations play an ad as soon as you connect. With a regular radio player that happens every time you switch stations.
This player keeps the streams of your favorites (up to 20) **always open and muted**. When you click a favorite,
its stream is simply unmuted. You are instantly live in the broadcast, without reconnecting and without a pre-roll ad.

It does cost bandwidth: each favorite is a continuous stream of roughly 64 to 320 kbit/s, so 20 favorites usually add up to about 2 to 4 Mbit/s (at most a little over 6).

## Features

- **Station list**: the newest list from http://rb2rs.freemyip.com/ (~52,000 stations), stored locally for offline use.
- **Search** by name, genre or country, with a country filter. Typos are forgiven: `radoi 538` finds "Radio 538". The best matches come first: the whole name, then the start of the name, a word in it, the tags and the country, and a typo last. Among equally good matches the most popular stations ([radio-browser.info](https://www.radio-browser.info/) play count) lead, in the chosen country or worldwide, so the list opens on stations people actually play. Stations that failed radio-browser's last check sink below the working ones and say since when they are down. `Enter` plays the best match, `↓` moves into the results and `Esc` empties the box; the station being listened to is marked in the results, and when a country hides every match, one click searches all countries.
- **Favorites**: add with the star, reorder by dragging. Each shows the song it is playing (or a red "Advertisement" during an ad break), for stations that send Shoutcast/Icecast titles.
- **Hears music and speech**: every stream is classified locally with Google's [YAMNet](https://www.kaggle.com/models/google/yamnet/tensorFlow2/yamnet/1) and shown next to the status ("Live · muted · music"). It reuses the audio that is streamed anyway, so no extra bandwidth. HLS streams are not classified.
- **Zapp on ad breaks / speech**: for music-only listening. When your station starts an ad break, news or a presenter, the player zaps to the highest favorite in your list that is playing a song, and returns once your station plays music again, never in the middle of a song. Ads are recognized from the markers stations send in many languages (`adbreak`, `Werbung`, `Reclame`, ...). For stations without markers, the song length is looked up (iTunes Search API), and if no new title arrives shortly after the song should have ended while speech is heard, an ad break is assumed ("Probably an ad break", in yellow). Picking a station yourself ends the zapping.
- **Zaps start the song from the beginning**: the last minutes of every favorite are kept (a time-shift buffer, 5 minutes by default), so a zap to a station that is halfway through a song plays that song from its start instead. Because the break on the station you were on is then known before you hear it, the zap also cuts it off right where it begins. Picking a station yourself, with a click, a shortcut or a media key, always plays it live. You listen a little behind the broadcast, at most the length of the song a zap landed in; every zap sets that anew, and **Go live** next to the status catches up at once. The buffer holds the compressed stream, about 100 MB for 20 favorites at 128 kbit/s; set it to 2, 5 or 10 minutes on the Zapper tab, which shows what it takes for your favorites. Switched off there (**Zap to the start of the song**), no buffers are kept and their memory is freed; every station then plays live.
- **Zapper tab**: the rules the zapper follows, on a tab of its own next to Stations. Switch zapping to the start of the song on or off, with what its buffers take in memory. Leave out favorites it should never zap to, such as a news station (they are still zapped away from and returned to). Choose whether it goes back to your station after the break or stays where it landed. Switch the crossfade on or off: by default a zap fades from one station into the other over 400 ms, and the fade out ends before the break starts, so no ad leaks in.
- **Same loudness on every station**: each favorite's music is measured (EBU R128) and the loud ones are turned down to about -14 LUFS, so zapping doesn't blast your ears. It takes about a minute of music per station and is remembered afterwards. Adjust per station in the settings, or switch it off. HLS stations keep their own volume.
- **Favorite tracks**: click the heart next to a song to save it with the station and date on the **Favorite tracks** tab.
- **Play history**: the **Play history** tab lists what your favorites played in the last 12 hours, whether or not you were listening, so "what was that song four minutes ago?" has an answer. Searchable, and it survives a restart.
- **Find a song on Spotify or YouTube**: the find button next to any song (playing, favorite or in the history) opens a search in the Spotify app or web player, or on YouTube.
- **Tidier song titles**: stations that SHOUT their titles are converted to normal capitalization ("QUEEN - BOHEMIAN RHAPSODY" becomes "Queen - Bohemian Rhapsody"), while deliberate capitals like "AC/DC", "ABBA" and "R.E.M." are left alone.
- **Compact window**: the title bar button shrinks the player to just your favorites, each with its song, a status indicator (music, speech, connecting, ad break, error), a heart, and volume. It sizes itself to your favorites, and both windows remember their size and position.
- **Ten languages**: English, Dutch, German, French, Spanish, Italian, Portuguese (Brazil), Ukrainian, Chinese (simplified) and Japanese. The app follows your Windows language list and falls back to English. Change it in the settings (applies on next start).
- **Updates itself**: half a minute after the start and every six hours, the app looks for a new version on the GitHub releases page and downloads it in the background. It is installed when you close the app, or right away from the **Restart to update** button in the notice at the top of the window. Only an installed app does this, not one that runs from source. The settings have a button to check now.
- **Settings** (the gear): language, app version, checking for a new version of the app, station list info and update check, shortcuts, per-favorite loudness, and clearing the logo and popularity cache.
- **Lock screen, media keys and volume flyout**: station, song and logo appear on the Windows media card, with play, pause and next. Media keys on your keyboard, headset or Bluetooth speaker move through your favorites; while zapping is on, they pass over the ones in an ad break or talking.
- **Shortcuts**: `Ctrl+Space` stop/play, `Ctrl+M` mute, `Ctrl+F` search. From any app: `Ctrl+Alt+P` stop/play, `Ctrl+Alt+M` mute, `Ctrl+Alt+→` / `Ctrl+Alt+←` next/previous favorite. The global ones can be switched off in the settings.
- **Taskbar**: right-click the taskbar button to switch to a favorite (with its current song) or mute.
- **Robust streams**: `.pls`, `.m3u` and `.asx` playlists are resolved, HLS (`.m3u8`) plays directly, and dropped or stalled streams reconnect automatically.
- **Non-favorites** play temporarily and stop when you switch. Make one a favorite while it plays and the stream stays open.

## Build and run

```powershell
dotnet build
dotnet run --project src/ZapperRadio
dotnet test
```

Settings and the station cache are stored in `%LOCALAPPDATA%\ZapperRadio`. The app used to be called WinRadioPlayer; an existing `%LOCALAPPDATA%\WinRadioPlayer` folder is moved there on first start.

## Translating

Every text of the app is in `src/ZapperRadio/Strings/<language>/Resources.resw`, one folder per language, named by its tag (`nl-NL`, `pt-BR`, `zh-CN`). The texts in the XAML are found through `x:Uid` (`SearchBox.PlaceholderText` belongs to the element with `x:Uid="SearchBox"`), and the ones built in code through `Localizer.Get` and `Localizer.Format`, where `{0}`, `{1}` and so on mark the numbers and names that are filled in and `{0:d}` and `{0:t}` a date and a time. `en-US` is the source. A text that is in no `Resources.resw` at all shows up as its own name, which makes a typo in a key easy to spot.

To add a language, copy the `en-US` folder, translate the values, and add the language to `Localizer.Languages`. Change a text only in the `Resources.resw` files, never in the XAML, and keep the same `{n}` placeholders in every language. The installer is still English.

## Building the installer

The installer and the updates are made with [Velopack](https://velopack.io/). Install its `vpk` tool once, in the same version as the `Velopack` package in `ZapperRadio.csproj`:

```powershell
dotnet tool install -g vpk --version 1.2.0
.\build-installer.ps1                         # artifacts\velopack\ZapperRadioApp-win-x64-Setup.exe
.\build-installer.ps1 -Version 1.1.0 -Arch arm64
```

The Setup program installs the app in `%LOCALAPPDATA%\ZapperRadioApp` and adds a Start menu shortcut. That is not the `ZapperRadio` folder with the settings, because uninstalling removes the whole install folder and the favorites should survive it. .NET and the Windows App SDK are included, so nothing else needs to be installed on the target PC. The Setup program is not digitally signed, so Windows SmartScreen may ask for confirmation the first time.

The same folder gets the packages and the `releases.<channel>.json` feed that installed apps read to find updates. Each architecture has its own channel (`win-x64`, `win-arm64`). `Updates/AppUpdater` reads the feed from the GitHub releases of this repository, so an update is nothing more than a new release.

To publish a release, bump `<Version>` in `src/ZapperRadio/ZapperRadio.csproj` and push to `main`. The [Release workflow](.github/workflows/release.yml) runs on every push. When no release exists yet for that version, it builds the x64 and ARM64 installers and update packages (with delta packages against the previous release) and attaches them to a new GitHub release tagged `v<version>`. Apps that are installed pick it up within hours.

An app that is started from source (`dotnet run`) or copied around is not installed by Velopack and does not look for updates.

## Structure

- `src/ZapperRadio.Core`: downloading and parsing the station list, search, playlist resolving, the local relay that reads song titles from the streams and plays the time-shift buffers back, the ad break and music/speech rules, the loudness measurement, and settings. No UI, fully tested.
- `src/ZapperRadio`: WinUI app. `Playback/RadioEngine` manages the muted streams and plays the station you listen to from its buffer when a zap lands on it, `Playback/StationStream` is a single `MediaPlayer` with reconnect logic that also keeps the loudness of its station, `Playback/SoundClassifier` decodes the relayed audio (Media Foundation via NAudio), runs YAMNet with the ONNX Runtime that comes with the Windows App SDK and measures the loudness of the same samples, `Updates/AppUpdater` finds, downloads and installs new versions with Velopack (`ViewModels/MainViewModel.Updates.cs` schedules it and shows the result), `Shell` holds the taskbar jump list, the Windows media card and the global shortcuts, and `Localizer` picks the language and looks up the texts in `Strings`.
- `tests/ZapperRadio.Core.Tests`: xUnit tests.
- `docs/design-notes.md`: why the larger features work the way they do, per version. [ROADMAP.md](ROADMAP.md) holds what is not built yet.

## Privacy

The app has no account, no analytics, no telemetry. Everything the app remembers stays in `%LOCALAPPDATA%\ZapperRadio`, and [PRIVACY.md](PRIVACY.md) lists every request the app makes over the internet, and to whom. The website counts its visitors with [GoatCounter](https://www.goatcounter.com/), without cookies; that is described in the same file.

## License

[MIT](LICENSE). The licenses of the components that ship with the app are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and are installed with it. The station list and popularity data are downloaded at runtime from rb2rs and [radio-browser.info](https://www.radio-browser.info/) and are not part of this repository. The YAMNet model in `src/ZapperRadio/Assets/Models` is by Google, converted to ONNX by [zeropointnine/yamnet-onnx](https://huggingface.co/zeropointnine/yamnet-onnx), and licensed under the Apache License 2.0.
