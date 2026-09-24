# Jello Client v2

A Roblox bootstrapper for FastFlags, modifications and optimizations —
rewritten from the v1 Electron/Python build as a single WPF app.
.NET 8, no NuGet dependencies, acrylic window with rounded corners on
Windows 10 1803+ and Windows 11.

Unlike v1, which discovered an existing Roblox or Bloxstrap install and wrote
settings into it, v2 seeds its own versions the way Bloxstrap does — resolving
the channel, downloading and verifying packages, and extracting them into its
own version tree under `%LOCALAPPDATA%\JelloClient`.

## Run it

```
cd JelloClient
dotnet run
```

## Ship it

**Self-contained, single file — this is what the GitHub release ships.** The
.NET 8 Desktop Runtime is bundled inside the executable, so the person running
it does **not** need to install .NET (or anything else) first. Just download
`JelloClient.exe` and run it.

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Framework-dependent, single file — smaller, but the machine must already have
the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0):

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Output lands in `bin/Release/net8.0-windows/win-x64/publish/JelloClient.exe`.

### Runtime requirements

- **.NET runtime** — not required for the released build. The self-contained
  single file carries its own copy of .NET 8.
- **WebView2** — used only for the optional in-app browser panel. The Evergreen
  runtime is preinstalled on Windows 11 and virtually all Windows 10 machines
  (delivered with Microsoft Edge / Windows Update). If it is ever missing, the
  app detects that and degrades gracefully rather than failing to start.

## Layout

```
JelloClient.csproj
GlobalUsings.cs           restores System.IO (see note below)
app.manifest              per-monitor v2 DPI
Assets/icon.ico           app + window icon
Assets/logo.png           title bar and About mark
App.xaml(.cs)             theme merge, system accent
MainWindow.xaml(.cs)      shell and page content
Theme/Palette.xaml        colours and metrics
Theme/Icons.xaml          Lucide geometry
Theme/Controls.xaml       control styles
Interop/Native.cs         all P/Invoke
Interop/WindowEffects.cs  acrylic, corners, capture exclusion, move lock
Interop/SystemAccent.cs   accent lookup
Roblox/Paths.cs           %LOCALAPPDATA%\JelloClient layout
Roblox/Deployment.cs      mirror selection, channel, version resolution
Roblox/PackageManifest.cs v0 manifest parser + package directory map
Roblox/Installer.cs       download, verify, extract, stage
Roblox/Launcher.cs        process start + singleton mutex
Services/SettingsStore.cs persisted settings and FastFlags
```

## GlobalUsings.cs

`UseWPF` drops `System.IO` from the SDK's implicit usings, so `Path`, `File`,
`Directory`, `FileStream` and friends don't resolve even with
`ImplicitUsings` enabled. `GlobalUsings.cs` adds it back for the whole project.
Don't delete it. Nothing here imports `System.Windows.Shapes`, so there's no
ambiguity between `System.IO.Path` and the WPF `Path` shape.

## How the deployment pipeline works

This mirrors Bloxstrap's `RobloxInterfaces/Deployment.cs` and `Bootstrapper.cs`.

1. **Mirror selection.** Five setup CDNs are raced in parallel, staggered by a
   priority delay so `setup.rbxcdn.com` gets a one-second head start over the
   AWS/Akamai/CacheFly mirrors and four seconds over the S3 bucket. Each is
   validated by fetching `/versionStudio` and checking the body equals
   `version-012732894899482c` — the hash of the last MFC Studio deployment,
   which never changes and so doubles as a health check. First valid response
   wins; the rest are cancelled.

2. **Version resolution.** `GET /v2/client-version/WindowsPlayer` against
   `clientsettingscdn.roblox.com`, falling back to `clientsettings.roblox.com`.
   A non-production channel appends `/channel/{name}`, and a 401/403/404 on a
   non-default channel is surfaced as `InvalidChannelException` rather than a
   generic HTTP error. Results are cached per channel and binary type.

3. **Package manifest.** `{BaseUrl}/channel/common/{versionGuid}-rbxPkgManifest.txt`.
   The format is `v0` followed by repeating four-line records — filename, MD5
   signature, packed size, unpacked size. Parsing stops at
   `RobloxPlayerLauncher.exe`, matching Bloxstrap.

4. **Download.** Each package is fetched from
   `{BaseUrl}/channel/common/{signature}` into `Downloads/{signature}`.
   Already-present files are MD5-checked and reused; if the stock Roblox
   bootstrapper has already downloaded the same blob to
   `%LOCALAPPDATA%\Roblox\Downloads\{signature}`, it's copied instead of
   re-downloaded. Five attempts with linear backoff, and a checksum mismatch
   deletes the file and retries.

