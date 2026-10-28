# EXPC checkpoint — 6.3.3 release preparation

EXPC Uninstaller | Active user testing / bug collection | Development paused after approved user-data/tray fix | Codex / Windows CLI, model/effort unknown.

Release branch codex/release-6.3.3, starting from ddb0fd6b; Release 6.3.3 commit/push/tag/draft-to-stable publication explicitly authorized 2026-10-28. Final producer/asset receipts will be under artifacts/verification/release-6.3.3. Local EXE dist/EXPCdelete.exe: 6.3.3, 9359360 bytes, SHA256 90615fb6b01b9777dfc61c37156f2593499c1538689f718490d149d8f7c5cfc5.

Completed: Data under LocalAppData/EXPC/Uninstaller; copy-only legacy import, no writes beside EXE; tray removed; close exits unless an operation is active. Cache/optional Logs retained. Update staging/rollback in user cache; cross-volume auto replacement unsupported without touching original.

CLI 116/116 + native/version/payload; nonshown WinForms close 1/1; console multi-instance and swap/rollback/handshake fixtures passed. GUI acceptance, real user vault migration and real update NOT TESTED. See EXPC_USER_PROFILE_6.3.3.md and artifacts/verification/20261028-183305.

User assets/old local-data/vault/diagnostic evidence preserved. Published v6.3.1 unchanged. NEXT: manually exit old instance, open local 6.3.3, verify data and close behavior. No auto GUI launch.
