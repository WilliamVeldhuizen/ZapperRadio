# ZapperRadio

**Instant station switching, no ads when you tune in.**

Internet radio for Windows (.NET 10 + WinUI 3 / Windows App SDK).

## Download

Windows 10 (version 2004) or later:

- [**ZapperRadio-x64.msi**](https://github.com/WilliamVeldhuizen/ZapperRadio/releases/latest/download/ZapperRadio-x64.msi) for most PCs
- [ZapperRadio-arm64.msi](https://github.com/WilliamVeldhuizen/ZapperRadio/releases/latest/download/ZapperRadio-arm64.msi) for ARM devices (e.g. Snapdragon laptops)

All versions are on the [Releases](https://github.com/WilliamVeldhuizen/ZapperRadio/releases) page. The installer is not digitally signed, so Windows SmartScreen may warn you: choose **More info** → **Run anyway**.

## Why

Many stations play an ad as soon as you connect. With a regular radio player that happens every time you switch stations.
This player keeps the streams of your favorites (up to 20) **always open and muted**. When you click a favorite,
its stream is simply unmuted. You are instantly live in the broadcast, without reconnecting and without a pre-roll ad.

It does cost bandwidth: each favorite is a continuous stream of roughly 64 to 320 kbit/s, so 20 favorites usually add up to about 2 to 4 Mbit/s (at most a little over 6).

## Features

- **Station list**: the newest list from http://rb2rs.freemyip.com/ (~52,000 stations), stored locally for offline use.
- **Search** by name, genre or country, with a country filter. Typos are forgiven: `radoi 538` finds "Radio 538". Within a country, the most popular stations ([radio-browser.info](https://www.radio-browser.info/) play count) come first.
- **Favorites**: add with the star, reorder by dragging. Each shows the song it is playing (or a red "Advertisement" during an ad break), for stations that send Shoutcast/Icecast titles.
- **Hears music and speech**: every stream is classified locally with Google's [YAMNet](https://www.kaggle.com/models/google/yamnet/tensorFlow2/yamnet/1) and shown next to the status ("Live · muted · music"). It reuses the audio that is streamed anyway, so no extra bandwidth. HLS streams are not classified.
- **Zapp on ad breaks / speech**: for music-only listening. When your station starts an ad break, news or a presenter, the player zaps to the highest favorite in your list that is playing a song, and returns once your station plays music again, never in the middle of a song. Ads are recognized from the markers stations send in many languages (`adbreak`, `Werbung`, `Reclame`, ...). For stations without markers, the song length is looked up (iTunes Search API), and if no new title arrives shortly after the song should have ended while speech is heard, an ad break is assumed ("Probably an ad break", in yellow). Picking a station yourself ends the zapping.
- **Same loudness on every station**: each favorite's music is measured (EBU R128) and the loud ones are turned down to about -14 LUFS, so zapping doesn't blast your ears. It takes about a minute of music per station and is remembered afterwards. Adjust per station in the settings, or switch it off. HLS stations keep their own volume.
- **Favorite tracks**: click the heart next to a song to save it with the station and date on the **Favorite tracks** tab.
- **Play history**: the **Play history** tab lists what your favorites played in the last 12 hours, whether or not you were listening, so "what was that song four minutes ago?" has an answer. Searchable, and it survives a restart.
- **Find a song on Spotify or YouTube**: the find button next to any song (playing, favorite or in the history) opens a search in the Spotify app or web player, or on YouTube.
- **Tidier song titles**: stations that SHOUT their titles are converted to normal capitalization ("QUEEN - BOHEMIAN RHAPSODY" becomes "Queen - Bohemian Rhapsody"), while deliberate capitals like "AC/DC", "ABBA" and "R.E.M." are left alone.
- **Compact window**: the title bar button shrinks the player to just your favorites, each with its song, a status indicator (music, speech, connecting, ad break, error), a heart, and volume. It sizes itself to your favorites, and both windows remember their size and position.
- **Ten languages**: English, Dutch, German, French, Spanish, Italian, Portuguese (Brazil), Ukrainian, Chinese (simplified) and Japanese. The app follows your Windows language list and falls back to English. Change it in the settings (applies on next start).
- **Settings** (the gear): language, app version, station list info and update check, shortcuts, per-favorite loudness, and clearing the logo and popularity cache.
- **Lock screen, media keys and volume flyout**: station, song and logo appear on the Windows media card, with play, pause and next. Media keys on your keyboard, headset or Bluetooth speaker move through your favorites.
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

```powershell
.\build-installer.ps1                         # artifacts\installer\ZapperRadio-1.0.0-x64.msi
.\build-installer.ps1 -Version 1.1.0 -Arch arm64
```

The MSI (WiX 6) installs the app in `Program Files\ZapperRadio` and adds a Start menu shortcut.
.NET and the Windows App SDK are included, so nothing else needs to be installed on the target PC.
An MSI with a higher `-Version` replaces the old installation. Your favorites are kept.
The MSI is not digitally signed, so Windows SmartScreen may ask for confirmation the first time.

To publish a release, bump `<Version>` in `src/ZapperRadio/ZapperRadio.csproj` and push to `main`. The [Release workflow](.github/workflows/release.yml) runs on every push. When no release exists yet for that version, it builds the x64 and ARM64 installers and attaches them to a new GitHub release tagged `v<version>`.

## Structure

- `src/ZapperRadio.Core`: downloading and parsing the station list, search, playlist resolving, the local relay that reads song titles from the streams, the ad break and music/speech rules, the loudness measurement, and settings. No UI, fully tested.
- `src/ZapperRadio`: WinUI app. `Playback/RadioEngine` manages the muted streams, `Playback/StationStream` is a single `MediaPlayer` with reconnect logic that also keeps the loudness of its station, `Playback/SoundClassifier` decodes the relayed audio (Media Foundation via NAudio), runs YAMNet with the ONNX Runtime that comes with the Windows App SDK and measures the loudness of the same samples, `Shell` holds the taskbar jump list, the Windows media card and the global shortcuts, and `Localizer` picks the language and looks up the texts in `Strings`.
- `tests/ZapperRadio.Core.Tests`: xUnit tests.
- `installer`: WiX project for the MSI (not in the solution, build it with `build-installer.ps1`).

## License

[MIT](LICENSE). The station list and popularity data are downloaded at runtime from rb2rs and [radio-browser.info](https://www.radio-browser.info/) and are not part of this repository. The YAMNet model in `src/ZapperRadio/Assets/Models` is by Google, converted to ONNX by [zeropointnine/yamnet-onnx](https://huggingface.co/zeropointnine/yamnet-onnx), and licensed under the Apache License 2.0.