5. **Extraction.** Each package extracts into the directory the stock
   bootstrapper uses, per the map in `PackageMap.Player` — `RobloxApp.zip` and
   `Libraries.zip` at the root, `content-textures2.zip` to `content\textures\`,
   `content-textures3.zip` to `PlatformContent\pc\textures\`, and so on. Entries
   resolving outside the target directory are skipped. Downloads are sequential,
   extraction runs in parallel.

6. **Staging.** `AppSettings.xml` is written to the version root, the
   `Modifications` folder is copied over the tree, and your FastFlags are written
   to `ClientSettings\ClientAppSettings.json`.

7. **State.** `State.json` records the installed version GUID, version string and
   channel. On the next launch, if the executable for that GUID already exists,
   steps 3-5 are skipped and only staging re-runs.

Extraction uses `System.IO.Compression` rather than SharpZipLib, so the project
has **zero NuGet dependencies** — the whole thing is framework-only.

## Window behaviour

**Always on top** sets `Topmost`. **Lock position** does two things: it sets
`WindowChrome.CaptionHeight` to 0 so the title bar no longer drags, and it
intercepts `WM_WINDOWPOSCHANGING` to force `SWP_NOMOVE` on every incoming move
request. That second part is what makes it resist external repositioning — a
window manager, automation script or anything else calling `SetWindowPos` gets
its move silently dropped. Resizing is unaffected, and the lock is bypassed while
maximized so maximize/restore still work.

If you need to move the window with the lock on, turn the lock off in Settings.
There's deliberately no hidden bypass gesture.

## Theme

The palette matches WPF-UI's dark theme, which is what Bloxstrap renders with:

| Token | Value |
| --- | --- |
| `ApplicationBackground` | `#FF202020` |
| `CardBackground` | `#0DFFFFFF` |
| `CardStroke` | `#19000000` |
| `ControlFill` | `#0FFFFFFF` |
| `ControlStroke` | `#12FFFFFF` |
| `TextPrimary` / `Secondary` / `Tertiary` | `#FFFFFF` / `#C5FFFFFF` / `#87FFFFFF` |
| Corner radius | 4 |
| Card padding | `14,16,14,16` |

Turning off acrylic swaps `Root.Background` to `ApplicationBackground`. The
acrylic tint is `#202020` at `0x99` alpha, so both states sit in the same colour
family.

`SystemAccent` reads `AccentColorMenu` from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent` and lightens it
40% for dark backgrounds, roughly what WPF-UI does to derive
`SystemAccentColorPrimary`. Falls back to `#4CC2FF`. It drives the nav selection
pill and the toggle switches, and is a `DynamicResource` so it can be swapped at
runtime later.

## Scrolling

`PageScrollViewer` replaces the stock template. The scroll bar overlays the
content rather than reserving a gutter, has no arrow buttons, and its thumb is a
3px rounded rail that widens to 6px on hover or drag. Horizontal scrolling is
disabled — pages wrap instead.

## Secure UI

The Settings toggle calls `SetWindowDisplayAffinity`. On build 19041 and above it
uses `WDA_EXCLUDEFROMCAPTURE` (0x11): the window renders normally on your
physical display but comes out as a black rectangle in anything that captures the
desktop. On older builds it falls back to `WDA_MONITOR` (0x01), which achieves the
same blackout but also blocks Magnifier and other accessibility tools. The status
bar reports which one you got.

What it does not do:

- It doesn't stop a phone pointed at your screen.
- It doesn't survive UI Automation or a screen reader. The window's text is still
  in the accessibility tree; only the pixels are protected.
- Some hardware-accelerated capture paths and virtual-camera drivers ignore it.
  Treat it as friction against casual capture, not a security boundary.
- It's per-window. Any new window needs its own call.

## Other things worth knowing

**Maximize.** `WM_GETMINMAXINFO` is handled to clamp the maximized window to the
monitor's work area. Without this a `WindowStyle="None"` window overhangs the
screen edges and covers the taskbar.

**Windows 10 drag lag.** `ACCENT_ENABLE_ACRYLICBLURBEHIND` on Windows 10 makes
dragging and resizing stutter — the blur region redraws out of step with the
frame. It's a DWM behaviour. To fall back to `ACCENT_ENABLE_BLURBEHIND` (state 3)
on Windows 10, pass `WindowEffects.IsWindows11` instead of `true` as the last
argument to `ApplyBlur` in `MainWindow.ApplyBackground`.

**Corner radius on Windows 10.** `SetWindowRgn` clips with hard, unantialiased
edges; there's no way around that on Windows 10. `CornerRadiusDip` is 8. The
region is recomputed on resize, on DPI change, and cleared when maximized.

**Transparency mode.** `AllowsTransparency` is deliberately `False` — `True`
disables hardware acceleration and kills the DWM blur. The window is kept
see-through by `source.CompositionTarget.BackgroundColor = Colors.Transparent`.

**Icons.** Lucide v1.38.0 geometry in `Theme/Icons.xaml`, stroked at 2px on a
24x24 canvas inside a `Viewbox`. To add one, take the `d` attribute from
`https://unpkg.com/lucide-static@latest/icons/NAME.svg`, concatenate multi-path
icons into a single `Figures` string, and convert any `<rect>` or `<circle>` to
path commands — the WPF geometry parser has no equivalent of those elements.
Lucide is ISC-licensed; the notice belongs in your third-party attributions.

## Not done yet

- **No self-updater.** Bloxstrap ships one that checks its own GitHub releases
  and swaps the exe. That's a separate piece from the Roblox deployment pipeline
  above and isn't built yet.
- **No install/uninstall protocol.** No registry entries, Start Menu shortcut,
  `roblox-player://` protocol handler, or Add/Remove Programs presence. Bloxstrap
  registers itself as the player protocol handler so launching from the website
  goes through it — that's how it intercepts launches, and it's the next thing
  worth porting.
- **No logging.** `Paths.Logs` exists but nothing writes to it.
- **No Discord RPC** (v1 had it) and no font/cursor modification helpers beyond
  the raw Modifications folder copy.
- **FastFlags editor is raw JSON.** No presets, no per-flag UI, no validation
  beyond "is this parseable".
