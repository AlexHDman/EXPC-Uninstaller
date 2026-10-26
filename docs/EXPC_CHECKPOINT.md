# EXPC checkpoint — release 6.3.1

EXPC Uninstaller | Release preparation | Development active only for approved self-update/lifecycle scope | Codex, Windows CLI/headless; model/effort unknown.

Starting branch expc/updates-vault @ 71d8390e09305e8148dd16bbf8bdf238923638c3. Existing restructuring preserved; user-modified assets/UI_Master_Visual.png and local unknown files are not part of this release commit. Version: EXPC.Version.props, 6.3.1.

Implemented: Help self-update, guarded replacement/helper/handshake, per-user-session native + managed instance protocol, real Close/Alt+F4/Exit. No changes to removal/leftover algorithms. See EXPC_RELEASE_6.3.1.md for evidence and limits.

Pre-release checks: 103/103 selected tests; native policy; console lifecycle and replacement fixtures. Final exact-commit build, artifact SHA and publication receipts belong in ignored artifacts/verification and GitHub Release, not a self-referential source commit.

GUI/tray/focus/DPI/mixed-integrity/live published GUI update: NOT TESTED. Final EXE must not auto-start. Publication explicitly authorized only for this release in own AlexHDman/EXPC-Uninstaller repository; no upstream writes/merge/force push.

After release: active user testing and bug collection; functional development paused. NEXT: user manually tests lifecycle and Help → Update application. Existing leftover/package-update defects remain open.
