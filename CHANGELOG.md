# Changelog

<p align="center"><strong>English</strong> · <a href="CHANGELOG.zh-CN.md">简体中文</a></p>

All notable changes to Novara are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [9.2.0] - 2026-09-30

### Fixed

- **Reminders never fired while the app was closed** - setting a reminder and closing Novara meant the system-level scheduled task was never created: the start date was formatted with the OS locale's short date, which schtasks rejects on Chinese-locale systems (an unpadded 2026/9/30), and the failure was silently swallowed. Tasks are now registered through the Task Scheduler COM API with a locale-independent start boundary, scheduling failures are reported locally instead of vanishing, and a due reminder launches the app and shows its dialog again. While the app is open everything worked as before - only the closed-app path was broken.
- **Silent partial paper-theme apply** - the paper-theme color parser only accepted 8-digit hex values, so the first 6-digit palette entry aborted the whole apply mid-table: the first half of the palette went live and the rest kept the plain light values, without any message. The parser now accepts 6- and 8-digit values, and a bad key only skips that key.
- **Hardcoded danger reds across the UI** - danger dialog titles, danger confirm buttons, red characters, trash-bin icons, the cooldown charge effect and probe verdict reds were literal hex values scattered across the interface, so paper themes could not soften them. They now flow from a single AppDanger source: paper themes soften the family per palette while light/dark keep the classic values.

### Changed

- **Reminder border gradient reworked** - the border now blends from the theme's brand blue into a dark red in RGB space and refreshes every second; the old green-to-red hue sweep stepped once every 30 seconds, which read as visible jumps. Paper themes start from their softened per-palette brand blue.
- **Urgent reminders rise in the list** - once a card enters the last 15% of its reminder window it moves directly below the pinned section, sorted by deadline, until the reminder is handled. This is display-only: manual ordering is never rewritten.
- **Breathing border on the reminder-due dialog** - the dialog now carries a soft brand-blue border that gently breathes while it is open, setting it apart from ordinary dialogs; it goes static when the animation toggle is off.
- **Path validity colors follow the theme** - existing paths now use the theme's brand blue and invalid paths the danger red, per theme (previously a fixed green/red pair).
- **Update button reminder dot** - when a check finds a new version and the upgrade dialog is dismissed, the check-update button shows a small brand-blue dot that persists across restarts until the update is installed or a fresh check reports up to date.

## [9.1.0] - 2026-09-29

### Fixed

- **Frozen TOTP live codes** - the two-step-verification row on expanded memo cards (6-digit code, seconds countdown, progress bar) could freeze at its initial value shortly after launch: the managed wrappers of the row's controls were collected by the GC while the on-screen elements kept rendering, and the liveness check misread that as "card removed" and dropped the live row. The registry now holds strong references and liveness is judged by visual-tree reachability, so codes tick, the countdown runs and the bar advances for as long as the card is on screen.
- **Full-red flash on cooldown dialogs** - the 30-second charging confirm button painted itself fully red for the first 100 ms before the countdown ticked in. The gradient now starts at the zero-charge state, so dialogs open clean.
- **Card drag stutter** - dragging memo cards re-created the drop indicator and re-laid-out the whole container on every pointer move. The indicator is now reused for the whole drag, is a no-op when the drop slot does not change, and child positions are cached between real layout changes.

### Changed

- **Silky TOTP progress bar** - the progress bar now slides continuously towards the next second instead of stepping once per second; with the animation toggle off it keeps the discrete per-second stepping.
- **Settings state colors** - the animation toggle, the theme picker (any explicit choice) and the tab-customization card (any non-full selection) light up in the brand color while active; paper themes keep their per-palette brand color. The language card intentionally stays neutral.
- **Web shell refresh** - the read-only web viewer's demo data no longer contains absolute local paths, and the service-worker cache was bumped (v18) so clients pick up the change.

## [9.0.0] - 2026-09-28

> 9.0 is the deployment release. The self-hosted server gains an official container image, a one-command compose stack with automatic HTTPS, and an operations CLI — while the data format, the cryptography and the wire contract do not move by a single byte. The server still never sees plaintext.

### Added

