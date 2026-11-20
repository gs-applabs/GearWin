# GearWin Migration

This prototype produces a package installation preview for Windows and Linux. It can plan on the destination or on another PC using an exported destination profile. The .NET 10 core can be referenced from both the Windows app and a future Linux UI.

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project migration/GearWin.Migration.Cli -- inspect > target.json
dotnet run --project migration/GearWin.Migration.Cli -- plan migration/example-manifest.json --target target.json
```

Run `inspect` on the destination. Copy `target.json` to the planning PC if needed. Omit `--target` to plan against the current machine. The profile includes OS and architecture metadata and the detected package backends. Keep `target.json` outside source control.

## Scope

- `inspect` reads local OS and executable metadata. Windows can detect WinGet. Linux can detect APT, DNF/DNF5, Pacman, Zypper, APK, XBPS, Portage, rpm-ostree, Flatpak and NixOS. Unknown distributions can use a single unambiguous native backend or Flatpak. Atomic OSTree and NixOS systems have separate routing rules.
- `plan` accepts schema version 1. Each application has an ordered `candidates` list. The first candidate supported by a detected backend and matching the destination is used. Each candidate may specify a `target` object with exact `distributionId`, `version`, `variantId`, or `architecture` requirements. Supply package IDs appropriate to the target's repositories. Flathub and WinGet are the default sources for their respective backends. NixOS entries are reported for declarative review.
- A `commandPreview` is a proposed command to review. Package availability, repository setup and application compatibility still need confirmation. An `unresolved` entry needs a matching package candidate or backend. The prototype does not run these commands, touch disks, export installed applications, or restore files, settings, game saves or drivers.

Run the focused tests with:

```sh
dotnet test migration/GearWin.Migration.Tests
```
