# GearWin Migration

This prototype inventories installed desktop apps and produces a package installation preview for Windows and Linux. The .NET 11 core can be referenced from both the Windows app and a future Linux UI.

Run from the repository root with the .NET 11 SDK:

```sh
dotnet run --project migration/GearWin.Migration.Cli -- inspect > target.json
dotnet run --project migration/GearWin.Migration.Cli -- inventory > inventory.json
dotnet run --project migration/GearWin.Migration.Cli -- plan migration/example-manifest.json --target target.json --inventory inventory.json
```

Run `inspect` on the destination and `inventory` on the source. Copy the JSON files to the planning PC if needed. Omit `--target` to plan against the current machine, or omit `--inventory` to preview every application in the manifest. Inventory files contain the application names on the source PC. Keep both exports outside source control.

Run a local metadata check on the destination with:

```sh
dotnet run --project migration/GearWin.Migration.Cli -- plan migration/example-manifest.json --inventory inventory.json --verify
```

`--verify` uses local package indexes for APT, Pacman, DNF/DNF5, Zypper, APK and XBPS. It reports an exact match with its version and architecture, an unconfirmed result, or an unsupported verifier. Flatpak checks installed applications and the selected origin, reported separately as `installedOnDestination`. Local indexes can be stale. Flatpak remote availability requires a separate check. Verification uses the existing local package data without downloading packages or executing installation previews.

## Windows graphical planning

In System & Tools → Tools below System Image Backup, export the installed-app inventory and the computer profile as JSON. Choose Create application plan, select the mapping manifest, source inventory and destination profile, then save the report. These use the same strict document contract and planner as the CLI. The report preserves unresolved/manual entries and inventory warnings; a bounded on-screen preview shows the first 200 entries and the complete report is saved as JSON. No command is executed. Windows GUI inventory supplements uninstall registrations with the shared current-user non-framework, non-resource packaged-app scan (Store and non-Store family identity and version). This broader read-only scan is separate from removable Store app eligibility for the UWP manager. Other users, provisioning, optional package types and automatic portable discovery remain incomplete. The CLI registry inventory alone does not include this supplement. For Linux, export the profile/inventory with the CLI. Local package verification remains a destination CLI operation; WinGet verification is not implemented.

### Destination inventory comparison and selected executables

Select "Compare with destination inventory" in the Windows planning card or pass `--destination-inventory destination.json` to the CLI. This compares explicit catalog identities against an imported destination inventory of the same OS, preserving multiple matches and uncertainty. It does not prove installation, exact package availability or compatibility; `--verify` remains a separate local package metadata check.

"Export with selected EXEs" adds up to 100 user-selected executable names as advisory entries. Full paths and file contents are not exported or executed. These entries always require manual review, even if a catalog identity matches; no automatic portable discovery or authenticity claim is made.

## Scope

- `inspect` reads local OS and executable metadata. Windows can detect WinGet. Linux can detect APT, DNF/DNF5, Pacman, Zypper, APK, XBPS, Portage, Eopkg, Urpmi, Slackpkg, Flatpak, Snap, rpm-ostree and transactional-update. PackageKit is a fallback when `pkcon` is present and there is no selected native manager. NixOS and Guix use manual configuration plans. Unknown variants use detected capabilities. Each preview still needs repository and installation validation on the destination.
- `inventory` reads Windows uninstall registrations or Linux desktop entries, including known Flatpak, Snap, Nix and Guix export directories. It captures app identities and available version and publisher metadata. Store/MSIX apps without uninstall registrations, command-line packages without desktop entries, and standalone apps without registrations require other discovery methods.
- `plan` accepts schema version 1. `sources` contains explicit source identities by `kind` (`linuxDesktop`, `windowsRegistry`, `windowsStorePackage` or `windowsPackagedApp`), app `id` or `name`, and optional `publisher`. An unrecognized or ambiguous source stays in the report as `unresolved`. The first candidate supported by a detected backend and matching the destination is used. A candidate may specify a `target` with exact `distributionId`, `version`, `variantId` or `architecture` requirements.
- A `commandPreview` is a proposed action for review. APT identifiers with trailing action suffixes are reported for manual review. JSON exports are limited to 16 MiB and desktop entries to 64 KiB. File, disk, settings, game and driver migration belong to later stages.

Run the focused tests with:

```sh
dotnet test migration/GearWin.Migration.Tests
```
