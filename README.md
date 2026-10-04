# Grok Usage Widget

[![Release](https://img.shields.io/github/v/release/SplenectomY/GrokUsageWidget)](https://github.com/SplenectomY/GrokUsageWidget/releases)

Always-on-top Windows meter for the SuperGrok **weekly** usage pool. Current version: **0.2.5**.

Versions follow [SemVer](https://semver.org/). Git tags are `vMAJOR.MINOR.PATCH`.

New to Grok, first-time setup, or wondering whether this helps with Unity / MCP / the browser? See **[Getting started](docs/Getting-Started.md)**.

## Download

Get the build from [Releases](https://github.com/SplenectomY/GrokUsageWidget/releases):

- `GrokUsageWidget.exe` — self-contained win-x64 (no extra .NET install, no sidecar DLL) on **v0.2.3+**
- `GrokUsageWidget-vX.Y.Z-win-x64.zip` — same exe + `grok.ico`

Do not grab only the tiny stub from older releases. `0.2.2` and below needed `GrokUsageWidget.dll` next to the exe; Downloads had the stub alone, so it exited with no window. Dropping that stub into a local `bin\Release` folder “fixed” it because the DLL was already there.

SmartScreen will still warn on a first run from the internet (`Zone.Identifier`). Run anyway, or:

```powershell
Unblock-File C:\Dev\GrokUsageWidget\GrokUsageWidget.exe
```

## What it does

Calls the same billing route Grok Build uses:

`GET https://cli-chat-proxy.grok.com/v1/billing?format=credits`

Auth is your existing `grok login` session in `%USERPROFILE%\.grok\auth.json`. The widget never prints or copies the token. It will refresh an expired access token from the stored refresh token.

This endpoint is what the official CLI uses. It is not a documented public API and can change.

## Run from source

Needs the .NET 8 SDK (Visual Studio + “.NET desktop development”).

```powershell
git clone https://github.com/SplenectomY/GrokUsageWidget.git
cd GrokUsageWidget
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\GrokUsageWidget.exe
```

## Use

- Drag the card; position **and monitor** are saved to `%USERPROFILE%\.grok\usage-widget.json`.
- Double-click the tray icon to show/hide.
- Right-click tray: **Reveal**, Refresh, Open Settings → Usage, **Start with Windows**, Hide, Exit.
- **Reveal** shows a missing card and centers it on the primary monitor (saved dock position is left alone until you drag again).
- There is no installer. `GrokUsageWidget.exe` *is* the widget. Launching a second copy force-kills the old process (v0.2.2+) and starts this file.
- Start with Windows uses HKCU Run (this Windows user only). Enable it from the parked exe, not `dotnet run`.
- Default poll: 60 seconds.
- Crashes and launches append `%USERPROFILE%\.grok\usage-widget.log`.

If the bar says **login**, run `grok login` in a normal user PowerShell, then Refresh.

## Versioning

- Patch `0.1.x` / `0.2.x` — fixes (token refresh, DPI, icon, launch).
- Minor `0.x.0` — features that stay compatible.
- Major `x.0.0` — breaking changes.

Tag `v0.2.3` (or bump `Version` in the csproj and tag `v0.2.4`) to cut a release. `.github/workflows/release.yml` builds on `windows-latest` and uploads the exe.
