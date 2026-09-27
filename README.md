# Grok Usage Widget

[![Release](https://img.shields.io/github/v/release/SplenectomY/GrokUsageWidget)](https://github.com/SplenectomY/GrokUsageWidget/releases)

Always-on-top Windows meter for the SuperGrok **weekly** usage pool. Current version: **0.2.3**.

Versions follow [SemVer](https://semver.org/). Git tags are `vMAJOR.MINOR.PATCH`.

---

## New to Grok?

Grok is xAI's assistant. You talk to it on [grok.com](https://grok.com), in the Grok / X apps, and — if you install it — from a terminal app called **Grok Build** (`grok`).

Paid plans (SuperGrok, SuperGrok Plus, and similar) share **one weekly usage pool** across products:

| Product | What it is |
|---|---|
| **Chat** | grok.com, the apps, long threads |
| **Imagine** | image / video generation |
| **Voice** | spoken conversations |
| **Build** | the `grok` CLI / agent that can edit files, run commands, talk to Unity, Blender, MCP servers, etc. |
| **API** | `api.x.ai` keys, if you use them |

When that pool hits 100%, paid features pause until the weekly reset (shown under **Settings → Usage** on grok.com). Extra credits and plan upgrades are also on that page.

**This widget is not Grok.** It does not chat, it does not drive Unity, and it is not an MCP server. It is a tiny always-on-top card that reads the same billing number Grok Build uses and shows “how much of this week is gone?” so you do not find out mid-task.

You still need a Grok account. The widget only displays usage for the account that last ran `grok login` on this Windows user.

---

## Prerequisites

| Need | Why |
|---|---|
| Windows 10 or 11, 64-bit | This is a WinForms desktop app. No macOS / Linux build. |
| An xAI / Grok account | Sign up at [grok.com](https://grok.com). |
| A **paid** SuperGrok (or higher) plan | The weekly pool the widget graphs is a paid-plan thing. Free Chat still works; this meter will not be very interesting on free. |
| [Grok Build / Grok CLI](https://x.ai/docs/build/cli/reference) installed | The widget does not log you in itself. It reads `%USERPROFILE%\.grok\auth.json`, which `grok login` writes. |
| A normal (non-Administrator) PowerShell or terminal | Elevated prompts write credentials to the wrong profile. |
| Internet | It polls `https://cli-chat-proxy.grok.com/v1/billing?format=credits`. |

You do **not** need:

- Visual Studio or the .NET SDK, if you use the **v0.2.3+** release exe (self-contained)
- Unity, Blender, or any MCP server — those are optional *consumers* of the same pool
- To leave this exe in Downloads

---

## First run (step by step)

### 1. Get a Grok account and a plan

1. Open [https://grok.com](https://grok.com) and sign in (X account or email, whatever xAI offers you).
2. Confirm the plan under profile → **Settings**. You want a paid SuperGrok tier if you care about this meter.
3. Open **Settings → Usage** once so you know what “full” looks like in the official UI. That is the same number the widget will show.

### 2. Install Grok Build and sign in on this PC

Install the official CLI using whatever xAI documents for Windows (PATH should contain `grok`). Then, in a **normal** PowerShell — not “Run as administrator”, not `C:\Windows\System32`:

```powershell
grok login
```

Finish the browser / device flow. You should now have:

```
%USERPROFILE%\.grok\auth.json
```

Sanity check (should print a session, not an error):

```powershell
grok login
# or just start: grok
```

If `auth.json` is missing, the widget can only say **login**.

### 3. Install the widget

1. Download **v0.2.3 or newer** from [Releases](https://github.com/SplenectomY/GrokUsageWidget/releases).
   - Prefer `GrokUsageWidget.exe` from 0.2.3+ (self-contained, one file).
   - Older `0.2.2` and below shipped a stub that dies unless `GrokUsageWidget.dll` sits next to it. Do not use those as a lone Downloads file.
2. Move the exe somewhere permanent, for example:

   ```
   C:\Dev\GrokUsageWidget\GrokUsageWidget.exe
   ```

   Leave it in Downloads and Windows will nag every time (Mark of the Web) and “Start with Windows” will point at a folder you empty.
3. Unblock the internet flag if SmartScreen complains:

   ```powershell
   Unblock-File C:\Dev\GrokUsageWidget\GrokUsageWidget.exe
   ```

4. Double-click the exe. **Run anyway** on SmartScreen is expected for an unsigned download.
5. You should get a small always-on-top card and a Grok icon in the tray.

If the card never appears:

```powershell
Stop-Process -Name GrokUsageWidget -Force -ErrorAction SilentlyContinue
Get-Content $env:USERPROFILE\.grok\usage-widget.log -Tail 40
```

Then run the exe again. From 0.2.2 onward it force-kills a stuck previous copy first.

### 4. First successful read

A good first paint looks like:

- Big percentage (weekly pool used)
- A short status line (plan / time until reset when the API sends it)
- A green / yellow / red bar

If it says **login**:

1. `grok login` in a normal user shell
2. Tray → **Refresh now**

The widget never prints your token. It only reads `auth.json` and will refresh an expired access token from the stored refresh token.

### 5. Park it

- Drag the card to a corner. Position **and which monitor** are saved in `%USERPROFILE%\.grok\usage-widget.json`.
- Tray → **Start with Windows** (HKCU Run, this Windows user only). Do this from the parked exe, not from a `dotnet run` build.
- Lost the card? Tray → **Reveal** (centers on the primary monitor without forgetting your saved corner).
- Hide / Exit live on the same menu. There is no installer and no Programs and Features entry.

Default poll is 60 seconds. Hitting Refresh or letting it poll does **not** burn your Grok weekly chat allowance in any meaningful way; it is a small billing JSON fetch.

---

## What this is good for (and what it is not)

The meter is useful whenever you are spending that **shared weekly pool** and do not want to alt-tab to Settings → Usage.

| Scenario | Does the widget talk to it? | Why you might still want the card |
|---|---|---|
| **Browser Chat** on grok.com | No. Chat spends the pool; the widget only *reads* the total. | Long research threads + Imagine in another tab. Glance at the card before you start a huge job. |
| **Grok Build / CLI** (`grok` in a project folder) | No direct link. Same `auth.json`, same pool. | Build turns are expensive. Two CLI sessions plus Imagine can empty a week in a day. |
| **Unity CLI + Grok** (`unity` pipeline + `grok` in a Unity repo) | No. Unity is a consumer via Build, not a plugin of this widget. | Play/eval loops, screenshots, and ffmpeg passes chew context. Keep the card on a second monitor while the Editor is focused. |
| **MCP servers** (Blender MCP, filesystem, GitHub, …) | No. MCP is configured inside Grok Build (`grok mcp`), not here. | MCP tool calls still bill as Build. The card tells you the server is not “free.” |
| **Voice / Imagine** | No. | Same pool. Easy to forget voice minutes while a CLI agent is also running. |
| **xAI API keys** | No. API spend can show up in the official Usage breakdown; this card is the weekly total. | Useful if you mix app Chat and API in one week. |

Hypothetical days it earns its keep:

1. You leave `grok` running overnight on a Unity localization pass and want to see in the morning whether the week is already toast.
2. You pair Imagine batches with a Build session and keep blowing past 80% without noticing.
3. You run **two** Grok Build windows on two Unity projects and have no idea which one is winning the pool.
4. You only use grok.com in the browser — no CLI agent — but still want a desktop reminder before a long voice call.

It is **not** a replacement for Settings → Usage (no per-product pie chart here), not a Unity package, and not something you add to an MCP config.

---

## Download

Get the build from [Releases](https://github.com/SplenectomY/GrokUsageWidget/releases):

- `GrokUsageWidget.exe` — self-contained win-x64 (no extra .NET install, no sidecar DLL) on **v0.2.3+**
- `GrokUsageWidget-vX.Y.Z-win-x64.zip` — same exe + `grok.ico`

Do not grab only the tiny stub from older releases. `0.2.2` and below needed `GrokUsageWidget.dll` next to the exe; Downloads had the stub alone, so it exited with no window. Dropping that stub into a local `bin\Release` folder “fixed” it because the DLL was already there.

SmartScreen will still warn on a first run from the internet (`Zone.Identifier`). Run anyway, or `Unblock-File` as in step 3 above.

## What it does (technical)

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
