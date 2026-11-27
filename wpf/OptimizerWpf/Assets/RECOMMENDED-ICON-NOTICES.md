# Recommended application artwork

The 29 existing recommendations have one explicitly selected product icon each. Opera GX and Vivaldi are included for the existing browser-cleanup cards. The catalog is `recommended-icons.json`. These product marks are not GearWin artwork: all product names, icons and trademarks belong to their respective owners; use is solely to identify software, not imply affiliation, sponsorship or endorsement. Review each source's applicable terms before public distribution.

Sources:
- memstechtips/package-icons, immutable commit afa5f634bf291edbc4c1fc678b70070945043ccf, manifest.json and THIRD-PARTY-NOTICES. 27 entries including the two browser-only icons use the exact manifest SHA-256 and explicitly selected identity aliases.
- Winamp, Viber, .NET Desktop Runtime, VC++: pinned Chocolatey package artwork repositories listed in the JSON. These four now have SHA-256 pins taken from the successful Windows CI downloads on 2026-11-27 (run 45991483809). These are observed hashes, not independently published vendor hashes. All 31 catalog entries are pinned.
- The artwork itself remains the respective owners' intellectual property, not covered by GearWin's MIT license.

Build from the repository root on Windows: `pwsh -File tools/prepare-recommended-icons.ps1`. Build CI checks each image's format/dimensions, pinned URL and published hash where available, normalizes to a 96px square and emits a contact sheet, per-asset provenance and the normalized PNG pack. Those prepared assets are embedded in GearWin.dll; the running app no longer requests publisher favicons. For offline source builds, place the reviewed normalized PNGs in `wpf/OptimizerWpf/Assets/recommended-icons/` before invoking dotnet build. Without the pack a developer build uses a neutral placeholder; release CI requires all recommendations to decode. Actual visual review and distribution-rights review remain release gates.
