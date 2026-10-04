# Agent notes

End users do not need this file. Releases and version bumps follow these rules.

## Versioning

Versions follow [SemVer](https://semver.org/). Git tags are `vMAJOR.MINOR.PATCH`.

`Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` in `GrokUsageWidget.csproj` must match the tag (assembly versions use a fourth `.0`).

- Patch `0.2.x` — fixes (token refresh, DPI, icon, launch, billing display).
- Minor `0.x.0` — compatible features.
- Major `x.0.0` — breaking changes.

Do not cut a release for a refactor or docs-only change unless asked.

To release: bump the csproj versions and `CHANGELOG.md`, commit, tag `vX.Y.Z`, push `main` and the tag. `.github/workflows/release.yml` builds on `windows-latest` and uploads a self-contained single-file `win-x64` exe. Do not publish a framework-dependent stub.

Current release: **0.2.4**.
