# App icon

The chosen mark, Bolt Wave in its Z-Bolt variant, built out as the Windows app icon
in `src/ZapperRadio/Assets/ZapperRadio.ico`.

An ink tile (`#131A26`, 14/64 corner radius) carries off-white broadcast arcs and the
amber Z-bolt (`#F2A93B`). The tile keeps the mark visible on a light taskbar; on a dark
one the arcs and the bolt do the work.

## Size-specific art

An icon that is one drawing scaled down turns to mush, so the art thins out as the
frame shrinks:

| Frames | Master | What is drawn |
| --- | --- | --- |
| 256, 64, 48, 40 | `zapperradio-icon-full.svg` | Both arc pairs on each side |
| 32 | `zapperradio-icon-mid.svg` | Inner arcs only, drawn heavier |
| 24, 20, 16 | `zapperradio-icon-tiny.svg` | The Z-bolt alone; arcs this small only muddy it |

`icon-<size>.png` are the rendered frames, and `ZapperRadio.ico` here is a copy of what
was installed. The `.ico` carries the same eight frames the previous icon did
(16/20/24/32/40/48/64/256), all PNG-compressed.

## Regenerating

There is no ImageMagick dependency: the frames were rendered with headless Chrome and
packed into an `.ico` by hand. With ImageMagick installed it is one line per master, for
example:

```powershell
magick zapperradio-icon-full.svg -background none -resize 256x256 icon-256.png
```

Then pack the PNGs into an `.ico` with all eight frames.

## Microsoft Store logos

The MSIX package of the Store version takes its tiles, app list icons, Store logo and splash screen from
`src/ZapperRadio/Assets/Store`. They are rendered from the same three masters with headless Edge:

```powershell
.\design\logos\app-icon\render-store-logos.ps1
```

The app list icon (`Square44x44Logo`) uses the size-specific masters like the `.ico`; the tiles and the
splash screen put the full icon in the middle of a transparent canvas.
