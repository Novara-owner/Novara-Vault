# Changelog

<p align="center"><strong>English</strong> · <a href="CHANGELOG.zh-CN.md">简体中文</a></p>

All notable changes to Novara are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
