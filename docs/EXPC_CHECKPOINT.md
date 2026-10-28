# EXPC checkpoint — 6.3.4 release preparation

EXPC Uninstaller | stabilization for approved self-update task | development active only within this task | Codex Windows CLI, model/effort not exposed | C:/AI/EXPC-Uninstaller | codex/release-6.3.4, base 766bd206.

User authorizes commit/push/new tag/draft-to-stable 6.3.4 in AlexHDman/EXPC-Uninstaller only. Preserve 6.3.3. No GUI/desktop control, no installed application changes. User assets and unrelated worktree files preserved.

Implementation and limits: EXPC_SELF_UPDATE_6.3.4.md. Exact old 6.3.2 network/verify and helper fixture passed, but user's GUI stall cause UNKNOWN. One manual replacement recommended for the stalled old client. New progress/handoff/journal and target-volume staging are implemented; cross-volume LIVE and full GUI update NOT TESTED.

Final producer must be clean exact commit. Artifact dist/EXPCdelete.exe; receipts artifacts/verification/release-6.3.4. Build pwsh -NoProfile -File scripts/Build-EXPCdelete.ps1; if dist locked, verified promotion retry via scripts/Promote-ExpcCandidate.py.

After handoff: active user testing / bug collection; functional development paused. No final EXE auto-launch.
