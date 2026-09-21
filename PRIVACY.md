# Privacy

The ZapperRadio app has no account, no analytics and no telemetry. It does not collect anything about you and there is
no server of its own that it reports to. What it does do is talk to a handful of other services to get the station
list, the audio, and a few small extras. This page lists every one of them, so you can judge for yourself. The
[website](#the-website) is separate from the app and counts its visitors, which is described at the end.

## What stays on your PC

Everything the app remembers is stored in `%LOCALAPPDATA%\ZapperRadio` and never leaves your PC:

- `settings.json`: your settings, favorite stations, favorite songs, window positions and the loudness measured per station;
- `play-history.json`: the songs your favorites played in the last 12 hours;
- `cache\`: the station list and the logos and popularity lookups (see below).

Deleting that folder removes all of it. Uninstalling the app does not, on purpose, so that your favorites survive a
reinstall. When you switch on **Start with Windows**, the app adds one entry to your user's `Run` registry key, and it
removes that entry again when the app is uninstalled.

The music/speech recognition and the loudness measurement run on your PC, on audio that is streamed anyway. No audio is
recorded or sent anywhere. The app plays every favorite through a small relay that listens on `127.0.0.1` only, so no
other device on your network can reach it.

## What the app sends over the internet

Every request carries your IP address, as any request on the internet does, and most also a user agent that names the
app, `ZapperRadio/<version>`. Nothing else identifies you or your PC.

| What | Who receives it | What is sent | When |
| --- | --- | --- | --- |
| Radio streams | The station's own streaming server | Just a request for the stream. All your favorites (up to 20) stay connected while the app runs, muted ones too, so each of those stations sees you as a listener. | While the app runs |
| Station list | [rb2rs.freemyip.com](http://rb2rs.freemyip.com/) | A request for the list of files, and then for the newest list. Nothing about you. This server only offers plain `http`. | When the app starts |
| Popularity of stations | [radio-browser.info](https://www.radio-browser.info/) | The country you are looking at, or nothing for all countries | When you look at the stations of a country, or of all countries; kept for a day |
| Station logos | radio-browser.info, then the server the station gave for its logo | The name and country of the station, then a request for the logo image | The first time a station is shown as a favorite or as the one playing; kept until you clear it in the settings |
| Song lengths | Apple's [iTunes Search API](https://performance-partners.apple.com/search-api) | The title of the song a favorite is playing, as the station sent it, for example `Queen - Bohemian Rhapsody`, to tell when an ad break must have started. This is done for the songs of all favorites, muted ones too, at most one lookup every 4 seconds. | Whenever a favorite starts a new song |
| Updates | GitHub, from the [releases](https://github.com/WilliamVeldhuizen/ZapperRadio/releases) of this project | A request for the list of releases, and then the update itself | Half a minute after the start and every 6 hours, for an installed app only |

The app does not use Spotify or YouTube by itself. Only when you click the find button next to a song, your browser or
the Spotify app opens with a search for that artist and title, and from then on you are dealing with them.

Each of these services has its own privacy policy, and the stations decide for themselves what they do with the
requests they get. Only the streams are needed to play the radio: the station list is kept on your PC, so
the other requests can be blocked in a firewall without stopping the app from playing.

## The website

This section is about [zapperradio.com](https://zapperradio.com/) only, not about the app. The person responsible for
the website is the maintainer of this project, William Veldhuizen; questions can go in an
[issue](https://github.com/WilliamVeldhuizen/ZapperRadio/issues).

- **Visitor statistics.** The website counts its visitors with [GoatCounter](https://www.goatcounter.com/), to see how
  many people come by, from where they were sent, and with which kind of browser and screen. GoatCounter is run by
  Martin Tournoij in Ireland. It sets no cookies and stores nothing in your browser. According to its
  [privacy policy](https://www.goatcounter.com/privacy) it does not store your IP address, your full user agent or any
  tracker ID: it keeps only totals per hour, such as the page, the referring site, the browser, the operating system,
  the language, the screen width and the country, and shares nothing with third parties. To tell repeat views from
  new visitors it makes a random ID from your IP address and user agent, which lives in memory for at most 8 hours and
  is not stored. Your request does reach GoatCounter's server, so your IP address is briefly there like with every
  request. Because of this, and because the statistics cannot be traced back to a person, the website has no cookie
  banner. The counting is done by a script from `gc.zgo.at` that the page loads, and you can block it in your browser
  or with an ad blocker without anything else on the page changing.
- **Hosting.** The website is served by [GitHub Pages](https://docs.github.com/en/pages), so GitHub receives your
  request, with your IP address and user agent, as with any page it hosts. See GitHub's
  [privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
- **Latest version.** The page asks GitHub's API (`api.github.com`) for the version number of the newest release, to
  show it next to the download buttons. That request comes from your browser and carries your IP address.
- **Downloads.** The download buttons and the links to the releases go to GitHub, which then handles your request.

## Changes

If this page changes, the change is in the history of this file on GitHub. Questions can go in an
[issue](https://github.com/WilliamVeldhuizen/ZapperRadio/issues).
