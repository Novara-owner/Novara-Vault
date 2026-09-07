# Changelog

<p align="center"><strong>English</strong> · <a href="CHANGELOG.zh-CN.md">简体中文</a></p>

All notable changes to Novara are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [6.2.0] - 2026-09-07

### Added

- **Small-window dialog adaptation** — a unified pass across all pages: confirm buttons are pinned to the card bottom instead of floating inside scroll areas, scroll regions get bounded heights, dialogs are clamped to the viewport and re-clamped live while the window resizes, and the welcome-tour carousel scales down centrally. Twelve dialogs that could overflow on split-screen or short windows now keep their buttons visible and their content scrollable.
- **Note dialog full-page scrolling** — the new/edit note dialog (now a fixed 600 px wide) pins its title and confirm button and scrolls the icon, name, and auto-growing content area as one page, so long notes stay editable on short windows instead of the input being squeezed into two clipped lines.
- **Infinite icon ring** — the icon pickers in the group / entry / workspace dialogs now scroll seamlessly in both directions (a three-fold mirrored carousel), map the mouse wheel to horizontal scrolling, and animate-center the selection; reopening a picker no longer keeps a stale highlight, and edit dialogs restore the saved icon correctly.
- **Editor body placeholder** — rich-text and markdown editors show a "start writing" placeholder when the body is empty, implemented with the Tiptap official CSS recipe; the markdown input font now matches the preview (Segoe UI).

### Changed

- **Region-based pinning (memo page)** — pinning now has one well-defined meaning: a pinned entry rises to the top of its own region (inside its group, or within the ungrouped stack) and never escapes it; a pinned group — globally unique — rises to the top of the list together with all its entries. Re-pinning within the same region displaces the old pin, unpinning keeps the card in place, and pinned cards cannot be dragged.
- **Ownership changes strip personal marks** — moving an entry into or out of a group (and group-deletion rescue, soft-delete to the recycle bin, or MCP group changes) now clears its star and pin, since those belong to the region rather than the entry; editing an entry keeps them.
- **Editor toolbar icons reworked** — undo, redo, and clear-formatting each get a distinct, conventional icon (clear-formatting previously borrowed the redo glyph).

### Fixed

- An incremental verification round (three parallel reviewers over all post-6.1.0 changes; no critical findings) led to four fixes: pinning the only standalone entry no longer desyncs the ungrouped stack when a group is created later; reopening the group icon ring no longer keeps a stale highlight; edit-dialog backfill centering on the icon ring is no longer overridden by the initial position jump; and dialogs that are already open while the window resizes are re-clamped (Plan / Settings / File-path pages).

## [6.1.0] - 2026-09-06

### Fixed

- **Memo page: creating a group after wiping memo data appeared to fail.** The group was actually created and persisted, but the workspace-era "hide empty groups" filter applied unconditionally and collapsed the new empty card immediately (and again on every reload), so it looked like nothing happened. Empty groups are now always visible when no workspace filter is active — the hiding rule applies only while a workspace is selected, which was its original intent.
- While a workspace is active, creating an empty group still keeps it hidden by design (group visibility follows its entries); the toast now says so explicitly instead of a generic "created".

## [6.0.0] - 2026-09-06

### Added

- **Motion design system** — one token-based animation layer drives the whole UI: a "hidden-light" navigation glow that traces the selected tab, page transitions, dialog depth and staggered entrances, card entrances, and press feedback. Durations and easing curves come from a single source (`Services/Motion.cs`), so motion stays consistent instead of accumulating one-off effects.
- **Workspaces** — lightweight virtual groups that span all five data types. Assign cards to a space and switching spaces reshapes every list at once; the database itself stays flat, nothing moves.
- **Quick Capture** — a global hotkey opens an always-on-top entry box from any app; dispatch the text to a memo, todo, or note and get back to work.
- **Network activity indicator** — the title bar shows a local-only state and briefly names the endpoint whenever Novara makes one of its user-triggered API calls, so outbound traffic is never silent.
- **Welcome tour** — a four-page walkthrough on first launch, built from the real UI: menus, navigation, the desktop-note demo, and the privacy lock.