- **Official container image** — `ghcr.io/novara-owner/novara-sync` (mirrored on Docker Hub): multi-stage build, runs as a non-root user, `HEALTHCHECK` on `/healthz`, and a startup self-check that probes the data root with a real write — an unwritable directory exits immediately with actionable `chown` guidance instead of failing per request. The image and the bare binary are verified to behave identically. amd64 (x86-64).
- **Compose + Caddy, one command** — `deploy/compose/` brings up the sync server and an automatic-HTTPS reverse proxy together, with a first-run flow of three steps: create a space, start the stack, pair a device. The Caddyfile is parameterized so bare-metal and compose users share one file; plain HTTP and self-signed TLS are documented fallbacks.
- **Space administration CLI** — `NovaraSync space create|list|show|delete|rotate-secret`: `--json` on every command, exit codes 0 (success) / 1 (business failure) / 2 (command-line error), fully non-interactive. Deletion is a local CLI operation by design — the server API exposes no delete endpoint, by contract.
- **Per-release verification attachments** — `SHA256SUMS` for the installer, `IMAGES.txt` pinning the image digests, and an SBOM (SPDX, Syft-generated); how to use them is documented in [docs/VERIFYING.md](docs/VERIFYING.md).
- **Export as image** — a diary entry or the whole memo collection as crisp PNG long-images in two widths (820px desktop, 420px mobile), rendered off-screen and sliced automatically for very long content; sensitive exports are gated behind the privacy lock and a plain-text warning.
- **Paper themes** — four reading-friendly paper palettes (Cream / Almond / Kraft / Cold Grey) that recolor the app, the editors and the desktop sticky notes, with softened brand accents; every hard-coded brand blue in the UI now follows one source.
- **Animation toggle** — one switch turns motion off app-wide (card entrances, page transitions, hover lifts) for accessibility and low-end hardware; dialogs, press feedback and the collapse animations keep their feedback role.
- **Check for updates** — a click in Settings fetches a static manifest, downloads the installer with a progress bar, verifies its SHA-256 and hands off to a silent install. No background polling, and every request shows up in the network activity panel.
- **Desktop sticky notes, rebuilt** — frameless pure-color cards with system rounded corners, a 20-color self-contained palette, per-note color and size persistence, and context menus that follow the main app.
- **Edge menu** — the hamburger handle moves to the screen edge as a two-stage slide-out panel; the docking side is configurable in Settings.

### Changed

