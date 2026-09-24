# Changelog

All notable changes are versioned with [SemVer](https://semver.org/): `MAJOR.MINOR.PATCH`.

Git tags are `vMAJOR.MINOR.PATCH` (example: `v0.1.0`).

## [0.1.0] — 2026-09-24

### Added
- Always-on-top weekly SuperGrok usage meter
- Tray icon (Grok favicon), drag-to-position, hide/show
- Persist position **and monitor** (`ScreenDevice` + offsets in `%USERPROFILE%\.grok\usage-widget.json`)
- Start with Windows (HKCU Run)
- Silent OIDC refresh of `auth.json` before billing calls
- Poll `GET https://cli-chat-proxy.grok.com/v1/billing?format=credits`