### Changed

- Unpinning a card now keeps it where it is on the plan and records pages (matching memo behavior) instead of dropping it back into time order; context menus order "Pin" before "Star" everywhere.

### Fixed

- Roughly 250 issues from five exhaustive verification rounds over the entire codebase. Highlights: encrypted state transitions (enable / disable / migrate / re-encrypt) are now fully transactional — there is no window where the disk holds plaintext while memory believes it is encrypted; snapshot restore is two-phase with rollback; IPC pending-write files are atomic; a stale chunked render can no longer duplicate recycle-bin cards; title input no longer swallows IME candidate confirmation.

### Security

- MCP hardening: malformed JSON gets a spec-compliant `-32700` error response instead of hanging the client until timeout; `read_item` argument validation happens before the permission gate; audit-log fields are length-capped and credential-masked; chat response bodies are capped at 32 MB.
- The CSV formula-injection guard now round-trips symmetrically: values that begin with a quote keep it through export and import.
- Snapshot file names are validated before delete or restore, blocking path traversal out of the backup directory.
- `serverInfo.version` in the MCP handshake reports the real assembly version.

## [5.3.0] - 2026-08-31

### Added

- **TOTP two-factor** — paste an `otpauth://` URI or a Base32 secret into email / account / website / WiFi entries. Memo cards show a live 6-digit code with remaining seconds, a countdown bar, and one-click copy.
- **Agent Permission Center** — each approved MCP client gets its own 20-bit permission matrix (read / create / update / delete across the five data types). New clients start read-only everywhere except memos; deletion additionally requires a global master switch. Unauthorized attempts land in the audit log.
- **Database health check** — the data overview card gains encryption status, latest backup, snapshot count, file integrity, and orphan-reference indicators.
- **Secret generator** — random passwords, UUIDs, and tokens generated inside entry dialogs.
- **Command palette** — Ctrl+K doubles as a command launcher via a `>` prefix: create items, open pages, lock now.
- **Remark copy buttons** — remark fields gained one-click copy buttons alongside the other sensitive fields.

### Changed