- **New installs start on the Almond paper theme** — existing installations keep whatever theme they had, and old versions that do not know paper fall back to the system theme.
- **Server components move to .NET 10 LTS** (`Novara.Server`, `Novara.Sync.Server`) while the desktop app and the shared `Novara.Core` stay on .NET 8. No storage-format, schema or contract change of any kind.
- **Help center** — the documentation moved to [novara.xin/help/](https://novara.xin/help/) (36 pages: getting started, features, security, sync, self-hosting, reference). The app's help links and the installer's post-install checkbox now point there; the retired single-file deployment guide is superseded by `deploy/README.md`, `deploy/compose/README.md` and the help center.

### Fixed

- The container health check keeps working when a host allowlist is configured (the probe now sends a `Host` header instead of being rejected by the server's own host filter), and the compose network pins Caddy to the upper half of the subnet so dynamic address allocation cannot collide with it.

## [8.0.0] - 2026-09-19

> 8.0 completes the second step of the connected era: Novara stops being a set of one-way snapshots and becomes genuinely cross-device. Sync runs through a server **you** host — bundled with the installer, startable with a double-click — and that server still never sees your data. The red lines did not move: the server never sees plaintext, there is no official cloud, and offline never degrades.

### Added

- **Cross-device sync** — the whole database stays converged across every device you pair, end-to-end encrypted with the same `.novaenc` v4 container used by snapshots and encrypted backups. Pairing takes three values (server URL, space id, enrollment secret); the space key is generated on your first device and never reaches the server. Your PC remains the authority: wipe the server and one push rebuilds it.
- **NovaraSync — a self-hosted server in the installer** — one self-contained executable, so the server machine needs no .NET runtime. It stores versioned ciphertext with retention count and total-size limits, authenticates devices with tokens (constant-time comparison plus failure rate limiting), and supports device registration and revocation. Two one-click scripts ship beside it: one creates the space and prints its credentials exactly once, the other starts the server; both pin the data directory to a user-writable location.
- **Web reader and limited editor** — the snapshot shell now loads live ciphertext from your server and can edit memos, to-dos and notes; changes are re-encrypted locally and uploaded together with the version they were based on. Plaintext never persists in the page: nothing is stored, the clipboard is cleared after 30 seconds, and memory is cleared after 5 minutes idle.
- **Conflict handling, stated honestly** — when two devices change the same version the later write wins, and the overwritten version is kept as a conflict copy. The desktop app offers a side-by-side comparison where you keep one side, export the other, or overwrite deliberately.
- **Device center and sync audit** — paired devices with trust state and last-sync time, per-device token revocation and reset, and a local audit log of every upload, download, conflict detection and conflict resolution.
- **Connected settings card** — server address, device name and token, the sync switch, push frequency (every save / 5 minutes / manual), last sync time and server version, in one card.

### Changed

- **Sync is opt-in and gated on the privacy lock** — a plaintext vault refuses to enable it and is guided to set a lock first; nothing leaves your machine unencrypted.
- **The installer now also carries the sync server** (about +40 MiB), so a self-hosted setup needs no separate download.

### Fixed

A pre-release verification of the whole codebase (extensive pre-release verification) hardened this release. Representative user-visible results: the MCP delete permission can no longer be granted while the delete master switch is off; `update_*` tool calls are fully validated before any field is written, so a rejected call can no longer leave a half-applied edit behind; the web editor no longer keeps an unsaved draft alive across a vault lock; and the snapshot template can no longer silently produce a viewer from the previous release.

## [7.0.0] - 2026-09-10

> 7.0 is not a feature release — it is a change of direction. Novara steps out of the single-machine era and begins the connected era: a cross-device, local-first personal data control layer for humans and AI agents, where both you and your AI can use the data — but only you hold the keys. The first step is read-only, encrypted, self-contained snapshots; sync and self-hosted deployment will grow from the same foundation under three fixed red lines: **the server never sees plaintext, there is no official cloud, and offline never degrades**.

### Added

- **Novara Snapshot** — export the whole database (or a chosen subset: memos / file paths / todos & notes / records, soft-deleted items always excluded) as a single self-contained encrypted HTML viewer. Double-click it or host it on your own NAS / server / static hosting; unlock with a password and browse read-only, completely offline. The embedded ciphertext reuses the `.novaenc` v4 container — 44-byte self-describing header as AES-GCM additional data, PBKDF2-SHA256 at 3,000,000 iterations, gzip-compressed payload — the same versioned contract as encrypted backups.
- **Viewer feature set** — four read-only sections with per-tab search (Enter to run, aligned with the desktop app), masked fields with tap-to-reveal and one-tap copy, live RFC 6238 TOTP codes computed locally in the browser, a light/dark theme toggle in the title bar (light by default, not persisted), a sidebar with tabs and an official-site link, and an honest "data as of" timestamp in the drawer. Entry icons mirror the desktop app's full icon set.
- **Mandatory encryption gate** — a plaintext vault refuses to export a snapshot and is guided to set a privacy lock first; every exported snapshot is encrypted, no exceptions. Two password paths: reuse the privacy-lock password, or set an independent one that Novara never stores (with a public-hosting strength warning).
- **Honest degradation** — on plain HTTP the viewer explains that secure context features are limited instead of failing as a wrong password; unsupported browsers are told their limits up front.
- **Deployment guide** — a hosted, mobile-first guide (novara.xin/snapshot-guide.html) covering local, LAN, and self-hosted public deployment, reachable from the export dialog's "Deployment Help" button.

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

- An incremental verification round (multiple automated reviewers over all post-6.1.0 changes; no critical findings) led to four fixes: pinning the only standalone entry no longer desyncs the ungrouped stack when a group is created later; reopening the group icon ring no longer keeps a stale highlight; edit-dialog backfill centering on the icon ring is no longer overridden by the initial position jump; and dialogs that are already open while the window resizes are re-clamped (Plan / Settings / File-path pages).

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

- Around 20 fixes from a final domain-wide verification, including a startup crash (`0xc000027b`) reported on some machines.

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
