# Novara Development Manual

<p align="center"><strong>English</strong> · <a href="DEVELOPMENT.zh-CN.md">简体中文</a></p>

> This document is aimed at developers and power users, providing a complete introduction to Novara's architecture design, core mechanisms, and the concrete implementation of every page.
> Official site: <https://novara.xin> · Repository: Novara-owner/Novara-Vault

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Tech Stack](#2-tech-stack)
3. [Solution Structure](#3-solution-structure)
4. [Storage & Data Model](#4-storage--data-model)
5. [Security & Encryption](#5-security--encryption)
6. [Privacy Lock & Lock Screen](#6-privacy-lock--lock-screen)
7. [Welcome Page](#7-welcome-page)
8. [Memo Page (BasicMemoPage)](#8-memo-page-basicmemopage)
9. [File Path Backup Page (FilePathPage)](#9-file-path-backup-page-filepathpage)
10. [Plan Page (PlanPage)](#10-plan-page-planpage)
11. [Records Page & Editor (DiaryPage / DiaryEditorPage)](#11-records-page--editor-diarypage--diaryeditorpage)
12. [Settings Page (SettingsPage)](#12-settings-page-settingspage)
13. [Global Search (SearchPage)](#13-global-search-searchpage)
14. [Recycle Bin (TrashPage)](#14-recycle-bin-trashpage)
15. [Desktop Sticky Notes (StickNoteHost)](#15-desktop-sticky-notes-sticknotehost)
16. [MCP Agent Interface](#16-mcp-agent-interface)
17. [Theme & Internationalization](#17-theme--internationalization)
18. [Reminder System](#18-reminder-system)
19. [Import & Export](#19-import--export)
20. [API Connectivity Detection](#20-api-connectivity-detection)
21. [Window, Tray & Single Instance](#21-window-tray--single-instance)
22. [Core Services in Depth](#22-core-services-in-depth)
23. [Build & Release](#23-build--release)
24. [Testing](#24-testing)
25. [Directory Structure](#25-directory-structure)
26. [Version History](#26-version-history)

---

## 1. Project Overview

Novara is a **local-first, four-in-one personal knowledge manager** (a Windows desktop application built on WinUI 3).

Four tabs + a settings page + an optional privacy lock:

| Tab | Function |
|--------|------|
| **Memo** | Manages account / password / API Key / email / website / bank card / WiFi / ID / custom entries in groups; double-click to copy, star & pin, API connectivity detection |
| **File path backup** | Registers local file / folder paths, one-click existence / validity detection (green / red status), copy / open |
| **Plan** | Todo + note cards (star / pin / sort / expand); "Send to desktop" standalone sticky note; timed reminders |
| **Records** | Rich-text diary (HTML) + Markdown document dual-format editor, timeline review, filtering |

Core design principles:

- **Local-first**: all data is stored in a local single file (`%LocalAppData%\Novara\data.novadb`), no cloud sync, no telemetry.
- **Optional encryption**: AES-256-GCM + PBKDF2; the whole database is encrypted when the privacy lock is on.
- **Privacy lock**: optional; lock-screen password + lockout duration that increases after 5 failures; supports Windows Hello unlock.
- **Five languages**: Simplified Chinese / Traditional Chinese / English / 한국어 / 日本語.
- **Theme**: light / dark / follow system.
- **Desktop sticky note**: a standalone `StickNoteHost` process, linked with the main app's "Send to desktop".
- **MCP interface**: exposes a local MCP service with 14 tools for AI agents to read and write data.
- **Single-file distribution**: .NET self-contained + Inno Setup installer.

---

## 2. Tech Stack

| Layer | Technology |
|----|------|
| UI framework | WinUI 3 (Microsoft.WindowsAppSDK 1.6) |
| Runtime | .NET 8 (`net8.0-windows10.0.26100.0`, Windows 10 2004 minimum) |
| Language | C# |
| Rich-text editor | WebView2 (Tiptap rich text + markdown-it Markdown) |
| Tray | H.NotifyIcon.WinUI |
| Storage | single-file JSON (`NovaraStore`), optional AES-GCM encryption |
| Desktop sticky note | standalone `StickNoteHost` process (FileSystemWatcher sync) |
| MCP | `NovaraMCP.exe` (stdio front end) + named pipe |
| HTML parsing / sanitization | AngleSharp |
| Testing | xUnit (`Novara.Tests`, referencing the pure-logic library `Novara.Core`) |
| Build | MSBuild / `dotnet publish` self-contained; Inno Setup installer |

---

## 3. Solution Structure

The `Novara.slnx` solution contains five projects:

```
Novara/
├── Novara.csproj        main program (WinUI 3 UI layer)
├── Novara.Core/         pure-logic library (net8.0, no WinUI, referenceable by xUnit)
├── Novara.Tests/        xUnit unit tests
├── StickNoteHost/       desktop sticky note standalone process (no main window, tray-resident)
└── NovaraMCP/           MCP stdio front end (pure net8.0 console, single-file publish)
```

**Layering principle**:

- `Novara.Core` is the "pure logic" layer: Models / CryptoService / ApiProbeService / ApiChatClient / ApiDiagnoseService / RelayProbeService / ProbeDataSetLoader / PasswordService / NovaraStore / McpLogic / CsvImportExportService / Loc / CoreEnv. It has no WinUI dependency and can be unit-tested independently.
- The main project's `Services/` is the "UI-related services": StartupService / StickySync / ContextMenuService / CrashLogger / AutoBackupService / McpService / WindowsHelloService / ToastService / ReminderScheduler / ChunkedRender / HtmlSanitizer / RelayCommand, etc.
- `Pages/` contains nine pages: BasicMemoPage / FilePathPage / PlanPage / DiaryPage / DiaryEditorPage / SettingsPage / LockScreenPage / SearchPage / TrashPage.

**Core decoupling**:

- **i18n injection**: `Novara.Core` injects the main program's `App.GetString` through `Loc.T`; the logic library never depends directly on UI resources.
- **Data directory injection**: `CoreEnv.DataDirName` provides the data directory name (`Novara-Dev` for Debug, `Novara` for Release), isolating the development / release environments.

---

## 4. Storage & Data Model

### 4.1 Unified single file

All user data lives in `%LocalAppData%\Novara\data.novadb`, internally a fixed JSON structure with seven isolated partitions:

| Partition | Content | Corresponding model |
|------|------|---------|
| `DiaryItems` | Diary / Markdown documents | `DiaryEntry` |
| `PathBackupItems` | File path backup entries | `FilePathEntry` |
| `TodoCards` | Todo cards | `TodoCard` |
| `NoteCards` | Note cards | `NoteCard` |
| `MemoGroups` | Memo groups | `MemoGroup` |
| `MemoEntries` | Memo entries | `MemoEntry` |
| `AppSettings` | App settings | `AppSettings` |

**Hard constraint: cross-partition queries are forbidden** — each page reads and writes only its own partition.

### 4.2 File header format

The file header is a fixed 22 bytes:

```
[4B "NOVA"] [1B version] [1B encryption flag] [16B MD5]
```

- Plaintext body = JSON; encrypted = GZip + AES.
- After writing, sets the `Hidden | ReadOnly` attributes, protected by an exclusive lock.

### 4.3 Read/write mechanism

- **Startup**: loads the whole database into memory at once (`App.Store` static singleton).
- **Flush to disk**: every add/delete/edit triggers an immediate background asynchronous full flush — `SemaphoreSlim` serialization + latest-snapshot merge + 300ms debounce + `Task.Run`; writes use `tmp + Move` atomic replacement + `Flush(true)`.
- **Exit**: forced synchronous fallback (`SaveSync`).
- **Safety valve**: `Save` is forbidden in the encrypted / corrupted state (to prevent overwriting).
- **Startup detection**: file missing → silently create a blank database; corrupted → delete the three files and rebuild after a confirmation dialog.

### 4.4 Data model in detail

**MemoGroup (memo group)**: `Id` / `Name` / `IconKey` / `CreatedAt` / `IsStarred` / `IsPinned` / `PinnedAt` / `IsDeleted` / `DeletedAt` / `Order`.

**MemoEntry (memo entry)**: `Id` / `GroupId` (foreign key, null = ungrouped) / `Name` / `Type` / `KeyInfo` / `Fields` (list of `EntryField`) / `IconKey` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Protocol` (API detection success status).

**EntryField (entry field)**: `Label` / `Value` / `CanCopy` (whether one-click copy is allowed; true for sensitive fields).

**FilePathEntry (file path entry)**: `Id` / `Name` / `Path` / `Note` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Order`.

**TodoCard (todo)**: `Id` / `Title` / `IconKey` / `MainText` / `SubTexts` / `CheckedStates` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Order` / `ReminderAt` / `ReminderSetAt`.

**NoteCard (note)**: `Id` / `Title` / `IconKey` / `Content` / `CreatedAt` / `IsStarred` / `IsPinned` / `IsDeleted` / `Order` / `ReminderAt` / `ReminderSetAt`.

**DiaryEntry (diary / document)**: `Id` / `Title` / `Content` / `CreatedAt` / `ModifiedAt` / `IsPinned` / `PinnedAt` / `IsStarred` / `IsDeleted` / `DeletedAt` / `Order` / `Format` ("html" = rich-text diary default / "markdown" = MD document, zero migration).

**AppSettings (settings)**: theme / language / auto-start / context menu / close behavior / visible tabs / privacy lock / welcome page / backup / MCP, etc. (see 4.6).

### 4.5 Data conventions

- **Memo partition**: flat list + foreign-key relation (`MemoGroup.Id` / `MemoEntry.GroupId`, null = ungrouped), nesting forbidden. When a group is deleted, its entries have `GroupId` set to null (rescued as standalone entries).
- **Stored values are language-independent**: internal enum / status values (Type, Theme, CloseBehavior, VisibleTabs) **are persisted as Chinese literals**, with display text translated by the UI layer. Future versions must not write language-dependent literals to storage.
- **Soft delete**: all five entity types (memo entry / file path / todo / note / diary) carry `IsDeleted / DeletedAt` for soft delete, moving to the recycle bin.
- **Sorting**: todo / note / diary use the `Order` field (0 = not manually sorted, time descending; >0 = user drag-defined order).
- **New card placement**: newly created cards are always inserted at the front of the "non-pinned area" (after pinned cards, before regular cards).

---

## 5. Security & Encryption

### 5.1 Encryption algorithm

- **Encryption**: AES-256-GCM (v2). 12-byte nonce + 16-byte tag; authentication relies on the GCM tag, the MD5 field is zeroed and not verified.
- **Key derivation**: PBKDF2-SHA256, 100,000 iterations.
- **Legacy compatibility**: 2.0 legacy data used AES-CBC (v1); after unlock it is automatically migrated to v2 GCM.

### 5.2 Version matrix

| Version | Meaning |
|------|------|
| v1 plaintext | always v1 (2.0 compatible), no encryption |
| v1 CBC | 2.0 legacy encryption; a one-time migration confirmation pops up after unlock → upgrade to v2 |
| v2 GCM | the current encryption scheme |

### 5.3 Password hash stored separately

The password hash is stored in a separate plaintext small file `security.dat` (dual salt: hash salt + derivation salt, salted SHA256), **not encrypted along with the database** — because there is no readable key during the lock-screen phase. When the database is encrypted, the password hash must be independently readable to verify unlock.

### 5.4 Password-change transaction

Password change is a **transaction**: first re-encrypt `data.novadb` (`Reencrypt`) then swap `security.dat`; if either step fails, the passwords of the two files stay consistent. Forgetting the password = wipe all data and rebuild an empty database, no backdoor.

---

## 6. Privacy Lock & Lock Screen

### 6.1 Startup verification flow

```
Read encryption flag
  ├─ plaintext → load directly → enter main UI
  └─ encrypted → read security.dat → show lock screen (follows system theme)
           → verify password → derive key, decrypt & load → read AppSettings for global refresh → enter main UI
```

### 6.2 Lock screen (LockScreenPage) implementation

**Layout** (vertically centered StackPanel):

- "Privacy Lock" title (theme color, follows the theme).
- Password input (a single PasswordBox, long password, custom template that vertically centers the text + Reveal eye + placeholder).
- "Unlock with Windows Hello" text (shown only when Hello is enabled, brand color, clickable, breathing animation).
- Unlock button (circular brand-blue button with an arrow icon).
- "Forgot password" text (at the very bottom of the page, 30px from the bottom).

**Core logic**:

- **Password submit**: Enter / unlock button → `VerifyAsync` → `LoadWithPassword`.
- **Auto-probe**: at 6 characters entered, debounce 400ms and try automatically; capped at 64 attempts per run; probe failures do not count, only explicit submit counts.
- **Error feedback**: wrong → red flash + shake (`ShakeAndFlashAsync`); correct → green frame + content fly-out animation (`PlayExitAnimation`: title moves left -240, input box moves right +240, Windows Hello text moves left, "forgot password" moves down).
- **5-attempt lockout**: 5 consecutive failures → red countdown breathing (30 minutes, persisted in `lockout.dat`, restored across restarts).
- **Clear password on window blur** (anti-shoulder-surfing, rule G13).
- **Windows Hello unlock**: click "Unlock with Windows Hello" → `RequestVerificationAsync` pops the native Hello → `PasswordVault` reads the password back → `LoadWithPassword` reuses the unlock path. On failure, reuse `ShakeAndFlashAsync`. When enabled, auto-attempts once (1s delay, waiting for the entrance animation).
- **Forgot password**: confirmation dialog → clear the three files → rebuild a blank plaintext database.

---

## 7. Welcome Page

The welcome page is a full-screen overlay shown on first launch (or when "show welcome page" is enabled).

**Layout**:

- Logo (brand image, centered).
- "Novara" title (two layers: shadow text + main text, Century Gothic 80px).
- Subtitle (brand tagline, wide letter spacing).
- "Click anywhere to start" hint (breathing animation).

**Animation**:

- Entrance animation: logo fades in, title slides in from the left, subtitle slides in from the right, hint fades in (staggered BeginTime).
- Hint breathing: Opacity 0.3↔0.7 loop.
- Fade-out animation: on click, the overlay fades out linearly over 400ms, then stops the breathing animation → enters the main content → the navigation bar fades in.

**Responsive**: `UpdateWelcomeLayout` scales each element's size according to the window height + uses Transform for positioning (`WelcomeLogoTransform.Y = -0.2875 * h`, etc.).

**Display logic**:

- `WelcomeOnLaunch = true`: show on every double-click launch.
- `WelcomeOnLaunch = false` + `HasCompletedWelcome = true`: skip the welcome page and go straight to the main content.
- Clicking the welcome page writes `HasCompletedWelcome = true` and flushes to disk synchronously.

---

## 8. Memo Page (BasicMemoPage)

Tab 1, manages memo groups and entries.

### 8.1 Features

- Group management + entry management + search + dialog editing + API connectivity detection.
- Group / entry star & pin corner badges (on the corresponding card only); pinned cards come first.
- Double-click an entry to copy the sensitive field.
- Group deletion: entries in the group are rescued as standalone entries (GroupId=null).
- The ungrouped card is always at the bottom; group cards never appear below it.

### 8.2 Entry types and fields

| Type | Required fields | Other fields |
|------|---------|---------|
| Email | address | password / note |
| Account | account name | password / URL / note |
| API Key | name | Key / URL / model ID / note |
| Website | URL | account / password / note |
| Bank Card | card number | cardholder / expiry / CVV / password / note |
| WiFi | network name | password / note |
| ID | ID number | name / issuing authority / validity / note |
| Custom | name | free-form fields |

- Form slots `FormField2~7` (7 rows).
- Edit backfill maps by **field label** (`FieldSlotFor(type, label)`) rather than position, compatible with legacy data.
- A Custom type with no icon explicitly chosen gets a **randomly assigned** Group icon (does not reuse the old puzzle default).
- Sensitive fields `CanCopy = true` (card number / CVV / password / network name / ID number / account / URL, etc.).

### 8.3 Core data structures

- `_groupIds` / `_entryIds`: Border → Guid mapping (stable in-memory keys; GUID for persistence).
- `_starredCards` / `_pinnedGroupCard` / `_pinnedEntryCard`: star / pin state.
- `_standaloneEntries` / `_entriesInGroup`: ungrouped entries / entries in group lists.
- `_targetGroupCard` / `_pendingMoveEntry` / `_editingEntryCard`: dialog operation markers (Hide completion + Unloaded double cleanup).

### 8.4 Interaction rules

- Dialog open: click the transparent scrim to close; clicks inside the dialog do not close it.
- API Key card: the second line shows the URL; right-click includes "Detect connectivity".
- Move in / out: `MenuFlyoutSubItem` second-level submenu (group icon + ungrouped).
- Drag sorting: ghost copy follows the pointer, the real card stays put (stable version, see 10.5).
- New entry placement: after pinned cards, before the ungrouped card (inserted at the front within the group).

---

## 9. File Path Backup Page (FilePathPage)

Tab 2, registers local file / folder paths.

### 9.1 Features

- Path card: name / path / note three lines.
- Path validity detection: green (`#4CAF50`) / red (`#FF4545`) border.
- Five detection triggers: create / edit validation, 30-minute timer, startup detection, full scan from the empty area, single-card right-click detection.
- Copy path Toast "Copied" disappears in 2 seconds; one-click open with `explorer /select` to highlight.
- New / edit dialog has "Select file / Select folder" dual buttons (Picker, `InitializeWithWindow` binds the main window handle).
- Path input auto-strips quotes; invalid / blank is rejected on create.
- Pinned single card first; drag sorting.

### 9.2 Core implementation

- `_autoCheckTimer`: 30-minute DispatcherTimer full detection.
- `_cardBaseBorderColor`: card base border color (used to restore on hover).
- Card hover: only that card's border changes color + lifts up (independent brush copy, does not contaminate other cards).
- Drag sorting: ghost copy (same stable version as the plan page).

---

## 10. Plan Page (PlanPage)

Tab 3, todo + note cards.

### 10.1 Todo cards

**Card structure**: title row (icon + title + pin + star + completion badge + expand button) + body (main todo row + sub-todo rows).

**Check linkage** (the single linkage trigger point: sub-todo check state change):

- All sub-todos checked → main auto-checks + collapses.
- After all checked, uncheck any sub → main unchecks in sync + force expand.
- Manually uncheck main → does not write back to subs (P5).
- Completion determination = all entries checked.

**Collapse rules**:

- Has unchecked entries → force expand.
- All checked & complete → collapse by default; can manually expand.
- Cards without sub-todos: no expand button; checking = complete = collapse.

**Completion badge** (three-color): when all items are checked, the title row shows a three-color "complete" badge (light brand-blue `#8C93FF` flag + brand-blue `#7276FF` flag + white check, 3 Path raw coordinates stacked + Viewbox scaling).

**Edit-rebuild migration**: creation time preserved as-is; star / pin preserved; check states inherited by per-line content comparison (main text changed → reset only main, a sub changed → reset only that row, new → unchecked by default).

### 10.2 Note cards

- Content too long (>240 chars) or containing line breaks → expand button; the expanded state `IsExpanded` is persisted.
- Context menu order: star → pin → send/unsend to desktop → edit → separator → delete.

### 10.3 Timed reminder

Todo / note card right-click "Set reminder" → card border gradient (green→red HSV interpolation) + system-level reminder + Toast + desktop reminder card (see 18).

### 10.4 Filtering & sorting

- Filter: mixed / todo / note (real filter by Visibility; drag disabled in the filtered view).
- Drag sorting: `Order` field persisted.
- New card placement: front of the non-pinned area (`AssignNewCardOrder`).

### 10.5 Card drag sorting (stable version reused across four pages)

- The real card stays in the list (brand-blue border + Opacity 0.35).
- The pointer-following is a ghost copy (RenderTargetBitmap screenshot Image, on an overlay Canvas with IsHitTestVisible=False).
- Drag animation is manual per-frame interpolation (async Task.Delay), Storyboard disabled.
- Auto-scroll uses `CompositionTarget.Rendering`.
- Drop constraint: pinned cards cannot be dragged; reset `_dropIndex` on the original position.

---

## 11. Records Page & Editor (DiaryPage / DiaryEditorPage)

Tab 4 ("Records"), hosting "HTML rich-text diary" + "Markdown document" dual formats.

### 11.1 List page (DiaryPage)

- Sorting: pinned first + modified time descending.
- Star / pin toggle is an incremental update (no list rebuild).
- MD document cards get a gold "document" badge (16px); HTML diaries get a "diary" badge.
- Filter: mixed / diary / document (integrated into the "Records" tab arrow, real filter, drag disabled in the filtered view).
- New entry: right-click the empty area → new document / new diary / import document.
- Export: right-click "Export ▸" submenu — HTML diary "Export MD (with images) / (without images)", MD document "Export Markdown (original)".

### 11.2 Editor (DiaryEditorPage)

**Editor split** (both hosted by WebView2):

- `format=html` → WebView2 + Tiptap rich-text editor (verbatim, untouched).
- `format=markdown` → WebView2 + markdown-it rendering (`html:false` against XSS), GitHub-style Write ↔ Preview toggle.

**HTML rich-text toolbar**: bold / italic / underline / strikethrough / color / alignment / heading / list / quote / link / code block / image insertion, etc. (floating capsule toolbar).

**MD toolbar** (16 buttons): headings H1-H4 / bold / italic / list / quote / link / code block / inline code / divider / table / clear formatting / undo / redo + Write/Preview toggle.

**Security**:

- HTML save / load is **double-sanitized** (`HtmlSanitizer` whitelist: b/strong/i/em/u/s/span/font/div/br/p/img/ul/ol/li/a/pre/code/hr/blockquote/h1-h6; strips all `on*` event attributes including entity-encoded variants, `javascript:` / `vbscript:` / non-image `data:` protocols).
- Titles are rendered as textContent plain text (against element-ification / scripting); capped at 120 characters (whitespace-trimmed, JS keydown/paste interception + C# fallback).
- Single-entry total image size cap of 20MB (defense against base64 inline-storage bloat).

**Save guards**:

- `_webViewReady=false` blocks saving (against content overwrite).
- JS execution failure returns null and abandons the save (against blank overwrite).
- On exit, save the editor's dirty content (Closing first cancels, then async-saves, then releases).
- Dirty check: title / body snapshot comparison; unchanged → no save / no "modified" prompt.

---

## 12. Settings Page (SettingsPage)

The settings page is a card-based layout, with these main cards:

| Card | Function |
|------|------|
| Data overview | entries / groups / todo completion rate / storage usage (off by default; default-expanded once enabled) |
| MCP interface | master switch + authorization list + configuration (see 16) |
| Display mode (theme) | light / dark / follow system (storage + restart loop) |
| Language | five-language switch (storage + restart loop) |
| Custom tabs | check/uncheck the four tabs (keep at least 1) |
| Start on boot | registry HKCU Run key (shows the real state) |
| Global context menu | desktop / file context menu switch |
| Window exit behavior | exit directly / tray-resident |
| Privacy lock | set password / change password / turn off lock / warning — four dialogs |
| Data backup | snapshot / restore / auto backup |
| Data archive & restore | import / export (native / CSV / MD / HTML / PDF) |
| Reset vault | high-risk confirmation → delete three files & rebuild |
| Official site | open novara.xin |

**Privacy lock four dialogs**:

- A set password: 6 characters entered twice consistently → write security.dat → encrypt in memory & flush immediately.
- B change password: verify the old password → **password-change transaction** (Reencrypt first, then swap security.dat).
- C turn off lock: decrypt to plaintext & write → delete security.dat.
- D warning: red text + red three-state confirmation button.

**Import/export**: export is always plaintext (with MD5 header); with a lock, both require password verification; import does not overwrite security.dat / lockout.dat; after import, ReloadPages + prompt to restart for language/theme changes to take effect.

---

## 13. Global Search (SearchPage)

Global search (Ctrl+K) covers five entity types: memo / file path / todo / note / diary.

### 13.1 Indexing

When the search page opens, an index is prebuilt once (`RebuildIndex`, lowercasing the searchable text of all non-soft-deleted entities); each subsequent Enter only traverses the index for matches, no more on-the-fly string concatenation.

### 13.2 Fuzzy search

- The query is tokenized by spaces; all words must match (AND) (`MatchesQuery`).
- Within a word, exact substring first, then **subsequence fuzzy** (`ContainsFuzzy`, `memo` hits `memorandum`, Chinese matches per character).

### 13.3 Sorting & rendering

- Relevance sorting: title hits first, within a group time descending.
- Chunked rendering: `ChunkedRender` splits into frames (8 cards per batch) to avoid one-shot reparent + animation jank.
- Source label: a brand-color small label at the card's top-right (memo / path / plan / diary / document).
- Source filter: mixed / memo / file / plan / diary / document.
- Click a result to jump + target card flash (scale pulse + brand-color border flash + restore the original color).

---

## 14. Recycle Bin (TrashPage)

- Five entity types carry `IsDeleted / DeletedAt` for soft delete.
- TrashPage is an independent full page: mixed sorting (by deletion time) / restore / permanent delete / clear all.
- Each row's card has two buttons on the right: "Restore" (brand-blue border) / "Permanent delete" (red border).
- 7-day startup auto-cleanup of expired soft deletes (`TrashPage.CleanupExpired`).
- Groups do not enter the recycle bin (physically deleted, entries rescued to ungrouped first).
- Restore / export never include soft-deleted data.
- Restore clears the star / pin state (does not inherit relations).

---

## 15. Desktop Sticky Notes (StickNoteHost)

### 15.1 Architecture

`StickNoteHost.exe` is a **standalone resident process** (no main window, tray-resident). The main program only writes `stickies.json` + launches the Host.

```
stickies.json (data directory, avoids data.novadb's exclusive lock):
{
  theme, language,
  notes: [{ id, kind, title, content, dueTime, items }]
}
```

- `id` = the database `NoteCard.Id`.
- `kind` = note / todo / reminder.
- `items` = todo sub-items (`StickyTodoItem{ label, checked }`, carried when `kind=todo`, bidirectional sync).

### 15.2 Sync mechanism

- **Main program → Host**: add/delete/edit → write `stickies.json` → Host `FileSystemWatcher` listens (callback on a worker thread, must capture the UI thread's DispatcherQueue) → refresh the windows.
- **Content-driven lifecycle**: json has cards → auto-launch shows cards; all empty → auto-exit.
- **Host → main program** (reverse): todo checks write back to `stickies.json` (300ms debounce), the main program's watcher syncs the card updates.

### 15.3 Sticky note interaction

- Whole-card drag + 8px edge resize (pure XAML pointer events + `SetWindowPos`, **system drag loop disabled** — the compositor swallows messages).
- Lock & pin (TOPMOST, SetWindowPos refresh reinforcement in Activated).
- Borderless (`IsResizable=false`); invisible in Alt+Tab (`TOOLWINDOW`).
- Theme / language follow: `stickies.json.theme/language` snapshot + watcher; the Host never restarts due to a theme change.
- Edit-jump IPC: Host right-click "Edit" → write pending-edit.json + named event `Local\Novara.EditRequest(.Dev)` → the main program switches to the plan page and opens the edit dialog (auto-starts if not running, cold-start retry ~4s).

### 15.4 Reminder card

`kind=reminder + dueTime`; 300×180 appears at the bottom-right stacking leftward; countdown red text ticks every second; on due `MessageBeep×3` + Toast + manual pulse turns red → auto-close deletes data.

### 15.5 Card visuals

- Faint border (theme border color, 1px, consistent with main-program cards).
- Title brand-blue (note / todo cards, keeps the brand element).
- Todo card checkbox: checked brand-blue + white check, unchecked rounded faint border.

---

## 16. MCP Agent Interface

Novara provides a local MCP service so any MCP client (AI agent) can natively discover and call tools to read and write Novara data.

### 16.1 Architecture

```
MCP client (stdio)
    ↓ JSON-RPC
NovaraMCP.exe (pure forwarding stdio front end, hand-written JSON-RPC: initialize / tools/list / tools/call / ping)
    ↓ named pipe Novara.Mcp
Main process McpService (unlock gate → token auth → process whitelist → redaction → CRUD)
```

### 16.2 Tool list (14 tools)

| Category | Tools |
|------|------|
| Create | create_memo / create_todo / create_note / create_diary / create_path |
| Update | update_memo / update_todo / update_note / update_diary / update_path |
| Query | list_items / read_item / search_items |
| Delete | delete_item (soft delete, off by default, must be enabled in settings) |

### 16.3 Security model

- **Privacy lock gate**: all requests are rejected while the database is locked.
- **Token auth**: the master switch is off by default; enabling it auto-generates a token (Base64Url 32B).
- **Process whitelist**: the first connection pops a confirmation; the path is recorded in the whitelist.
- **Redaction**: sensitive fields (password / API Key) are redacted in output; `update` cannot tamper with existing sensitive fields, but can add new sensitive fields.
- **Write boundary**: `create` / `update` / `delete` (soft delete); the recycle bin is not exposed; `format` cannot be changed.

### 16.4 Settings page MCP card

Title + collapse / configure / master switch three buttons + authorization list panel (empty state with a centered hint, non-empty follows the content, each row = path + red revoke). Four dialogs: configure (Key reset / JSON copy / delete-permission dropdown) / reset confirm / JSON select (JSON / prompt) / delete-permission confirm.

---

## 17. Theme & Internationalization

### 17.1 Theme

- Three themes: follow system / dark / light.
- Switching goes through a "store + restart" loop (**no runtime hot-swap**).
- Dynamic color must go through `App.GetBrush(key)` (manually selects a dictionary by program theme); direct indexing of `Application.Current.Resources` is forbidden; caching Brush references is forbidden.
- Unified brand color `#7276FF` (`AppPrimaryButtonBrush` / `AppAccentBrush` / `AppDialogBorderBrush` same value in the three theme dictionaries).
- Three-tier button system: primary action blue (`#7276FF` → hover `#8C93FF` → pressed `#5855FF`), secondary outline, destructive red (`#CCFF4545` → `#FFFF4545` → `#CC3A3A`).
- Dialog buttons come in only three kinds: cancel (outline) / confirm blue / confirm red.
- Follow-system real-time linkage: `UISettings.ColorValuesChanged` + 500ms debounce + UiQueue marshal.

### 17.2 Internationalization (i18n)

- **C# static dictionary `AppResources.cs`** (5 languages, 5 dictionaries, keys fully aligned).
- `x:Bind` static method binding; MainWindow (does not support x:Bind) uses the Chinese original text + `Tag="Key"` runtime ApplyLocalizedTexts.
- Switch loop: settings language dropdown → persist → dialog confirm → restart to take effect (**no hot-swap**).
- Exception fallback: config corrupted → system language → Chinese; key missing → return the key placeholder, no crash.
- Follow system: `AppLanguage` empty = follow system, reads `GlobalizationPreferences.Languages[0]` mapping (zh-Hant/TW/HK/MO→zh-TW, zh-*→zh-CN, en→en-US, ko→ko-KR, ja→ja-JP, other→en-US fallback).
- English layout constraint: automatic width adaptation, no fixed hard widths, long text TextWrapping.

---

## 18. Reminder System

Three-layer reminder:

| Layer | Mechanism |
|----|------|
| In-app | todo / note card right-click "Set reminder", card border gradient (green `#00CC22` → red `#DD2222`, HSV continuous interpolation), popup on due |
| System-level | `ReminderScheduler` registers a one-shot task via `schtasks` (`/sd` date `yyyy/MM/dd`), at due time launches `Novara.exe --reminder <id>` |
| Toast | `ToastService` sends `AppNotification` (with a system sound), unpackaged front AUMID shortcut |

Data: `TodoCard` / `NoteCard` add `ReminderAt` (due time) + `ReminderSetAt` (set time).

- Border gradient: progress `p = (Now - ReminderSetAt) / (ReminderAt - ReminderSetAt)` clamped to 0~1, HSV continuous interpolation.
- Due popup: card content + "Got it", pops once only; click clears fields + deletes the task + restores.
- A running reminder is forwarded via the single-instance Mutex + `ReminderDueRequest` (pending json + named event); if not running, launch and parse the argument to show the reminder.
- Due already passed before boot → silently dropped (by design).
- Desktop reminder cards also hook Toast + `MessageBeep×3` + auto-close & delete on due.

---

## 19. Import & Export

| Format | Purpose | Notes |
|------|------|------|
| Native backup (.novabak) | full backup / restore | always plaintext (with MD5 header); with a lock, password verification required; import validates + rollback + Id dedup |
| CSV | memo import / export | compatible with KeePass / Bitwarden, three dialects, auto-detected |
| Markdown | summary / single entry / document original | read-only, cannot be imported |
| HTML collection | records page collection | brand logo + official site + tagline + print-friendly CSS |
| PDF collection | records page collection | offscreen WebView2 `PrintToPdfAsync` silent PDF conversion (A4) |

- Export is always plaintext; with a lock, password verification is required.
- Import does not overwrite `security.dat` / `lockout.dat`.
- Privacy lock change-password / turn-off-lock / forgot-password / reset / import / corruption-rebuild all add/remove the Windows Hello credential in sync.
- CSV export flow: double-confirmation dialog → FileSavePicker(.csv) → UTF-8 BOM write.
- CSV import flow: FileOpenPicker(.csv) → auto-detect the dialect → preview dialog → import (all to ungrouped, no automatic group creation).

---

## 20. API Three-Tier Detection

Memo-page API Key entries expose a three-tier detection system via right-click. Core logic lives entirely in `Novara.Core/Services/` (pure, unit-testable): `ApiProbeService` (tier 1, original) / `ApiChatClient` (shared chat client) / `ApiDiagnoseService` (tier 2) / `RelayProbeService` (tier 3) / `ProbeDataSetLoader` (probe dataset).

| Tier | Service | Cost | Purpose |
|------|---------|------|---------|
| Connectivity test | ApiProbeService (original) | 0 | Connectivity / key validation |
| Status diagnosis | ApiDiagnoseService | 1–3 requests | Reachability + balance presence (inferred) + metadata + latency/TTFT |
| Relay probe | RelayProbeService | 10+ requests | 8 probes + weighted score + 4-tier verdict |

- **Vendor identification**: dispatches templates by URL host keywords (OpenAI / OpenRouter / Anthropic / Gemini / Azure / Groq / Together / Perplexity / Zhipu / Qwen / DeepSeek / Moonshot / Mistral / SiliconFlow / StepFun / Xiaomi MiMo / Ollama + generic fallback).
- **Auth styles**: bearer / x-api-key / api-key / query / raw. Xiaomi MiMo uses an `api-key` header instead of a standard Bearer. The generic fallback chain is Bearer → bare key × multiple endpoints.
- **9 statuses**: success / wrong key / no quota / rate-limited / no endpoint / permission / server error / network error / unknown.

### Tier 2: Status diagnosis
- A reachability gate plus balance inference (error codes + message keywords such as `insufficient/quota/billing/balance/credit/arrears`), metadata, and latency/TTFT (forced streaming).
- Chat endpoint derivation (never hardcode `/v1`): models-endpoint parent + `/chat/completions`. Vendor prefixes differ (Zhipu `/api/paas/v4`, Qwen `/compatible-mode/v1`, Groq `/openai/v1`; Ollama and Azure OpenAI are handled specially).

### Tier 3: Relay probe (8 probes + scoring)
- 8 probes: identity (response-metadata `model` field — never ask "who are you"), capability benchmark (question set), format compliance, token billing, tool calling, hidden injection, response poisoning, long-context truncation.
- Weighted scoring (identity 0.20 / capability 0.20 / tool 0.15 / billing 0.15 / injection 0.10 / poisoning 0.10 / format 0.05 / truncation 0.05), skipped probes excluded and re-normalized; 4-tier verdict: trusted ≥90 / mostly-trusted ≥75 / suspicious ≥50 / high-risk <50.
- Anti-cheat: random nonce, a 2000 completion-token budget, 90s global timeout, per-probe fault tolerance, URL allowlist (anti-SSRF), and small-model recognition (capability/tool FAILs downgrade to WARN).
- Data-driven: probe questions and poisoning regexes live in `ProbeDataSet.json` (replaced at startup, no hot reload), with a built-in fallback.

### Security & logging
- `AllowAutoRedirect=false` against key cross-domain leak; key masked + error body redacted; `ConnectTimeout=5s`.
- Diagnostic log: `ApiChatClient` writes each chat to `%LocalAppData%\Novara\relay-probe.log` (timestamp / URL / status / usage / truncated body).

---

## 21. Window, Tray & Single Instance

- Default "exit directly"; tray-resident is optional (settings page dropdown).
- `AppWindow.Closing` interception: resident → Cancel + Hide; exit directly → save editor dirty content + SaveSync then release.
- **Single-instance mutex**: named Mutex (`Local\Novara.SingleInstance(.Dev)`) + FindWindow wake by title, the second instance exits silently; MCP background mode with no window sends a `ShowWindowRequest` wake signal.
- Context menu (desktop "Open Novara" + file / folder "Add to file path backup"): HKCU\Software\Classes user-level registry keys.
- Start on boot: HKCU Run key (triple validation: value exists + env vars expanded + exe exists; path quoted).
- Permanently removed from the settings page: change storage path / open storage directory / vault path migration (no underlying file entry points exposed).

---

## 22. Core Services in Depth

### Novara.Core (pure logic)

- **NovaraStore**: `Load` / `LoadWithPassword` / `SaveAsync` (SemaphoreSlim serialization + snapshot merge + 300ms debounce) / `SaveSync` / `EnableEncryption` / `DisableEncryption` / `Reencrypt` (rollback on failure) / `ExportBackup` (always plaintext + soft-delete filter) / `ImportBackup` (validate + rollback + normalize + Id dedup) / `ResetDatabase`; writes use tmp+Move atomic replacement + Flush(true).
- **CryptoService**: `Encrypt` (v1 CBC) / `Decrypt` + `EncryptGcm` / `DecryptGcm` (v2); PBKDF2-SHA256 100,000 iterations.
- **PasswordService**: `security.dat` dual salt + salted SHA256 constant-time comparison; `lockout.dat` (FailCount/Until/Enabled); `SetBaseDir` path injection.
- **ApiProbeService**: tier 1 — vendor identification + protocol matrix + error classification + redaction (see 20).
- **ApiChatClient**: shared chat client (OpenAI/Anthropic/Gemini protocols + streaming + usage/TTFT + chat-endpoint derivation).
- **ApiDiagnoseService**: tier 2 — reachability + balance inference + metadata + latency (see 20).
- **RelayProbeService**: tier 3 — 8 probes + weighted scoring + 4-tier verdict + anti-cheat (see 20).
- **ProbeDataSetLoader**: probe dataset loading + validation + built-in fallback.
- **McpLogic**: 14-tool pure logic (token auth / redaction / CRUD), unit-testable.
- **CsvImportExportService**: export + parse (auto-detect three dialect headers + build fields by type template).
- **Loc**: i18n injection delegate.
- **CoreEnv**: data directory name (Debug/Release isolation).

### Services (UI-related)

- **StartupService**: registry HKCU Run key Enable/Disable/IsEnabled (triple validation).
- **StickySync**: `stickies.json` read/write (tmp+Move atomic + named mutex); `FindHostExe` (release = same directory; development = ascend up to bin); theme / language / send-to-desktop / clear; reverse-channel FileSystemWatcher.
- **ContextMenuService**: context menu registration (desktop + file/folder).
- **CrashLogger**: three-source exception logging (AppDomain/TaskScheduler/Application → logs\crash-*.txt, rolling 10 files + redaction).
- **AutoBackupService**: rolling snapshots + restore + periodic timer (byte-copies data.novadb).
- **McpService**: named pipe + auth + dispatch + unlock gate.
- **WindowsHelloService**: PasswordVault store/retrieve (resource="Novara"/userName="winhello") + UserConsentVerifier verification.
- **ToastService**: AppNotification Toast (unpackaged front AUMID + Register).
- **ReminderScheduler**: schtasks one-shot task registration.
- **ChunkedRender**: chunked rendering helper (DispatcherQueue low-priority per-frame).
- **HtmlSanitizer**: XSS whitelist sanitizer (extracted from DiaryEditorPage, reused by HTML export / MCP format=html).
- **RelayCommand**: minimal ICommand implementation (tray command binding).
- **EditRequest / AddPathRequest / ReminderEditRequest / ReminderDueRequest / ShowWindowRequest**: IPC requests (pending json + named event).

---

## 23. Build & Release

Release chain:

```
dotnet publish -r win-x64 --self-contained
  → manually add Novara.pri (without it, the app crashes with 0xc000027b on launch)
  → Inno Setup compile Installer\setup.iss
```

Key pitfalls (must follow):

- **Novara.pri**: must be manually added after publish; without it, the app crashes with `0xc000027b` on launch.
- **StickNoteHost independent publish**: the Host is `EnableMsixTooling=true`, and its publish produces `resources.pri` (MSIX resource). If the Host is published directly into the main program's publish directory, `resources.pri` interferes with XAML loading → `0xc000027b` on launch. The correct way: publish the Host to a separate temporary directory and copy only `StickNoteHost.exe`. `resources.pri` must never appear in the publish directory.
- **NovaraMCP independent publish**: a pure net8.0 console, `PublishSingleFile=true` + `SelfContained=true`, publish produces a single self-contained exe (no `resources.pri`). Copy only `NovaraMCP.exe` to the main program's publish directory.
- Uninstaller: delete the auto-start registry entry + before deleting `data.novadb`, first clear the Hidden|ReadOnly attributes.
- Data retention semantics: choosing "keep data" on uninstall leaves `%LocalAppData%\Novara` as-is; reinstall restores automatically.
- After clearing bin/obj, the first compile requires `dotnet restore` first (otherwise NETSDK1004).

---

## 24. Testing

`Novara.Tests` (xUnit) references `Novara.Core`, covering:

- Storage: zero-migration contract (Format field), encryption / decryption round-trip, corruption detection, import rollback.
- Security: password hash, lockout counting, password-change transaction.
- API probe: vendor identification, protocol matrix, error classification.
- MCP logic: token auth, CRUD, sensitive-field redaction.
- CSV: three-dialect parsing, field mapping.

The pure-logic layer (`Novara.Core`) has no WinUI dependency, guaranteeing independent unit testing.

---

## 25. Directory Structure

```
Novara/
├── App.xaml / .cs             entry: MainWindow singleton, GetString/GetBrush/CreateGeometry, single instance, UiQueue
├── MainWindow.xaml / .cs      main window: title bar + 4-page navigation + welcome page; tray/restart/close exit; IPC handler
├── IconData.cs                all SVG icon paths + GroupIconKeysInOrder selector order
├── AppResources.cs            5-language static dictionary (i18n)
├── Themes/
│   ├── DesignSystem.xaml      colors/radius/spacing/fonts (theme dictionary)
│   └── Controls.xaml          common control styles (buttons / GlassMenuFlyout, etc.)
├── Novara.Core/
│   ├── Models/                MemoGroup / MemoEntry / EntryField / FilePathEntry / TodoCard /
│   │                          NoteCard / DiaryEntry / AppSettings / NovaraDatabase
│   └── Services/              NovaraStore / CryptoService / ApiProbeService / ApiChatClient /
│                              ApiDiagnoseService / RelayProbeService / ProbeDataSetLoader /
│                              PasswordService / McpLogic / CsvImportExportService / Loc / CoreEnv
├── Novara.Tests/              xUnit unit tests
├── Services/                  UI services: StartupService / StickySync / ContextMenuService / CrashLogger /
│                              AutoBackupService / McpService / WindowsHelloService / ToastService /
│                              ReminderScheduler / ChunkedRender / HtmlSanitizer / RelayCommand, etc.
├── Pages/
│   ├── BasicMemoPage         memo page
│   ├── FilePathPage          file path backup page
│   ├── PlanPage              plan page (todo + note)
│   ├── DiaryPage             records list page
│   ├── DiaryEditorPage       records editor (WebView2 rich text + Markdown)
│   ├── SettingsPage          settings page
│   ├── LockScreenPage        lock screen page
│   ├── SearchPage            global search page
│   └── TrashPage             recycle bin page
├── StickNoteHost/             desktop sticky note standalone process
├── NovaraMCP/                 MCP stdio front end (single-file publish)
├── DiaryEditorJs/             WebView2 editor bundle (Tiptap + markdown-it)
├── Installer/                 Inno Setup install script
└── Assets/                    icons (ico / png / svg logo)
```

---

## 26. Version History

| Version | Date | Content |
|------|------|------|
| 2.0 | 2026-08-07 | Four tabs + settings page + privacy lock; encrypted storage / tray / theme / bilingual |
| 3.0 | 2026-08-12 | Desktop sticky notes, global search (Ctrl+K), AES-256-GCM upgrade + long passwords, five languages, card recycle bin, system-level reminders, global context menu |
| 4.0 | 2026-08-18 | Card drag sorting, todo desktop bidirectional sync, diary MD export, API detection upgrade, chunked rendering / crash log / data backup / write debounce, unit-test engineering |
| 5.0 | 2026-08-19 | Records page repositioning (Format field + MD editor), MCP Agent interface (14 tools), Windows Hello unlock (project completion) |

---

> Novara is designed around "local-first, simple, and private". All data belongs to the user and never leaves the machine.
