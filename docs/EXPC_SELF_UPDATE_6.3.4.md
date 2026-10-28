# EXPC 6.3.4 — self-update repair and investigation

2026-10-28. Scope approved by user: self-update, profile-data safeguards and lifecycle only. Branch codex/release-6.3.4, base 766bd206. No changes to removal, leftovers or third-party application updates. GUI/desktop control prohibited.

## Reported failure and evidence boundary

User reports local 6.3.2 stays at Downloading and verifying when updating to 6.3.3. Screenshot 2026-10-28_201942.jpg. No usable historical update-request/result logs were found in the specific update cache paths. The process was no longer running at inspection. The exact historical stall stage and cause are UNKNOWN; no deadlock is claimed reproduced.

6.3.2 was a local candidate, not a published GitHub release: remote releases before this task were 6.3.1 and 6.3.3. Preserved 6.3.2 EXE SHA256 fff697bcfcc3aae3da8165d0cab35c686ff65f1b622cb4436a345291b0cff49b. Its actual cached DLL (assembly version 6.3.2.0), invoked through reflection without Main/GUI, completed Check/Fetch/Verify of published 6.3.3 in 6.39 seconds (9,359,872 bytes, expected SHA256). Its actual helper SHA256 7b3d8d63eeddc1e7a2c9dba89bf93124344feabf2d1e9d77c9e5e3f0d1b11b71 completed replacement/handshake on an independent no-window executable fixture. This does NOT prove old GUI end-to-end success.

Static wait graph: native launcher waits only its managed child; helper waits the exact app PID/start-time then launcher PID/start-time, each at most 60 seconds. New launch follows both exits, so launcher/app instance locks have been released. No process termination by name or external installer termination. Historical screenshot cannot establish which process held the original EXE. No compatible manifest change is justified: published metadata/hash checks already pass in the old client. For the user's stalled 6.3.2, use one manual replacement to obtain the new client; do not promise that a newly published EXE repairs code already running in 6.3.2.

## Implemented

- Visible download byte counts and separate verification/restart stages. HTTP bounded at 90 seconds, Fetch at 3 minutes, body inactivity at 20 seconds. Cancellation does not replace the original. Network/verification continuations avoid the GUI context; verification runs off the UI thread.
- Small RU/EN update window: actual versions, at most three explicitly marked localized release-summary bullets, neutral fallback, release link, Update/Cancel. Full release body/hash/test report is not shown. Explicit operation journal link appears after starting an update.
- Logs/self-update-<id>.jsonl records stages, PID, waited PID, exception type and code for explicit self-updates only; no secrets or startup telemetry. Helper result persists in Logs even after the app exits. One-time outcome notification on next startup; journal retained.
- Client waits up to 10 seconds for helper acknowledgement. Helper accepts a separate committed handoff within 15 seconds before waiting for exit. Closing/cancelling before commitment cannot authorize a later replacement. Deferred main-window Close after modal dialog returns; double activation guarded.
- Per-user/session helper lock retained through replacement AND failure/rollback. Old app and launcher waits 60 seconds each; new handshake 60 seconds. No kill or restart loop. A still-running unacknowledged candidate is not killed: verified rollback is retained for recovery.
- Download remains in user Cache. Verified candidate is copied next to the target before File.Replace; backup is also on target volume. Original identity rechecked immediately before replacement. Successful operation removes its transaction files; failure preserves recovery evidence. This removes 6.3.3's unconditional cross-volume rejection, without claiming real cross-volume testing.
- All permanent Data/Cache/Logs and no-tray behavior retained. Only short-lived transaction EXEs beside the target are now explicitly authorized. Metrics remain off unless EXPC_DIAGNOSTICS=1. Legacy data copied without overwriting; user data untouched by these tests.
- Locked dist preserves prior EXE and verified .new. scripts/Promote-ExpcCandidate.py <verification-directory> retries only verified staging after unlock, with previous hash preserved.

