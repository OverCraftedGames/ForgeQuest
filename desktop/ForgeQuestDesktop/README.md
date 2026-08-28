# ForgeQuest Desktop (WebView2 wrapper)

A thin Windows desktop shell around the same `www/index.html` used by the
Android build. No Electron/Node involved — just a WinForms window hosting a
`Microsoft.Web.WebView2` control, pointed at a tiny local HTTP server the app
starts itself.

## Why a local server instead of just opening the file

Loading `index.html` directly as `file://` gives Chromium-based browsers
(WebView2 included) flaky, sometimes-partitioned `localStorage` behavior.
Serving it over `http://127.0.0.1:<port>/` instead gives it a normal, stable
origin, so the game's own save/load system (see `SESSION_NOTES.md` at the repo
root) works exactly like it does in a real browser.

**The port is fixed (`47811`, see `MainForm.cs`), not randomly chosen.**
`localStorage` is scoped per-origin *including the port* — if the port changed
every launch, every launch would look like a brand-new empty origin and saves
would silently appear to vanish, even though the browser profile itself
persisted fine. (This actually happened during development — see
`SESSION_NOTES.md`.) If port 47811 happens to be taken by something else, the
app tries a few ports after it and falls back gracefully, but persistence is
only guaranteed when it lands on the same port as last time.

## Where saves/profile data live

`%LOCALAPPDATA%\ForgeQuest\WebView2\` — a per-user WebView2 profile folder,
explicitly pointed there in code so it works even if the exe is launched from
a read-only location (Program Files, a fresh zip extract, etc.). Delete that
folder for a completely clean slate (equivalent to uninstalling + reinstalling
as far as save data goes).

## The canonical game file is pulled in automatically

`ForgeQuestDesktop.csproj` has:
```xml
<None Include="..\..\www\index.html" Link="www\index.html" CopyToOutputDirectory="PreserveNewest" />
```
This always copies the *live* `forgequest-app/www/index.html` into the output
folder at build/publish time — unlike the Android assets copy, there's no
manual sync step to remember here. Edit the canonical file, rebuild, done.

## Building & running while developing

```bash
dotnet build -c Debug
```
Then run `bin\Debug\net10.0-windows\ForgeQuest.exe` directly, or `dotnet run`.
Requires the .NET SDK (already on this machine) and the Edge WebView2 Runtime
(ships with Windows 11 / recent Windows 10 alongside Edge; if it's ever
missing the app shows a message pointing at the Evergreen Bootstrapper
instead of crashing).

## Producing a standalone .exe to hand someone else

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
Produces `publish\ForgeQuest.exe` (~110MB — it bundles the entire .NET
runtime, so the target machine needs nothing installed except the WebView2
Runtime) plus a `publish\www\index.html` sibling folder that **must ship
alongside the exe** — copy the whole `publish\` folder, not just the exe.

If ~110MB is annoying and you know the target machine already has the
matching .NET runtime installed, drop `--self-contained true` and the two
`Publish...` flags for a much smaller framework-dependent build instead.

## App icon

`Resources/app.ico` is a hand-assembled multi-size icon (16/32/48/64/128/256px,
PNG-compressed frames) cropped from the anvil+sword+spark emblem in the
project root's `placeholder_header_logo.png` (the "FORGEQUEST" text banner
underneath it was cropped out — doesn't read at icon sizes). `app_icon_master.png`
next to it is the 345×345 square source if it ever needs re-cropping or
re-exporting at different sizes.

It's wired in two places, both needed:
- `<ApplicationIcon>` in the `.csproj` — sets the icon Explorer shows on the
  `.exe` file itself.
- `this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)` in
  `MainForm`'s constructor — `<ApplicationIcon>` alone does **not** make
  WinForms use it for the actual running window/taskbar/alt-tab.

To swap in a real (non-placeholder) icon later: replace `app.ico` with a new
multi-size ICO (or re-run the same crop/resize/assemble approach against a new
source image) and rebuild — no code changes needed.

## Known-harmless build warning

`MSB3277 ... conflicts between different versions of "WindowsBase"` — the
WebView2 NuGet package ships both a WinForms and a WPF flavor and MSBuild
notices the WPF one's WindowsBase reference doesn't match; we only use the
WinForms flavor (`Microsoft.Web.WebView2.WinForms`) so this doesn't affect
anything at runtime.
