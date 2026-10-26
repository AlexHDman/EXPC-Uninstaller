# EXPC 6.3.1 — scoped release preparation

Stage: release preparation. Development active only for the approved portable self-update and lifecycle changes. No removal/leftover changes. Agent: Codex, Windows CLI/headless; exact model/effort unavailable.

## Implemented
- Existing source/tests/build/dist restructuring is retained. Version source: EXPC.Version.props; native VERSIONINFO, managed assembly and generated update manifest use 6.3.1. The previous EXPC assembly inherited BCU 6.3; the own GitHub repository returned no releases or tags before this release.
- Help → Update application, bold first item; RU/EN, current theme. Network check is user-triggered only. Stable releases and exact repository asset URLs, no downgrade, schema/runtime/architecture/version/size/SHA-256 checks, bounded HTTPS and redirects. API/offline errors remain errors. Embedded credentials are not used.
- Original portable origin is checked against the real parent launcher process. Download/request staging uses LocalAppData/EXPC/SelfUpdate; existing user data locations are unchanged. SHA-256 from the same release provides integrity, not an independent authenticity guarantee. Signed executables are checked with WinVerifyTrust; unsigned builds are not represented as signed.
- Separate console helper waits for exact old app and launcher PID/start-time, never kills either, stages replacement on the target volume, atomically replaces with a verified rollback copy. One new launcher starts; bounded event handshake confirms the new main form reached Shown. No handshake does not mean success. A still-running unacknowledged process is not killed; rollback is retained. On a recoverable failure the verified original can restart once with a result message, without requesting another update.
- Close, Alt+F4 and Exit share the real exit path, cancel background checks, lock vault and remove tray icon. An active destructive/install operation blocks closure with one warning, never terminates an external process.
- User SID + Windows Local namespace mutex/event protocol covers native launcher and managed application, independent of EXE/cache path. The launcher remains alive waiting for its exact child; simultaneous bootstrap/runtime windows are serialized. Activation request is queued until the main window is ready. Locks are released before update restart. Explicit per-user ACL/low integrity label supports same-user integrity transitions without granting other users access.

## Background evidence before final exact-commit build
- Selected .NET regression suite: 103/103; native dependency policy assertions; C#/C++ and updater build; PE/payload composition and hashes checked.
- Console fixtures: both layers, five sequential duplicate starts, six simultaneous duplicates, eight simultaneous cold starts, exit/restart and abandoned-lock recovery after terminating only the disposable fixture PID.
- Native fixture recognizes a directly started managed owner.
- Actual atomic replacement and rollback on EXE copies with spaces/Cyrillic paths; occupied target preserves original; real helper launches a harmless console fixture and receives the startup handshake, then removes its rollback file.
- Unit cases: version ordering, malformed versions, empty/draft/prerelease/missing asset, HTTP errors/rate limit/offline/timeout, hostile redirect/repository, incompatible manifest and invalid download preserving original.
- Evidence: artifacts/verification/lifecycle-20261026-232306 and per-build directories. Final committed build and downloaded release assets must be checked before publication; no claim of publication is made by this source report.

## Explicit limitations / NOT TESTED
- Physical close/tray/focus, restoration of a minimized real form, mixed elevated/non-elevated GUI, separate user/RDP sessions, DPI and real runtime installation: NOT TESTED. Console IPC tests are not GUI acceptance.
- Real published EXPC GUI update end-to-end: NOT TESTED. Helper fixture replacement is separate evidence. Runtime dialogs and external uninstallers were never started.
- Signed-release trust chain/revocation, actual removable-volume disconnect/access-denied filesystem, Windows shutdown during an active external operation: NOT TESTED.
- No claim that PDF-XChange/leftover or package upgrade field defects were fixed. Existing product data/vault remain in their previous location.
- Old EXEs without self-update/instance protocol require one manual replacement and closing old instances. Subsequent releases can use Help → Update application.
- The application and launcher are unsigned. A compatible stable .NET Desktop 10 x64 runtime >=10.0.12 is still required.

## Release protocol
Own repository only: https://github.com/AlexHDman/EXPC-Uninstaller . No BCU upstream push, no force push, no overwritten tags. Selected source commit → clean detached build → tag → draft with EXPCdelete.exe, EXPCdelete.update.json, SHA256SUMS.txt and RU/EN notes → authenticated download/hash comparison → publish stable/latest → unauthenticated public API/download validation. Gate failure leaves a draft.

GitHub REST references: https://docs.github.com/en/rest/releases/releases and https://docs.github.com/en/rest/releases/assets .

Repeat: `pwsh -NoProfile -File scripts/Build-EXPCdelete.ps1`; fixtures: `python -X utf8 scripts/Test-ExpcLifecycle.py` (no GUI; own disposable processes only).

Diff review: source/config paths preserved; line-ending-only noise removed against original Git blobs. Current code whitespace checks pass; three pre-existing trailing-space lines in archived docs/upstream/ci-original.yaml are retained as historical evidence. Secret marker matches were only the two scanners testing for a PEM header; no credential payload was found. User Visual Master/local audit materials excluded from the commit.
