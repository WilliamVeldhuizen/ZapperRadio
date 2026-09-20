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

- Station list: the newest `stations-yyyy-MM-dd.rsd` from http://rb2rs.freemyip.com/ (~52,000 stations), stored locally for offline use.
- Search by name, genre or country, plus a per-country filter. `qmusic` also finds "Q music". Typos are forgiven (Levenshtein distance): `radoi 538` finds "Radio 538" and `klasiek` finds "NPO Klassiek". Exact matches are listed first.
- When a country is selected, its most popular stations (by [radio-browser.info](https://www.radio-browser.info/) play count) are listed first.
- Add favorites with the star, and reorder them by dragging.
- Each favorite shows the song it is playing right now (or "Advertisement" in red during an ad break), for stations that send Shoutcast/Icecast titles.
- **Hears music and speech**: the audio of each stream is classified locally with [YAMNet](https://www.kaggle.com/models/google/yamnet/tensorFlow2/yamnet/1), Google's open-source sound classifier, and shown next to the status ("Live · muted · music", "Now playing · speech"). Songs sound like music almost throughout, while ads, news and presenters mix speech with jingles, so the last half minute is weighed together. It uses the audio that is streamed anyway (no extra bandwidth) and takes about 20 ms of processing per station every 5 seconds. HLS streams are not classified.
- **Zapp on ad breaks / speech**: for listening to music only. When the station you listen to starts an ad break, or a presenter, the news or a talk show is heard, the player zaps to the highest favorite in your list that is playing a song, and zaps back once the station plays a song again. It never goes back in the middle of a song, though: the music on the station it landed on is what you are listening to, so the return waits until that station reaches its own break or starts talking, and is given up on if that takes longer than ten minutes. If that favorite starts an ad break or talking too, it zaps on, and it also zaps as soon as a favorite becomes available when none was at the start of the break. Ads are recognized from the ad markers stations send, in any of the languages the station list covers (such as the `adbreak` and `commercial-in` titles of Qmusic and JOE, `Werbung`, `Reclame` or an `adw_ad` field). Stations that do not mark their ads are recognized too: the length of each song is looked up (via the iTunes Search API, in either "Artist - Title" or "Title - Artist" order), and when no new title arrives within 20 seconds after the song should have ended and speech is heard, an ad break is assumed ("Probably an ad break", shown in yellow). The song is timed from the moment it is *heard* to start, not from the moment its title came in: plenty of stations announce the next title while the previous song is still fading or over the jingle in between, so the clock waits for the first music after the title. If the station keeps playing music, it is just a longer version or a song it sent no title for, and nothing happens; a minute of uninterrupted music also ends an assumed break. A station that puts its own name or the name of the program in the title between the song and the ads does not reset that clock either, so the break behind it is still found. Streams that cannot be listened to fall back to the overdue song alone. Favorites that are heard playing music are zapped to even when they send no title (or only a program name), and favorites where someone talks are never zapped to. Picking a station yourself ends the zapping, and if you pick it during its ad break or while someone talks, that break is not zapped away from.
- **Same loudness on every station**: one station is mastered several decibels louder than the next, which is the most annoying part of zapping. The audio of every favorite is decoded anyway, so how loud its music really is is measured from it the way broadcasters do it (EBU R128 / ITU-R BS.1770: K-weighted, over gated 400 ms blocks), and the loud ones are turned down to about -14 LUFS, the level streaming services normalize to. Only the windows that are heard as music count, so ads and presenters do not skew it, and a station is corrected by at most 12 dB down or 6 dB up. It takes about a minute of music per station, after which the measurement is remembered, so from the next start it is right from the first second. In the settings each favorite shows what it measured, with a slider to make it quieter or louder yourself, and the whole thing can be switched off. Stations that cannot be listened to (HLS) keep the volume they come in at.
- **Favorite tracks**: hear a song you like? Click the heart next to it at the bottom. The song is saved with the station and date on the **Favorite tracks** tab, next to the **Stations** tab, where you can find it on Spotify or YouTube, copy its title or remove it.
- **Play history**: the **Play history** tab lists everything your favorites played over the last 12 hours, newest first, whether or not you were listening to them, so "what was that song four minutes ago?" and "what did I zap away from?" both have an answer. Each entry names the station and the time; the heart keeps a song as a favorite track, the find button opens it on Spotify or YouTube, and the copy button puts the title on the clipboard. The search box above the list narrows it down to the songs whose title, artist or station matches every word you type, so a song you half remember is a few letters away. A station that repeats a title (which happens on every reconnect) is not listed twice, ad markers are left out, and everything older than 12 hours is dropped, as are the oldest entries beyond 2,000. The list survives a restart, in `%LOCALAPPDATA%\ZapperRadio\play-history.json`, and the button next to the tab empties it.
- **Find a song on Spotify or YouTube**: the find button next to the song that is playing, and next to every favorite track and every entry in the play history, searches for it on Spotify or on YouTube, which closes the loop from "heard it on the radio" to "saved in my playlist" without typing anything over. Spotify opens in its app when it is installed and in its web player when it is not. A stream title is not a track id, so the link searches rather than opens the song: the artist and the title are looked for as plain words, which also covers the stations that send the two the other way around.
- **Tidier song titles**: plenty of stations shout their library, either whole ("QUEEN - BOHEMIAN RHAPSODY") or just one of its two fields ("MELISSA ETHERIDGE - Like The Way I Do", "Drill Instructor - CAPTAIN JACK"), while other capitals are meant the way they are written ("AC/DC - T.N.T.", "ABBA", "R.E.M."). Since only the station knows which is which, the guess is a careful one. The artist and the song are judged separately, and a field is only considered when it holds no lower case letter at all, because whoever wrote it wrote it in capitals throughout. Such a field is still left alone when it is one short word, a dotted abbreviation or a name with a number in it, which is what a deliberate name looks like: "AC/DC - THUNDERSTRUCK" becomes "AC/DC - Thunderstruck", "ABBA - SOS" stays as it is, and "2PAC" keeps its capitals. The rest gets a capital where a word starts, taking apostrophes into account ("DON'T" becomes "Don't", "O'CONNOR" becomes "O'Connor"). The price of the guess is a name that really is all capitals and long enough to look like a sentence, such as BLACKPINK.
- **Compact window**: the button in the title bar shrinks the player to a small window with nothing but your favorites, each with the song it is playing now. Click one to listen. A small indicator per favorite says what it sounds like: a green note while it plays music, a green speech bubble while someone talks, yellow while it is connecting or is probably in an ad break, and red during an ad break or when the stream will not play. The song that is playing has its heart here too, to save it to your favorite tracks, and the volume slider and mute button sit right under it. The same button switches back to the full window for searching, adding favorites, the favorite tracks and the play history. The compact window sizes itself to your favorites, so there is no empty space under the last one and none of them are hidden behind a scrollbar, and it is never maximized: a whole screen of favorites is the full window with the middle left out. Both windows keep their own size and position, and the full window also keeps whether it was maximized, so switching brings each back the way you left it, and the window you last used is the one you get on the next start.
- **Ten languages**: English, Dutch (Nederlands), German (Deutsch), French (Français), Spanish (Español), Italian (Italiano), Portuguese (Brazil), Ukrainian (Українська), Chinese (simplified, 中文) and Japanese (日本語). The app starts in the first language of your Windows language list that it speaks, and falls back to English. The settings have a language box to pick another one, which takes effect the next time the app starts. The choice also decides how dates and numbers are written, so a window reads in one language throughout. The names of stations, countries and genres come from the station list and stay as they are there, and so do the song titles.
- **Settings**, behind the gear next to the tabs: the language, what the app is, its version and a link to this repository, which station list is in use and when it was downloaded, with a button to check for a newer one, the shortcuts with a switch for the ones that work from any app, the measured loudness of each favorite with a button to measure it again, and a button to clear the cached logos and most-played lists so every logo is looked up again (useful when a station shows the wrong one). The station list itself is kept, since it is the one thing that also works offline.
- **Lock screen, media keys and the volume flyout**: the station, the song and the station logo are shown on the Windows media card, in the volume flyout and on the lock screen, with play, pause and next. The same card is what the media keys of a keyboard, a headset or a Bluetooth speaker press, where next and previous move through your favorites, so you can zap without touching the window. The card is the app's own: each favorite is a player of its own and twenty of them would each claim the card, so the per-player overlay stays off and the app feeds one card from what the window shows.
- `Ctrl+Space` to stop or play, `Ctrl+M` to mute or unmute, `Ctrl+F` to search, which goes to the search box of the tab you are on.
- **The same shortcuts from any app**: `Ctrl+Alt+P` to stop or play, `Ctrl+Alt+M` to mute or unmute, and `Ctrl+Alt+→` and `Ctrl+Alt+←` for the next and previous favorite, also while you are working somewhere else. On `Ctrl+Alt` rather than `Ctrl`, because claiming `Ctrl+Space` system wide would take it away from every other app. A combination another app already holds is left to that app and named in the settings, where the whole set can also be switched off.
- Right-click the taskbar button to switch to a favorite (each shown with its current song) or to mute and unmute, without switching to the window.
- `.pls`, `.m3u` and `.asx` playlists are resolved to the actual stream. HLS (`.m3u8`) is played directly.
- Dropped or stalled streams reconnect automatically.
- A station that is not a favorite plays temporarily and stops when you switch. If you make it a favorite while it plays, the stream stays open.

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
