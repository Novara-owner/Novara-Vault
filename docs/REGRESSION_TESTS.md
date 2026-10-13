# Regression Tests

English · [简体中文](REGRESSION_TESTS.zh-CN.md)

How bugs fixed over the last six months map onto the automated regression surface. The project started in August 2026, so "the last six months" is the project's whole life. The bug sources are the [CHANGELOG](../CHANGELOG.md) "Fixed" entries and the verification-round summaries; the regression surface has two layers:

1. **Unit / integration tests** — `Novara.Tests` (xUnit, 35+ files, ~850 tests) covering the data, crypto, sync and server layers, which reference the shipping assemblies directly.
2. **Static guards** — `Tools/page_lifecycle_guard.py` (368 checks), `Tools/reverse_verify.py` (193 mutation-verified entries), `Tools/cooldown_guard.py`, `Tools/danger_red_guard.py`, `Tools/guards.py` (the suite that ties them together). WinUI pages cannot be referenced by the test project by construction, so page-level fixes are pinned as source-form invariants in these guards — each entry is verified by mutation (removing the fix must fail the guard).

## Mapping (named fixes in the changelog)

| Version | Fixed issue | Regression test |
|---|---|---|
| 10.3 | Exported files lost their extension | Guard: `reverse_verify` extension-append entry + final write-time guard pinned in code |
| 10.2 | Toolbar edits did not refresh the preview | Manual verification (WebView2 editor surface) |
| 10.2 | Split-view typing jumped to the bottom | Manual verification (WebView2 editor surface) |
| 10.1 | Pickers in elevated instances | Manual verification (native Win32 dialog migration) |
| 9.4 | Password dialogs double-submitted | Guard: `page_lifecycle_guard` in-flight lock check |
| 9.4 | Reminder border lost after drag | Guard: `page_lifecycle_guard` border-preservation check |
| 9.4 | Server write fence against cross-process delete races | `SpaceAdminTests`, `SqliteSpaceStoreTests`; HTTP probe in `Tools/m3_e2e.py` |
| 9.4 | Update download validates the manifest | `UpdateManifestVerifierTests` (5 tests) + `UpdatePolicyTests` |
| 9.4 | Lock screen over the first-run wizard | Guard: `page_lifecycle_guard` lifecycle checks |
| 9.3 | Editor failure diagnostics (missing WebView2 runtime) | Manual verification + diagnostics logging review |
| 9.3 | WebView2 environment re-initialization error | Guard: `page_lifecycle_guard` re-entrancy check |
| 9.3 | Mixed-language wizard text | Manual verification (locale matrix) |
| 9.2 | Reminders did not fire with the app closed | Manual verification (Task Scheduler COM integration) |
| 9.2 | Paper-theme color parser rejected 6-digit colors | Guard: `danger_red_guard` palette checks; theme apply path reviewed |
| 9.2 | Hardcoded danger reds across the UI | Guard: `danger_red_guard` (hex + `FromArgb` scan) |
| 9.1 | Frozen TOTP live codes (GC of managed wrappers) | Guard: `page_lifecycle_guard` strong-reference pins |
| 9.1 | Cooldown confirm-key first-frame flash | Guard: `cooldown_guard` (20 hosts audited) |
| 9.1 | Card drag stutter | Manual verification (frame-loop perf) |
| 9.0 | Container health check with host allowlist | HTTP-level probe in `Tools/m3_e2e.py` |
| 6.2 | Incremental verification round | Covered by the guard suite below |
| 6.1 | New empty group vanished under the no-filter view | Guard: `page_lifecycle_guard` visibility checks |
| 6.0 | ~250 issues from five verification rounds | The whole guard suite: 193 reverse-verify entries + 368 lifecycle checks + the guard family — these rounds are why the guards exist |
| 5.2 | Sync-lock livelock | `SyncPlannerTests` (session-lock lifecycle rules) |
| 5.1 | ~20 fixes from a domain-wide verification | Covered by the guard suite below |

## Data/crypto/sync/server fixes land with tests by policy

The contributing policy requires major functionality to arrive with tests, and the layers that can be unit-tested have never shipped a fix without one. Representative test files and what they pin: `StoreTests` (28, storage format and atomic writes), `SyncContractTests` (19, envelope MAC + keywrap vectors), `SyncEngineTests` (16, engine rounds against a real in-process server), `SyncServerTests` (35, optimistic concurrency, quota, retention), `RelayProbeServiceTests` (37), `ApiChatClientTests` (27), `McpLogicTests` (18), `BackupIntegrityTests` (18), `HeaderV5Tests` (6, RFC 9106 Argon2id vectors), `KdfHardeningTests` (6), `UpdateManifestVerifierTests` (5), and the rest — see the suite index in `Novara.Tests`.

## Totals

- Named changelog fixes with a mapped regression artifact: **26/26** (17 unit/integration-test or mutation-verified guard entries, 9 manual verifications for WebView2/COM/native-dialog surfaces that cannot be hosted in a unit test).
- Aggregate rounds (6.0's ~250 issues, 5.1's ~20) are covered by the standing guard suite rather than one test per issue.

---

*Last updated: 2026-10-13*
