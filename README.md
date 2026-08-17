<h1 align="center">Novara</h1>

<p align="center"><strong>English</strong> · <a href="README.zh-CN.md">简体中文</a></p>

---

Novara is a **local-first, privacy-focused knowledge manager** for Windows, built with C# and WinUI 3. It brings memos, file paths, to-do lists, sticky notes, and rich-text records together into a single encrypted database — all stored entirely on your machine with **no cloud uploads, no telemetry, and no account registration**.

Your data lives in `%LocalAppData%\Novara\` as a portable single-file database, optionally protected by **AES-256-GCM authenticated encryption** with a password of up to 64 characters. Whether you are managing daily notes, tracking project tasks, saving frequently used paths, keeping private records, or storing API keys with built-in connectivity testing, Novara keeps everything in one fast, native, reliably offline application.

With five interface languages, light/dark/system themes, desktop sticky notes, global search, a recycle bin, and deep Windows integration, Novara is designed to be the last note-taking tool you will ever need.

<p align="center">
  <img src="images/English%20Welcome%20Page.png" alt="English Welcome Page" width="500" />
</p>

---

## What's New in 4.0

- **Drag & reorder** cards on all four pages, with the order saved
- **Desktop to-do sync** — check a to-do on a desktop note and it syncs back to the app
- **Markdown export** — export records as Markdown, with or without images
- **Upgraded API connectivity test** — vendor recognition and clearer error details
- **Data overview panel**, keyboard shortcuts, rolling backups, and a brand-design refresh

## Security & Privacy Lock

Optional password protection backed by **AES-256-GCM authenticated encryption** (since 3.0). Once enabled, your entire database is encrypted at rest — every memo, path, todo, and record entry becomes unreadable without the correct password. Passwords may be **6 to 64 characters**, never stored in plaintext (only a salted SHA-256 hash is kept locally). There is no backdoor, no recovery mechanism, and no cloud dependency — if you forget your password, the only way forward is to wipe the database and start over.

GCM adds **authenticated encryption**: any tampering with the encrypted file is detected by the cryptographic tag, so corrupted or modified data is reported instead of silently misread. Databases created by Novara 2.0 (legacy AES-CBC) are seamlessly migrated to the new format after one confirmation on first unlock — your existing data upgrades in place without loss.

Security is reinforced by a **30-minute lockout after 5 consecutive failed attempts** — the counter and lock state persist across restarts, preventing brute-force bypass. The lock screen follows your system theme, covers the entire window, and clears its input automatically when the window loses focus, protecting against shoulder-surfing in shared environments.

<p align="center">
  <img src="images/Privacy%20Lock.png" alt="Privacy Lock Settings" width="500" />
</p>

<p align="center">
  <img src="images/30-Minute%20Lock%20Page.png" alt="30-Minute Lock Page" width="500" />
</p>

## Memo Manager

The home page where all your memos live — the central hub of Novara. Organize entries into custom groups, star or pin important ones for instant access, and move entries between groups via the context menu to structure data the way you think.

Five built-in entry types — **Email, Account, API Key, Website, and Custom** — cover the majority of use cases:

- **Email** — address + password, with optional remarks
- **Account** — account name + password, with optional remarks
- **API Key** — key + endpoint URL + model ID, with optional remarks
- **Website** — name + URL, with optional remarks
- **Custom** — unlimited flexible key-value fields

Starred and pinned entries are lifted to the top for instant access, and every card displays its key information on the second line so you can identify entries at a glance.

<p align="center">
  <img src="images/Backup%20Page.png" alt="Memo Manager" width="500" />
</p>

### API Key Management & Connectivity Testing

As AI agents and LLM-powered applications become essential in daily workflows, managing API keys securely has never been more important. Novara stores your keys in an encrypted database — safe from plaintext leaks and prying eyes — but storage is only half the story.

Every API Key entry ships with a built-in connectivity test: right-click the entry, select **Test Connectivity**, and Novara probes the endpoint with an idempotent, cost-free `GET /v1/models` request. It tries Bearer-token authentication first; if the server responds with 401 or 403, it retries without Bearer, ensuring compatibility with both OpenAI-standard and domestic Chinese providers.

Results are shown in clear visual states: **Testing** (theme color), **Success** (green), **Auth Failed** (red), **No Models Endpoint** (yellow), or **Network Error** (red). Advanced configuration — OpenAI Standard (Bearer), Domestic Compatible (No Bearer), or RAW mode (testing disabled) — appears only after a failed test. Successful tests automatically save the working protocol; failed tests never modify your saved configuration.

<p align="center">
  <img src="images/API.png" alt="API Key Management" width="500" />
</p>

## File Path Manager

A dedicated page for saving and organizing file and folder paths you access frequently. Store your important locations — project directories, configuration files, log folders — and reach them instantly: one click copies the path, another opens it in Explorer with the file or folder highlighted.

When creating or editing an entry, you can either type the path or **pick it with the built-in file/folder pickers** — no manual typos, no guessing.

### Real-Time Path Validity Detection

Paths change; files get moved or deleted. Novara continuously monitors every saved path and gives instant visual feedback: a **green border** means the path exists, a **red border** means it is missing or inaccessible.

Validity checks run automatically in five scenarios: when you create or edit an entry, on a 30-minute background timer, during app startup, on a full scan from the empty area, or via right-click on an individual card. You always know, at a glance, whether your saved locations are still valid.

<p align="center">
  <img src="images/File%20Backup.png" alt="File Path Manager" width="500" />
</p>

## Task Dashboard

A lightweight productivity board that combines to-do lists, sticky notes, and timed reminders in one place — your second brain for getting things done.

### Todo Cards

Each todo card consists of a main task and optional subtasks:

- All subtasks checked → main task auto-checks → card collapses to save space
- Uncheck any subtask → main task unchecks → card expands
- Manually uncheck the main task → subtasks remain unchanged, card expands
- No subtasks → no expand button; checking the main task completes the card

All checked states persist to disk and survive app restarts.

### Sticky Notes

Quick, unstructured notes that don't fit a todo format. Content longer than 240 characters or containing line breaks shows an expand button; expanded view removes the line limit. Expand/collapse state is persisted.

### System Reminders

Right-click the empty area of the board and choose **Add Reminder** to create a countdown card on your desktop. Reminders appear as compact floating cards with a live countdown, ring three times at the due moment, pulse red, and then close automatically — even if Novara itself is not running.

<p align="center">
  <img src="images/Plan%20Page.png" alt="Task Dashboard" width="500" />
</p>

## Rich Text Records

A full-featured rich-text records editor built on WebView2 that turns journaling into a polished writing experience — personal journals, meeting notes, or detailed project documentation.

- **Formatting tools** — bold, italic, underline, font color; the floating capsule toolbar stays accessible without scrolling
- **Image insertion** — paste or upload images directly into the editor; stored base64-encoded inside the database
- **HTML import** — paste formatted content from other sources; automatically sanitized to remove scripts and dangerous attributes
- **Auto-save & data safety** — content is preserved when you navigate away or close the app; blank overwrites are blocked; HTML is double-sanitized on save and load with a strict whitelist, keeping your records safe from XSS

<p align="center">
  <img src="images/Diary%20Editor%20Page.png" alt="Rich Text Records Editor" width="500" />
</p>

## Global Search

Press **Ctrl+K** anywhere to open the global search page and find anything in one keystroke. Results from memos, file paths, todos, notes, and record entries are aggregated into a single list with source badges, sorted by recency, with matched keywords highlighted.

Click a result to jump straight to the item — Novara navigates to the correct page, scrolls the target card into view, and pulses it with a brand-colored flash so you always know where you landed. Cold-start retries guarantee the jump lands even on a freshly launched app.

<p align="center">
  <img src="images/Search%20everywhere.png" alt="Global Search" width="500" />
</p>

## Desktop Sticky Notes

Send any note card to your desktop with one click. Novara runs a dedicated lightweight host process (**StickNoteHost**) that keeps your notes visible on the desktop even when the main app is closed:

- **Drag & resize** — unlock a note to move it anywhere and resize it from any edge
- **Lock & pin** — lock a note to fix its position, keep it always on top, and survive Win+D
- **Theme & language sync** — notes follow the app's theme and language instantly
- **Edit on desktop** — right-click a note and choose Edit; Novara opens the card's editor automatically
- **Live sync** — editing or deleting a card in Novara updates or removes its desktop note in real time

<p align="center">
  <img src="images/Sticky%20Notes.png" alt="Desktop Sticky Notes" width="500" />
</p>

## Recycle Bin

Deleted cards are never lost instantly. Instead of permanent deletion, Novara moves items to a **Recycle Bin** with a **7-day auto-purge**: deleted entries are recoverable for a week, then permanently removed on the next startup.

The recycle bin supports **restore** (back to its original page) and **permanent delete** (with a red double-confirmation), a **Clear All** action, and a mixed chronological list of everything recoverable. Deleted desktop notes are removed from the desktop as well, and recycled data is excluded from exports.

## Five-Language UI

Novara ships with **five complete interface languages** — 简体中文, 繁體中文, English, 한국어, and 日本語 — with a **Follow System** option that matches your Windows language automatically.

Switching is seamless: pick a language in Settings, confirm, and the app restarts fully translated — every page, dialog, and menu, with no partial or mixed translations. The UI is powered by static resource dictionaries (no runtime XAML merging), which means zero crash risk and instant lookups. Your data is completely language-independent: only interface text is translated, so your memos, todos, and record entries stay exactly as you wrote them.

<p align="center">
  <img src="images/English%20Welcome%20Page.png" alt="English" width="240" />
  <img src="images/Chinese%20Welcome%20Page.png" alt="简体中文" width="240" />
  <img src="images/Traditional%20Chinese%20Welcome%20Page.png" alt="繁體中文" width="240" />
  <br />
  <img src="images/Korean%20Welcome%20Page.png" alt="한국어" width="240" />
  <img src="images/Japanese%20Welcome%20Page.png" alt="日本語" width="240" />
</p>

## Theme System

Novara offers three theme options — **Light, Dark, and Follow System** — with seamless switching from Settings. Every color, border, and surface is carefully tuned for a polished look in both modes: rich, comfortable dark surfaces for late-night use, and crisp, airy light surfaces for daytime productivity.

A unified button system reinforces visual clarity: primary actions use the brand blue with smooth hover/press states, secondary actions use an outlined "ghost" style, and destructive actions use red — consistent across every dialog in the application. Theme preference persists across restarts.

## Data Import & Export

Your data should move with you — freely, securely, and without friction. Novara provides full database backup and restore:

- **Exports** are always plaintext with an MD5 integrity header — human-readable, inspectable, version-compatible, with no proprietary formats or vendor lock-in
- **Import** performs thorough validation: duplicate IDs are regenerated, null partitions are normalized, orphaned references are cleaned up, and the database rolls back safely if anything fails
- For encrypted databases, both import and export require password verification
- When exporting for a new machine, file path entries are **excluded by default** (with an option to include them) — paths rarely survive a migration, so you start clean instead of red-flagging dozens of dead paths

## Startup, Tray & System Integration

Novara adapts to the way you work:

- **Auto-start with Windows** — your workspace is ready when you log in
- **System tray mode** — keep Novara running silently in the background; click the tray icon to show or hide the window
- **Global right-click menu** — right-click any folder or file in Explorer to add it to your path backups, or right-click the desktop to open Novara directly
- **Desktop reminders** — countdown cards keep working even when the app is closed

## Data & Privacy at a Glance

- **Local-first**: everything stays on your machine — no cloud, no telemetry, no account
- **Authenticated encryption**: AES-256-GCM with PBKDF2 key derivation (100,000 iterations)
- **Portable single file**: `data.novadb` holds all seven partitions; optional password protects the whole database
- **Recoverable deletes**: the recycle bin gives you a week before anything is truly gone
- **Open, honest storage**: exports are plaintext with integrity checks — your data is never locked into a format you can't read

## Privacy & Security

- **Privacy Policy** — [English](PRIVACY.md) · [简体中文](PRIVACY.zh-CN.md)
- **Security Policy** — [English](SECURITY.md) · [简体中文](SECURITY.zh-CN.md)

---

Novara is free to use and designed to stay that way. For the latest releases and the official guide, visit **novara.xin**.