## Verification

- Production networking: old 6.3.2 Check/Fetch/Verify passed against 6.3.3; new published client checked separately in final receipts.
- Regression tests include stalled body/cancel, incorrect hash, successful retry, progress, marked-language summaries/fallback, live-process timeout without kill, explicit journal, target-side staging, rollback and neighbouring-data preservation.
- Real production helper on private fixtures: parallel helper rejected; old PID timeout; handshake timeout with exited-candidate rollback; retry; readonly target denial; accepted-but-uncommitted handoff refused; committed handoff success; cleanup. Only no-window fixture binaries launched.
- Native/managed five sequential and parallel/simultaneous starts, exit/restart/crash, cross-layer ownership; locked target preservation; real swap/rollback. Real window focus not inferred from these checks.
- Nonshown STA form close/vault-lock test passed. No physical desktop UI used.
- Promotion fixture: locked target preserves old hash and pending candidate; retry promotes and retains rollback.
- Source diff/whitespace and secret/packaging checks before commit/release. Published 6.3.3 tag/assets remain immutable.

## NOT TESTED

Actual user GUI end-to-end, focus/tray/DPI, real user vault migration, actual replacement between two distinct filesystem volumes, and mixed-elevation/session LIVE acceptance. No isolated interactive environment was available/used. Different-directory staging is tested; it is not a two-volume test. User programs were not installed, updated or removed.

Final exact-commit build, tag, release, hashes and public asset receipts are recorded in artifacts/verification/release-6.3.4 and the post-publication checkpoint. Any earlier dirty-candidate hash is not the released artifact. Next: user manual acceptance; functional development returns to pause after handoff.

## Final publication result

- Stable/latest release: https://github.com/AlexHDman/EXPC-Uninstaller/releases/tag/v6.3.4
- EXE: https://github.com/AlexHDman/EXPC-Uninstaller/releases/download/v6.3.4/EXPCdelete.exe
- Producer 902ab8caa006e488e04161ae61fe1a5b56c83cb1, immutable tag v6.3.4; documentation-only follow-up does not move the tag.
- Local dist/EXPCdelete.exe: 9,371,648 bytes, SHA256 c14340d28efa4e4c5efcec1ecb5710888b8ab142e4f8676286ef64ec1147db2f.
- Clean exact-commit build: build/release-6.3.4-source/artifacts/verification/20261028-204949. 122/122 selected tests, native policy/version/payload gates passed; 33 payload files, personal-data markers not found. Separate targeted regression/offscreen close run: 23/23.
- Exact packaged helper SHA256 d43e9174bca262ff0c6be48d05f2bf4a4aad6bdda32c906baf1c1f36487bd748 passed failure/retry/handoff scenarios in artifacts/verification/selfupdate-20261028-205111. Fixture journals were copied into project evidence and only their exact files/notification removed; no fake test outcome left for the user.
- Draft assets downloaded and matched BEFORE stable publication. Public GitHub latest/API/assets then verified without token; EXE/manifest/SHA256 match local files.
- Exact-commit production client check: current=release=6.3.4, older requires update, newer is not downgraded. Production Download/Verify: 5.59 seconds, expected bytes/hash; RU/EN summaries each contain three items.
- Exact old 6.3.2 component also downloaded/verified published 6.3.4 in 5.63 seconds. Old DLL/helper hashes match the preserved 6.3.2 payload receipt. This remains component evidence, NOT GUI acceptance.
- Published 6.3.3 remains unchanged. No merge/force push, no GUI EXE auto-launch, no installed application changes. User Visual Master hash unchanged; unrelated files left uncommitted.

STATUS: published, automated/component checks passed; original GUI incident cause UNKNOWN; GUI and actual distinct-volume replacement NOT TESTED. NEXT: close the stalled 6.3.2 and manually replace that EXE once with 6.3.4. Active user testing / bug collection; functional development paused.
