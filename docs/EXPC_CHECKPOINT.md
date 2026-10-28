# EXPC checkpoint — 6.3.4 published

STATUS: active user testing / bug collection; functional development paused.
Agent: Codex, Windows terminal; model/effort not exposed. Project C:/AI/EXPC-Uninstaller.

Branch codex/release-6.3.4. Producer commit 902ab8caa006e488e04161ae61fe1a5b56c83cb1; immutable tag v6.3.4 points to the clean exact-commit build. Documentation-only follow-up records publication; no tag change.

Release stable/latest: https://github.com/AlexHDman/EXPC-Uninstaller/releases/tag/v6.3.4
Local dist/EXPCdelete.exe: 9,371,648 bytes; SHA256 c14340d28efa4e4c5efcec1ecb5710888b8ab142e4f8676286ef64ec1147db2f.
Receipts: artifacts/verification/release-6.3.4. Detailed investigation/results: EXPC_SELF_UPDATE_6.3.4.md.

Implemented: localized concise update window/progress; bounded acknowledged/committed helper handoff; persistent explicit-update Logs; target-volume staging with rollback; verified dist promotion retry. Data/Cache/Logs, metrics off, no tray and active-operation protection retained. Removal and leftovers unchanged.

122/122 clean-build tests, native/version/payload gates; exact helper private-file fault/retry tests; 23/23 targeted regression/nonshown close checks. Draft/public assets verified; production 6.3.4 download verified in 5.59 s and exact old 6.3.2 component in 5.63 s. Original reported GUI stall cause UNKNOWN. Actual GUI end-to-end, real two-volume replacement and user migration LIVE NOT TESTED.

User assets/dirty files preserved; prior published releases unchanged. No final GUI launch. NEXT: one manual replacement of the stalled 6.3.2, then user acceptance. No further implementation/publication without task scope.
