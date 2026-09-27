# Changelog

All notable changes are versioned with [SemVer](https://semver.org/): `MAJOR.MINOR.PATCH`.

Git tags are `vMAJOR.MINOR.PATCH` (example: `v0.1.0`).

## [0.2.2] — 2026-09-27

### Fixed
- Launch always force-kills other `GrokUsageWidget` processes, then starts this copy. No more silent exit when a hidden/stuck instance owns the mutex.
- Writes `%USERPROFILE%\.grok\usage-widget.log` on start, kill, and crash.

## [0.2.1] — 2026-09-27

### Fixed
- The download is the app, not an installer. A second launch used to exit silently because of the single-instance mutex.
- Second launch now **Reveals** the running card.
- If the file you double-clicked is a *different copy* (Downloads vs the installed exe), you get Yes = replace / No = find the old one.

## [0.2.0] — 2026-09-27

### Added
- Tray **Reveal**: show the card, center it on the primary monitor, bring it to front. Does not overwrite the saved dock position.

### Changed
- Menu label is **Open Settings → Usage**. Grok has no public `/usage` URL; this still opens https://grok.com/ (sign in → profile → Settings → Usage).

## [0.1.0] — 2026-09-24

### Added
- Always-on-top weekly SuperGrok usage meter
- Tray icon (Grok favicon), drag-to-position, hide/show
- Persist position **and monitor** (`ScreenDevice` + offsets in `%USERPROFILE%\.grok\usage-widget.json`)
- Start with Windows (HKCU Run)
- Silent OIDC refresh of `auth.json` before billing calls
- Poll `GET https://cli-chat-proxy.grok.com/v1/billing?format=credits`