- **Key derivation hardened (format v3)** — PBKDF2-SHA256 iterations raised from 100,000 to 3,000,000 (about 340 ms unlock on the reference machine). v2 databases migrate through a one-time opt-in prompt; the lock-screen password hash moved to PBKDF2 as well. Databases upgraded to v3 cannot be opened by versions 5.2.0 and earlier.
- **Desktop sticky-note host ships as a complete self-contained bundle** in a `Host\` subfolder — the old bare-exe deployment died instantly on clean machines (Event 1023, "The application to execute does not exist").
- Soft-delete confirmation dialogs unified across all pages; refreshed trash / lock / command-line icons.

### Security

- AngleSharp updated to 1.5.0 (CVE-2026-54570).
- The machine-local `AppxMSBuildToolsPath` MSBuild property was removed from the public project file.

## [5.2.0] - 2026-08-29

### Added

- **Encrypted backup export / import (`.novaenc`)** — a self-contained v4 container (password → KDF → AES-256-GCM, header bound as AAD) protected by a separate backup password with a strength meter. Plaintext exports now warn that the file contains sensitive data.

### Fixed

- Root-caused the sync-lock livelock (session-lock criterion) and a regression in the privacy-gated startup flow.

## [5.1.0] - 2026-08-29

### Added

- **MCP audit log** — every agent call is recorded locally: who (process), when, which tool, which object, and the result — including denied attempts. Viewable from the MCP settings card.
- **Auto-Lock & Lock Now** — lock on demand (Ctrl+Shift+L), after an idle timeout (5 / 10 / 30 / 60 minutes), or when Windows locks its session.
- **SHA-256 integrity headers** for plaintext exports; older MD5-headered files remain importable via dual-header detection.

### Fixed

- Around 20 fixes from the N6 verification round, including a startup crash (`0xc000027b`) reported on some machines.

## [5.0.0] - 2026-08-25

### Added

- **MCP Agent interface** — a native MCP server (`NovaraMCP.exe`) exposing 14 tools across five data types (`memo` / `todo` / `note` / `diary` / `path`): `create_*`, `update_*`, `delete_item`, `list_items`, `read_item`, and `search_items`.
- **Records page** — Markdown documents join the HTML diary: source/preview split editor (WebView2 + markdown-it), Markdown import, a filter bar, and per-card format badges.
- **API relay probe** — an eight-probe detection system (identity, capability benchmark, format compliance, token billing, tool calling, hidden injection, response poisoning, long-context truncation) that produces a weighted risk verdict, with randomized nonce-tagged prompts and a local updatable `ProbeDataSet.json`.
- **API status diagnosis** — a second detection entry that infers balance presence, reads metadata, and measures latency / TTFT.
- **8 memo entry types** — Bank Card, WiFi, and ID added.
- **CSV import/export** — auto-detects Novara, KeePass, and Bitwarden formats.
- **PDF / HTML collection export** — export all records as a printable HTML or PDF collection.
- **Indexed + fuzzy global search** — faster Ctrl+K with subsequence fuzzy matching and title-first ranking.
- **Windows Hello unlock** — biometric / PIN unlock for the privacy lock.
- **Rolling auto-backup** — up to 10 local snapshots with restore.

### Changed

- **"Diary" tab renamed to "Records"** — the long-form page now hosts both HTML diary and Markdown documents.

### Security

- **MCP security model** — off by default; token authentication (fixed-time comparison); database-unlock gate; per-process authorization whitelist; a separate delete permission; sensitive-field redaction with anti-relabeling protection.

## [4.0.0] - 2026-08-18

### Added

- Card drag-and-drop reordering across all four pages, with order persisted.
- Bidirectional desktop-todo sync (desktop checkbox writes back to the main app).
- Diary Markdown export (single entry, with or without images).
- Data overview panel (entries, groups, todo completion rate, storage usage).
- Keyboard shortcuts (Ctrl+1~4 tabs, Ctrl+, settings, Ctrl+Shift+Backspace recycle bin).
- Export dropdown (Markdown summary / native backup).

### Changed

- API connectivity test overhaul: vendor recognition, protocol matrix, refined error codes, and a detail dialog.
- Unified brand-button system and extensive brand styling across buttons, dialogs, dropdowns, and checkboxes.

### Security

- Monotonic clock for the 30-minute lockout (resists system-time rollback).

### Engineering

- Batch rendering for large lists, crash logs written locally (redacted), rolling backups, debounced writes, and a unit-test project (`Novara.Core` + xUnit).

## [3.0.0] - 2026-08-12

### Added

- Desktop sticky notes (StickNoteHost) with drag/resize, lock & pin, theme/language sync, and desktop editing.
- Global search (Ctrl+K) with jump-to-result and a brand-colored flash.
- Recycle bin with 7-day auto-purge.
- Five interface languages (简体中文 / 繁體中文 / English / 한국어 / 日本語) plus Follow System.
- System reminders (desktop countdown cards).
- Global right-click menu (add paths from Explorer / open Novara from the desktop).
- File/folder pickers for path entries, and path validity detection.
- Website memo entry type; required-field validation for email/account entries.
- Export configuration (exclude path entries by default for new machines).
- Rich-text editor upgraded to Tiptap 2.27 (WebView2).

### Security

- AES-256-GCM authenticated encryption (migrates legacy AES-CBC databases).
- Password extended from 6-digit PIN to 6–64 characters.
- 30-minute lockout after 5 failed attempts, persisted across restarts.
- Dual-salt SHA-256 password hashing.

## [2.0.0] - 2026-08-07

### Added

- Four tabs (memo, path backup, plan, diary) plus a settings page and an optional privacy lock.
- Encrypted storage (AES-256-CBC), system tray, theme switching, and bilingual UI.
