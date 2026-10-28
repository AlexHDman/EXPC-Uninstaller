# EXPC 6.3.5 — leftover result and compact window

Scope: explicitly approved separate leftover defect; publication approved by user. Base 53acf6d4; branch codex/fix-leftovers-6.3.5. Preserve published 6.3.4 and all user assets/data.

## Proven code defect and historical limit

CleanupBoundary.SafeDirectory requires the installation leaf to match the product name. CaptureInstallPaths applies that deletion eligibility test before scanning, including already captured paths. A version-number MSI leaf can therefore produce zero scopes rather than a meaningful check of the original path. This is reproduced by a synthetic MSI version directory. Original screenshot alone does not prove which registration fields the specific 1C install contained; real 1C incident acceptance remains NOT TESTED.

A separate snapshot now captures existing version-number MSI directories for observation only, corroborated by MSI registration identity, InstallLocation, an existing icon inside the directory, exact version leaf, no reparse points, protected-root rules and no overlap with another catalog entry. This evidence is captured BEFORE uninstall and passed with the immutable original metadata/sources. It never grants deletion authority. An absent observed directory is a successfully checked scope. A surviving observation-only directory or related unproven registry key produces a partial result, never automatic cleanup. Missing evidence/access errors remain partial; missing scopes are not declared clean. No 1C-specific exception or broad disk search. Known safe remaining files still become selectable entries. Other versions/shared roots are rejected by ownership boundaries.

## UI

Empty and incomplete outcomes use a compact 560x220 logical client area, short RU/EN status, Close and Details. No unused tree area or journal buttons on the main result. Details reveal a bounded scrollable journal with Copy/Save; Clear removed. Saved operation journal is preserved and receives scan errors/warnings. Tree results retain the larger existing window and selection safeguards. Current theme retained. Programmatic 96/144/192 scaling and text measurement are OFFSCREEN checks, not physical DPI acceptance.

## Verification

- 38 component/offscreen tests passed; 4 external-process fixture scenarios skipped because EXPC_OPERATION_FIXTURE was not supplied.
- 3/3 additional nonshown-form text-fit checks at logical 100/150/200% passed.
- Production StandardRemoval snapshot -> synthetic uninstall removing the known file/directory -> production Scan: original metadata retained, checked scope count positive, no found leftovers or warning.
- Existing-but-unowned observed directory -> partial, no deletion candidate; absent path -> checked; injected access denial -> warning; insufficient metadata -> warning; competing installation -> no observed ownership; known safe remaining file -> candidate.
- Existing unchecked-profile/registry-child selection guards exercised by OperationWindowTests; no real user data used.
- Canonical C#/C++ build, versions, native policy and payload gates. Final exact-commit receipt under artifacts/verification/release-6.3.5.
- Diff/secret markers reviewed; user Visual Master excluded from commit. No GUI auto-launch, no changes to installed applications.

NOT TESTED: actual 1C uninstall, information bases, physical GUI/DPI on real monitors and historical installation metadata. No universal claim that 1C never leaves data. Self-update and other existing features retained.

After publication: user acceptance; active testing/bug collection, functional development paused.

## Published result — 2026-10-29 (Europe/Moscow)

Stable/latest: https://github.com/AlexHDman/EXPC-Uninstaller/releases/tag/v6.3.5
EXE: https://github.com/AlexHDman/EXPC-Uninstaller/releases/download/v6.3.5/EXPCdelete.exe
Producer commit 6a8d5c6d3f2a3648d32a78833814e575a6c068f3, tag v6.3.5. Documentation-only follow-up does not move this tag.
Local dist/EXPCdelete.exe: 9,366,528 bytes, SHA256 2f8b6f888b47c07071341e691fb93cadefed42a6cc54db6df49a72c287e4a188.
Clean build receipt: build/release-6.3.5-source/artifacts/verification/20261028-235838, copied to artifacts/verification/release-6.3.5.
125/125 canonical safe tests passed. Final exact-commit ActionTests/OperationWindowTests: 41 passed, 4 skipped, including programmatic text-fit/placement checks at 96/144/192. No physical DPI acceptance inferred.
Draft assets downloaded back and matched before stable publication; public latest API and all assets verified without token. Production update client sees version=current=6.3.5; older needs update and newer is not downgraded.
User asset hash preserved. No upstream writes, force push or merge. Existing releases retained. Final EXE not launched. Actual 1C uninstall and physical GUI NOT TESTED.
NEXT: user acceptance. Active testing / bug collection; functional development paused.
