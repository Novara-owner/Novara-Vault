# Regression Tests

English · [简体中文](REGRESSION_TESTS.zh-CN.md)

How bugs fixed over the last six months map onto the automated regression surface. The project started in August 2026, so "the last six months" is the project's whole life. The bug sources are the [CHANGELOG](../CHANGELOG.md) "Fixed" entries and the verification-round summaries; the regression surface has two layers:

1. **Unit / integration tests** — `Novara.Tests` (xUnit, 50+ files, 1,076 tests) covering the data, crypto, sync and server layers, which reference the shipping assemblies directly.
2. **Static guards** — `Tools/page_lifecycle_guard.py` (1,032 checks), `Tools/reverse_verify.py` (207 mutation-verified entries), `Tools/cooldown_guard.py`, `Tools/danger_red_guard.py`, `Tools/guards.py` (the suite that ties them together). WinUI pages cannot be referenced by the test project by construction, so page-level fixes are pinned as source-form invariants in these guards — each entry is verified by mutation (removing the fix must fail the guard).

## Mapping (named fixes in the changelog)

| Version | Fixed issue | Regression test |
|---|---|---|
| 12.0 | Follow-memory values normalize on read, ship as optional sync fields, and treat a remote null as "no opinion" | `Novara.Tests/MemoDisplayModeTests.cs` (normalize, persisted contract, untouched models) + `SyncDiffTests.UngroupedCardExpanded_*` (CI) |
| 12.0 | Interaction regressions caught by the cycle's review passes (spontaneous ungrouped-card expand, lost first-expand animation, text-only fold fade) | Source-form invariants in `page_lifecycle_guard`, each with mutation cases |
| 11.1 (R1) | HTML editor image picker used the wrong COM collection; multi-select was disabled | `RegressionGuardsTests.FilePicker_MultiSelectUsesShellItemArray` (CI) |
| 11.1 (R1) | Multiple images replaced each other; image selection was hard to clear | `RegressionGuardsTests.DiaryEditor_ImageSelectionAndMultiInsert` (CI) |
| 11.1 (R1) | Toolbar commands stole focus from the editor | `RegressionGuardsTests.DiaryEditor_ToolbarKeepsEditorFocus` (CI) |
| 11.0 | Hardening pass (~470 fixes across the codebase) | Full guard suite + the cycle's CI tests; the editor hotfix is pinned in the 11.1 rows |
| 10.3 | Exported files lost their extension | `RegressionGuardsTests.FilePicker_KeepsTheExtensionInvariant` (CI) + local guard |
| 10.2 | Toolbar edits did not refresh the preview | `RegressionGuardsTests.EditorToolbar_ActionsSyncThePreview` (CI) |
| 10.2 | Split-view typing jumped to the bottom | Manual verification (WebView2 editor surface) |
| 10.1 | Pickers in elevated instances | `RegressionGuardsTests.FilePicker_UsesNativeWin32DialogsOnly` (CI) |
| 9.4 | Password dialogs double-submitted | Guard: `page_lifecycle_guard` in-flight lock check |
| 9.4 | Reminder border lost after drag | Guard: `page_lifecycle_guard` border-preservation check |
| 9.4 | Server write fence against cross-process delete races | `SpaceAdminTests`, `SqliteSpaceStoreTests`; HTTP probe in `Tools/m3_e2e.py` |
| 9.4 | Update download validates the manifest | `UpdateManifestVerifierTests` (5 tests) + `UpdatePolicyTests` |
| 9.4 | Lock screen over the first-run wizard | Guard: `page_lifecycle_guard` lifecycle checks |
| 9.3 | Editor failure diagnostics (missing WebView2 runtime) | Manual verification + diagnostics logging review |
| 9.3 | WebView2 environment re-initialization error | `RegressionGuardsTests.EditorInit_IsReentrancyGuarded` (CI) + local guard |
| 9.3 | Mixed-language wizard text | Manual verification (locale matrix) |
| 9.2 | Reminders did not fire with the app closed | Manual verification (Task Scheduler COM integration) |
| 9.2 | Paper-theme color parser rejected 6-digit colors | `RegressionGuardsTests.PaperTheme_ParsesSixDigitColors` (CI) |
| 9.2 | Hardcoded danger reds across the UI | `RegressionGuardsTests.NoHardcodedDangerRed` (CI) + local guard |
| 9.1 | Frozen TOTP live codes (GC of managed wrappers) | `RegressionGuardsTests.TotpRegistry_UsesStrongReferences` (CI) + local guard |
| 9.1 | Cooldown confirm-key first-frame flash | `RegressionGuardsTests.CooldownHosts_KeepInlineButtonTemplates` (CI) + local guard |
| 9.1 | Card drag stutter | `RegressionGuardsTests.DragAutoScroll_UsesTheCompositorFrameCallback` (CI) |
| 9.0 | Container health check with host allowlist | HTTP-level probe in `Tools/m3_e2e.py` |
| 6.2 | Incremental verification round | Covered by the guard suite below |
| 6.1 | New empty group vanished under the no-filter view | `RegressionGuardsTests.EmptyGroup_VisibilityIsWorkspaceConditional` (CI) + local guard |
| 6.0 | ~250 issues from five verification rounds | The whole guard suite: 193 reverse-verify entries + 368 lifecycle checks + the guard family — these rounds are why the guards exist |
| 5.2 | Sync-lock livelock | `SyncPlannerTests` (session-lock lifecycle rules) |
| 5.1 | ~20 fixes from a domain-wide verification | Covered by the guard suite below |

## Data/crypto/sync/server fixes land with tests by policy

The contributing policy requires major functionality to arrive with tests, and the layers that can be unit-tested have never shipped a fix without one. Representative test files and what they pin: `StoreTests` (28, storage format and atomic writes), `SyncContractTests` (19, envelope MAC + keywrap vectors), `SyncEngineTests` (16, engine rounds against a real in-process server), `SyncServerTests` (35, optimistic concurrency, quota, retention), `RelayProbeServiceTests` (37), `ApiChatClientTests` (27), `McpLogicTests` (18), `BackupIntegrityTests` (18), `HeaderV5Tests` (6, RFC 9106 Argon2id vectors), `KdfHardeningTests` (6), `UpdateManifestVerifierTests` (5), and the rest — see the suite index in `Novara.Tests`.

## Totals

- The table spans the 11.1 editor hotfix, the 11.0 hardening pass and the earlier named fixes. Each R1 fix added a `RegressionGuardsTests` source-form assertion (CI); the 11.0 pass is backed by the full guard suite. The historical split between behavioural tests, source-form assertions, local guards and manual verification is shown per row rather than aggregated, because the mix and the suite size change every release.
- The local guard scripts (which run before every local build and are mutation-verified) back the CI assertions and the aggregate rounds (6.0's ~250 issues, 5.1's ~20) — they are development infrastructure and intentionally not part of this public repository.

---

*Last updated: 2026-10-28*
