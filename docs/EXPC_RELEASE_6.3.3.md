# EXPC Uninstaller 6.3.3 — publication result

STATUS: published stable / latest on 2026-10-28.

- Release: https://github.com/AlexHDman/EXPC-Uninstaller/releases/tag/v6.3.3
- Producer commit: `754774f4e3c3b92fb7774fa8ccef026f0d753423`; tag `v6.3.3`.
- Branch: `codex/release-6.3.3`; origin AlexHDman/EXPC-Uninstaller. No upstream writes, merge or force push.
- Artifact: `dist/EXPCdelete.exe`, 9359872 bytes.
- SHA256: `96b9d9f9f8ca01dbf58506ef69b22144edafe1e4d64ac43c5c169ef7baa1077e`.
- Clean producer: `build/release-source`, verification `20261028-185354`.
- Final receipts: `artifacts/verification/release-6.3.3`.

## Changes

All application data uses `%LOCALAPPDATA%/EXPC/Uninstaller`: Data for settings/vault/operation evidence, Cache for extraction/update staging, Logs for optional diagnostics. Startup metrics are off unless EXPC_DIAGNOSTICS=1. No writes beside the portable EXE. Existing local-data and vault beside the launched EXE are copied without overwriting destination or deleting originals; no global search for old data is performed.

Tray residency removed. X, Alt+F4 and Exit close the app; active operations retain the short warning and external installers are not terminated. Self-update replacement staging/rollback stays in user cache. Automatic replacement across volumes is rejected safely; manual replacement is required in that case.

## Verified

- Exact-commit C#/C++ build, native policy, version agreement, payload integrity and 116/116 safe tests passed.
- Nonshown STA close test: 1/1; console fixtures covered sequential/concurrent single-instance starts, crash/restart and replacement/rollback/handshake. These are not physical GUI checks.
- Commit and annotated tag pushed; tag identifies the exact clean EXE producer.
- Draft assets EXE, update manifest and SHA256 downloaded back and matched before publication.
- Stable latest verified via public GitHub API without token; all three published assets downloaded and matched sizes/SHA256.
- Production SelfUpdateClient console fixture reads 6.3.3: current equals release, older requires update, newer is not downgraded.
- User assets and unrelated worktree files excluded from commit. Existing v6.3.1 retained.

## NOT TESTED / limitations

Physical GUI, tray/focus/DPI acceptance; real user vault migration; real GUI update with the published EXE. Only disposable console fixtures exercised replacement. Final EXE was not launched. No installed applications changed. Existing unclosed removal/cleanup defects are not claimed fixed. Portable requires stable .NET Desktop Runtime 10 x64 (10.0.12 or later); EXE is unsigned.

Older EXEs without self-update need one manual replacement. Thereafter use Help → Update application. Exit any old running instance through File → Exit before opening this candidate.

NEXT: user acceptance. Active testing and bug collection; functional development paused.
