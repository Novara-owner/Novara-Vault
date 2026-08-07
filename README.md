# Novara
Novara is a local all-in-one memo application built with C# / WinUI 3. It integrates memos, file paths, to-do lists, sticky notes and rich-text diaries into a single encrypted database. All your data is stored entirely on your local machine.

No cloud servers. No telemetry. Your data stays under your full control.

## Features
### Privacy Lock
Optional 6-digit numeric PIN protection powered by AES256-GCM encryption. Once enabled, all database content is encrypted at rest.
5 consecutive wrong password attempts trigger a 30-minute lockout. The failure counter and lock timer persist across program restarts and cannot be bypassed.
There is no password recovery backdoor. If you forget your PIN, the only solution is to wipe the entire database and rebuild it empty.

### Memo Manager
Supports group classification, star/pin priority marking and full-text search.
Dedicated API Key entry type built-in, with connectivity detection compatible with standard OpenAI interfaces.

### File Path Manager
Store and categorize local files & folders. Visual green/red border indicators show path validity.
One-click copy full path and one-click open folder with file highlighted in File Explorer.

### Task Dashboard
Hierarchical to-do cards with automatic completion logic for subtasks.
Sticky notes support expand/collapse for long text. All tick states are persisted permanently.

### Rich Text Diary
WebView2-based WYSIWYG editor, supporting bold, italic, underline and image insertion.
Built-in strict HTML sanitization to eliminate XSS risks during both save and load operations.

### Dual-Language UI
Fully localized Simplified Chinese / English interface.
Static resource dictionary architecture, no runtime XAML merging to avoid program crashes. Language changes take effect after restart.

### Theme System
Three display modes: Dark, Light, Follow System. User theme preferences are saved persistently.

### Data Import & Export
Complete database backup and recovery. All exported files are plaintext for cross-device compatibility.
Imported data is auto-sanitized and merged without overwriting local security files.

### System Tray
Two window close modes available: Minimize to tray or Exit directly.

### Auto Startup
Optional auto-launch with Windows, implemented via registry entries.

## Tech Stack
- Language: C# / XAML
- UI Framework: WinUI 3 / Windows App SDK 1.6
- Runtime: .NET 8 LTS
- Target OS: Windows 10 (Build 19041, 2004) & Windows 11
- Deployment: Self-contained publish + Inno Setup installer
- Storage: Single-file JSON database with optional AES256-GZip encryption
- Rich Text Engine: WebView2 (Edge Chromium)

## Installation Guide
Download the latest installer from the Releases page.
1. Run `Novara-Setup.exe`
2. Follow the setup wizard
3. Launch Novara from desktop shortcut or Start Menu

### System Requirements
- Windows 10 Version 2004 (Build 19041) or newer / Windows 11
- WebView2 Runtime (will auto-install if missing)
- .NET 8 Runtime (bundled inside self-contained package)

## Local Data Directory
All user data is stored in this local path:# Novara
Novara is a local all-in-one memo application built with C# / WinUI 3. It integrates memos, file paths, to-do lists, sticky notes and rich-text diaries into a single encrypted database. All your data is stored entirely on your local machine.

No cloud servers. No telemetry. Your data stays under your full control.

## Features
### Privacy Lock
Optional 6-digit numeric PIN protection powered by AES256-GCM encryption. Once enabled, all database content is encrypted at rest.
5 consecutive wrong password attempts trigger a 30-minute lockout. The failure counter and lock timer persist across program restarts and cannot be bypassed.
There is no password recovery backdoor. If you forget your PIN, the only solution is to wipe the entire database and rebuild it empty.

### Memo Manager
Supports group classification, star/pin priority marking and full-text search.
Dedicated API Key entry type built-in, with connectivity detection compatible with standard OpenAI interfaces.

### File Path Manager
Store and categorize local files & folders. Visual green/red border indicators show path validity.
One-click copy full path and one-click open folder with file highlighted in File Explorer.

### Task Dashboard
Hierarchical to-do cards with automatic completion logic for subtasks.
Sticky notes support expand/collapse for long text. All tick states are persisted permanently.

### Rich Text Diary
WebView2-based WYSIWYG editor, supporting bold, italic, underline and image insertion.
Built-in strict HTML sanitization to eliminate XSS risks during both save and load operations.

### Dual-Language UI
Fully localized Simplified Chinese / English interface.
Static resource dictionary architecture, no runtime XAML merging to avoid program crashes. Language changes take effect after restart.

### Theme System
Three display modes: Dark, Light, Follow System. User theme preferences are saved persistently.

### Data Import & Export
Complete database backup and recovery. All exported files are plaintext for cross-device compatibility.
Imported data is auto-sanitized and merged without overwriting local security files.

### System Tray
Two window close modes available: Minimize to tray or Exit directly.

### Auto Startup
Optional auto-launch with Windows, implemented via registry entries.

## Tech Stack
- Language: C# / XAML
- UI Framework: WinUI 3 / Windows App SDK 1.6
- Runtime: .NET 8 LTS
- Target OS: Windows 10 (Build 19041, 2004) & Windows 11
- Deployment: Self-contained publish + Inno Setup installer
- Storage: Single-file JSON database with optional AES256-GZip encryption
- Rich Text Engine: WebView2 (Edge Chromium)

## Installation Guide
Download the latest installer from the Releases page.
1. Run `Novara-Setup.exe`
2. Follow the setup wizard
3. Launch Novara from desktop shortcut or Start Menu

### System Requirements
- Windows 10 Version 2004 (Build 19041) or newer / Windows 11
- WebView2 Runtime (will auto-install if missing)
- .NET 8 Runtime (bundled inside self-contained package)

## Local Data Directory
All user data is stored in this local path:%LocalAppData%\Novara\
File breakdown:
- `data.novadb`: Main JSON database (plaintext or AES encrypted)
- `security.dat`: Password salt & SHA256 hash storage
- `lockout.dat`: Password failure counter & lock timer
- `language.dat`: Saved UI language preference

You can migrate all your notes by copying this entire folder to another PC.

## Security Specifications
- Local-only AES256-GCM encryption, PBKDF2 key derivation with 100,000 iterations
- Passwords are stored as salted SHA256 hashes; plaintext passwords are never saved on disk
- Database protected by exclusive file lock + Hidden / Read-only file attributes
- Diary HTML content double-sanitized on save & load, strips `<script>`, inline event attributes and malicious protocols like `javascript:`
- No outbound network requests except manual API connectivity tests initiated by users

## Core Architecture Highlights
- Single-file database with 7 isolated logical partitions: Diaries, File Paths, To-Dos, Stickies, Memo Groups, Memo Entries, App Settings
- Memory-first design: Full data load on startup; all edits are written to disk via thread-safe async operations
- Atomic write mechanism using temporary `.tmp` files to prevent database corruption on crash
- Unified theme rendering via `App.GetBrush()` to maintain consistent colors across all UI elements
- Double HTML sanitization for diaries to block XSS injection vectors

## Development Guide
### Prerequisites
- Visual Studio 2022 (17.8+)
- Windows App SDK 1.6
- .NET 8 SDK (locked via global.json: 8.0.402)

### Contact Us
For business cooperation, technical consulting, bug feedback, feature suggestions and other communications, please contact us via the following mailboxes:
owner@novara.xin
novara.xin@outlook.com
Novara_xin@163.com
