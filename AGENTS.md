# EXPC Uninstaller — project instructions

Read C:\AI\AGENTS.md, C:\AI\CLAUDE.md and C:\AI\EXPC_DEVELOPMENT_PROTOCOL.md before work. Preserve these project-specific restrictions.

- Work only in C:\AI\EXPC-Uninstaller. Stage: active user testing / bug collection; functional development paused. Read docs/EXPC_CHECKPOINT.md and docs/EXPC_BACKLOG.md.
- No desktop control, visible GUI, interactive Sandbox, runtime installation, or changes to installed applications without a separate explicit instruction. Never auto-start the final EXE.
- No commit/push/merge/tag/release without explicit authorization. Shared Git workflow does not authorize publishing this project.
- Preserve user assets, dirty worktree, local-data, vaults, backup keys and diagnostic evidence. Do not read secrets into reports.
- Canonical build: pwsh -NoProfile -File scripts/Build-EXPCdelete.ps1. Output dist/EXPCdelete.exe; manifests under artifacts/verification. All build operations bounded; previous dist survives failed gates.
- No blanket git clean/reset or recursive build/artifacts cleanup. build/toolchain is protected. See docs/EXPC_RESTRUCTURE_MAP.md for historical paths.
- Keep original graphics and upstream attribution. No functional fixes during this restructuring/testing pause. Maintain checkpoint/backlog, distinguishing unit checks from GUI/live acceptance.

Release 6.3.1 exception: user explicitly authorizes scoped self-update/lifecycle implementation, commit/push/tag and draft-to-stable GitHub Release in AlexHDman/EXPC-Uninstaller only, after gates. No upstream writes, merge, force push or desktop control. After handoff return to active user testing / functional development paused. See docs/EXPC_RELEASE_6.3.1.md.
