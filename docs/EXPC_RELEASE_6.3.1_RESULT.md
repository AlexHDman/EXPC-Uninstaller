# EXPC 6.3.1 — publication result

STATUS: stable release published; latest confirmed without a personal token. Stage: active user testing / bug collection. Functional development paused.

- Release: https://github.com/AlexHDman/EXPC-Uninstaller/releases/tag/v6.3.1
- Direct EXE: https://github.com/AlexHDman/EXPC-Uninstaller/releases/download/v6.3.1/EXPCdelete.exe
- Producer commit/tag: `5dbb9ea1923183c7ccc00aa6f6f8982df0c49938` / `v6.3.1` (tag unchanged).
- Local EXE: `C:\AI\EXPC-Uninstaller\dist\EXPCdelete.exe`
- Size: 9,364,480 bytes (8.931 MiB).
- SHA256: `c4b9d739cf447421020341e8acfc001cd05c814979a1dd1af92170619eb647e4`
- Exact clean producer checkout: build/release-source; source build evidence artifacts/verification/20261026-233646 inside it. Copied release evidence: artifacts/verification/release-6.3.1.

## Actual checks
C#/C++ + helper compilation, native runtime-policy assertions, 103/103 selected regression tests, PE/payload/file inventory checks. Gate verifies matching FileVersion/ProductVersion 6.3.1 in launcher, app and helper. No local-data, vault, logs, tests or private-key payload markers in the distribution.

Console fixtures: five sequential and six parallel repeat launches, eight concurrent cold starts at both layers, cross-layer owner detection, exit/restart and crash recovery. Actual file swap/rollback, locked-target preservation and helper restart/handshake performed only on disposable fixtures. No real program installation/removal/update and no EXPC GUI launch.

Draft assets EXPCdelete.exe, EXPCdelete.update.json and SHA256SUMS.txt downloaded and byte/hash compared before publication. After publication, unauthenticated API confirms stable latest v6.3.1; all assets downloaded again and matched. Production SelfUpdateClient in a console harness reads the public release/manifest: current 6.3.1 equal, 6.3.0 older, 6.3.2 newer/no downgrade. This is not a real GUI replacement test.

Existing user-modified Visual Master SHA remains unchanged. Personal local files, old archives and audit materials were not committed. Source restructuring is retained; original BCU attribution/licenses retained. No upstream push, merge, force push, desktop control or automatic final EXE start.

## NOT TESTED / limitations
- Physical close/tray removal, foreground/minimized window restoration, DPI, different UAC integrity levels and separate user sessions.
- Real GUI update from the published EXE, UAC/runtime installation and crash during a real external runtime installer.
- Real Authenticode-signed update, removable-volume disconnect, actual access-denied target and OS shutdown during a destructive operation.
- Existing PDF-XChange/leftover and third-party update field defects remain open; no removal/cleanup algorithm changes.
- EXE is unsigned; .NET Desktop 10 x64 >=10.0.12 stable required. A same-release SHA is integrity evidence, not independent publisher authentication.

Old EXEs without self-update require one manual replacement and closing old instances. Future updates: Help / Справка → Update application / Обновить программу.

NEXT: user manually opens EXPCdelete.exe and accepts lifecycle/UI behavior; no automatic GUI launch. Source tag remains frozen. Any subsequent commit for this report/checkpoint changes documentation only.
