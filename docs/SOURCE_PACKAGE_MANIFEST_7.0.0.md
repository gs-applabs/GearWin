# GearWin 7.0.0 — Source Package Manifest

The GitHub Actions workflow `installer-e2e` creates reproducible deliverables from one checked-out commit:

| Artifact | Contents |
| --- | --- |
| `setup` | `GearWin-Setup.exe` and SHA-256 |
| `gearwin-source-7.0.0` | Full `GearWin-7.0.0-source.zip`, `GearWin-7.0.0-markdown.zip`, `PACKAGE_PROVENANCE.txt`, `SOURCE_SHA256SUMS.txt` |

Source archive uses `git archive HEAD`, including *only tracked* repository contents (not `.git`, local `bin/`, `obj/`, pre-existing installer outputs or user data). The Markdown package includes every tracked `.md`/`.MD` file in its original relative path and is deliberately redundant with the full source.

Installers and source archives must be obtained from the **same successful GitHub Actions run**, because the branch can move. SHA-256 should be checked after downloading.

Start Claude Code with `docs/CLAUDE_CODE_HANDOFF_7.0.0.md`; follow `CLAUDE.md` only where consistent with the current tree. The 7.0.0 installer is a *candidate*; real disk clone/recovery boot and SSD physical writes remain unvalidated.
