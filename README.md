# Grok Usage Widget

[![Release](https://img.shields.io/github/v/release/SplenectomY/GrokUsageWidget)](https://github.com/SplenectomY/GrokUsageWidget/releases)

Always-on-top Windows meter for the SuperGrok **weekly** pool. Current version: **0.1.0**.

Versions follow [SemVer](https://semver.org/). Git tags are `vMAJOR.MINOR.PATCH`.

## Download

Get the signed-off build from [Releases](https://github.com/SplenectomY/GrokUsageWidget/releases):

- `GrokUsageWidget.exe` — needs the [.NET 8 desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- `GrokUsageWidget-vX.Y.Z-win-x64.zip` — exe + `grok.ico`

## What it does

Calls the same billing route Grok Build uses:

`GET https://cli-chat-proxy.grok.com/v1/billing?format=credits`

Auth is your existing `grok login` session in `%USERPROFILE%\.grok\auth.json`. The widget never prints or copies the token. It will refresh an expired access token from the stored refresh token.

## Run from source

Needs the .NET 8 SDK (Visual Studio + “.NET desktop development”).

```powershell
git clone https://github.com/SplenectomY/GrokUsageWidget.git
cd GrokUsageWidget
dotnet publish -c Release -r win-x64 --self-contained false -o publish
.\publish\GrokUsageWidget.exe
```

## Use

- Drag the card; position **and monitor** are saved to `%USERPROFILE%\.grok\usage-widget.json`.
- Double-click the tray icon to show/hide.
- Right-click tray: Refresh, Open Usage page, **Start with Windows**, Hide, Exit.
- Start with Windows uses HKCU Run (this Windows user only). Enable it from the published exe, not `dotnet run`.
- Default poll: 60 seconds.

If the bar says **login**, run `grok login` in a normal user PowerShell, then Refresh.

## Versioning

- Patch `0.1.x` — fixes (token refresh, DPI, icon).
- Minor `0.x.0` — features that stay compatible.
- Major `x.0.0` — breaking changes.

Tag `v0.1.0` (or bump `Version` in the csproj and tag `v0.1.1`) to cut a release. `.github/workflows/release.yml` builds on `windows-latest` and uploads the exe.

This billing endpoint is what the official CLI uses. It is not a documented public API and can change.
