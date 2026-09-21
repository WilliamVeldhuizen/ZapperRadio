# Third-party notices

ZapperRadio itself is [MIT licensed](LICENSE). It is built on the components below, which are part of the installed
app. This file is installed next to `ZapperRadio.exe`, together with the `licenses` folder.

| Component | Used for | Copyright | License |
| --- | --- | --- | --- |
| [.NET runtime](https://github.com/dotnet/runtime) | Runs the app; the app carries it with it | .NET Foundation and Contributors | MIT, see below. Its own third-party notices: [THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT) |
| [Windows App SDK](https://github.com/microsoft/windowsappsdk) 2.4.0 | The WinUI 3 window, media playback and the taskbar; the app carries it with it | Microsoft Corporation | Microsoft Software License Terms (the `license.txt` in the NuGet package); notices in `licenses/WindowsAppSDK-NOTICE.txt` |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime), through the Windows App SDK (`Microsoft.WindowsAppSDK.ML` 2.1.74) | Runs the YAMNet model | Microsoft Corporation | MIT, see below; notices in `licenses/WindowsAppSDK-ML-ThirdPartyNotices.txt` |
| [YAMNet](https://www.kaggle.com/models/google/yamnet/tensorFlow2/yamnet/1) by Google, converted to ONNX by [zeropointnine/yamnet-onnx](https://huggingface.co/zeropointnine/yamnet-onnx) | Hears whether a stream plays music or speech | Google | Apache License 2.0, in `Assets/Models/LICENSE-yamnet.txt` |
| [NAudio](https://github.com/naudio/NAudio) 2.2.1 | Decodes the stream audio for the sound classifier | Mark Heath 2023 | MIT, see below |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4.2 | The view model plumbing | .NET Foundation and Contributors | MIT, see below |
| [Velopack](https://github.com/velopack/velopack) 1.2.0 | The installer and the updates | Velopack Ltd | MIT, see below |

## Data

The station list and the popularity data are not part of the app; they are downloaded when the app runs, from
[rb2rs](http://rb2rs.freemyip.com/) and [radio-browser.info](https://www.radio-browser.info/). Station names, logos and
streams belong to the stations that broadcast them, and so does every name and mark used with them. Song lengths are
looked up with Apple's [iTunes Search API](https://performance-partners.apple.com/search-api); only the length is used.
See [PRIVACY.md](PRIVACY.md) for what is requested from whom.

## The MIT license

Applies to the components marked MIT above, each with the copyright given in the table.

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Keeping this file current

The two files in `licenses` are copies of `NOTICE.txt` from the `Microsoft.WindowsAppSDK` package (2.4.0) and of
`ThirdPartyNotices.txt` from the `Microsoft.WindowsAppSDK.ML` package (2.1.74). When the Windows App SDK is upgraded,
copy the new ones from `%USERPROFILE%\.nuget\packages` over them and update the versions in the table above. Keep the
version out of the file names: a dot in the name of a file that ships with the app makes the resource indexer warn
about an invalid qualifier. The same goes for a new package with code in the installed app.
