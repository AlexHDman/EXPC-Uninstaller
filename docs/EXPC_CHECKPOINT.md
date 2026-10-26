# EXPC checkpoint — published 6.3.1

Project: EXPC Uninstaller | Stage: active user testing and bug collection | Functional development: paused | Completed mode: scoped implementation → headless gates → release. Agent: Codex / Windows CLI; exact model/effort unavailable.

Branch: codex/release-6.3.1. Immutable EXE producer/tag: 5dbb9ea1923183c7ccc00aa6f6f8982df0c49938 / v6.3.1. Subsequent checkpoint/report commit is documentation only; does not replace the published asset/tag.

Release: https://github.com/AlexHDman/EXPC-Uninstaller/releases/tag/v6.3.1 — stable/latest, verified through public API without token. EXE: C:\AI\EXPC-Uninstaller\dist\EXPCdelete.exe; 9364480 bytes; SHA256 c4b9d739cf447421020341e8acfc001cd05c814979a1dd1af92170619eb647e4.

Implemented: user-requested self-update and native/managed lifecycle protocol. Existing reorganization retained. Removal/leftover algorithms unchanged. User Visual Master and personal/audit data preserved outside commit.

CLI: 103/103, native policy, exact clean producer build, version gate, payload, console lifecycle/replacement fixtures. Draft downloads and public downloads match; production manifest client accepts published 6.3.1 and distinguishes older/current/newer. Evidence: artifacts/verification/release-6.3.1. Report: EXPC_RELEASE_6.3.1_RESULT.md.

LIVE GUI/tray/focus/DPI/mixed-integrity/separate users/real published GUI update: NOT TESTED. No desktop control or final GUI start. Older EXEs need manual replacement once; old running instances should be closed manually.

NEXT: user manual acceptance of the published candidate. Functional development remains paused until a new explicit instruction. Existing defects stay in EXPC_BACKLOG.md.
