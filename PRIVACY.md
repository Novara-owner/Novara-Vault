# Novara Privacy Policy

<p align="center"><strong>English</strong> · <a href="PRIVACY.zh-CN.md">简体中文</a></p>

---

**Effective date:** 2026-08-14
**Last updated:** 2026-08-25
**Applies to:** Novara 5.0 (and, where the behavior described below already existed, earlier versions)

> This policy describes the Novara **desktop application** for Windows. The official website (novara.xin) and the GitHub repository are separate properties; this document focuses on the software you install and run on your machine.

---

## TL;DR

- **Novara is 100% local.** Your data stays on your computer and never leaves it unless you explicitly make it leave.
- **We collect nothing.** No account, no telemetry, no usage analytics, no ads, no device fingerprinting.
- **It works fully offline.** The only network activity in the entire application is the API-key detection you trigger manually (Section 5), and the local MCP interface you opt into (Section 6).
- **Encryption is optional and OFF by default.** Read Section 4 carefully so you know exactly what is and is not protected.

---

## 1. What Novara is

Novara is a local-first personal knowledge manager for Windows, built with C# and WinUI 3. It combines memos, file-path bookmarks, to-do lists, sticky notes, and rich-text records into a single database that is stored entirely on your own device.

There is **no cloud backend**, no account system, and no server operated by the Novara project that could receive your data. The application is designed to be fully functional with no internet connection at all.

## 2. What we collect: nothing

The Novara application does **not** collect, transmit, or store on any remote server:

- your notes, passwords, records, to-do items, or any content you create;
- your identity, name, or email address (there is no registration);
- usage statistics, feature-usage logs, or behavioral analytics;
- device information, hardware identifiers, or crash reports (see Section 8);
- your IP address or network activity.

Because everything is processed and stored on your own machine, the developer has **no technical ability to see, read, or access your data**.

## 3. Where your data lives

All Novara data is written to a single local directory:

```
%LocalAppData%\Novara\
```

> **Development builds:** test/development builds use `%LocalAppData%\Novara-Dev\` instead, so that test data never mixes with your installed release. The rest of this section applies to both.

Files in that directory include:

| File | Purpose |
|------|---------|
| `data.novadb` | Your entire database (memos, paths, to-dos, notes, records, settings) as a single file |
| `security.dat` | Only salted hashes of your lock password (never the password itself); see Section 4 |
| `lockout.dat` | Lockout state after repeated wrong passwords (failure count and lock expiry) |
| `language.dat` | A plain-text hint of your selected interface language (needed to show the lock screen in the right language before the database is unlocked) |
| `stickies.json` | Data used to synchronize desktop sticky notes with the helper process |
| `backups\` | Rolling local snapshots created by the built-in backup feature |
| `logs\` | Local log files (see Section 8) |

Nothing in this directory is uploaded anywhere. Deleting this directory (or using the in-app "Reset" function) removes your data.

## 4. Encryption — read this carefully

Encryption in Novara is **optional and turned OFF by default**.

- **Default state:** your database is stored as a plaintext JSON file on your local disk. It is protected by the Windows user account's file permissions, but it is **not encrypted** unless you enable the privacy lock.
- **When you enable the privacy lock:** the entire database is encrypted with **AES-256-GCM** (authenticated encryption). The encryption key is derived from your password using **PBKDF2-SHA256 with 100,000 iterations** and a per-user random salt. GCM also detects tampering: if the encrypted file is modified, Novara reports it as corrupted instead of silently misreading it.
- **Password:** the lock password is **6 to 64 characters**. Only salted **SHA-256 hashes** of the password are stored locally — the plaintext password is never written to disk.
- **Windows Hello (since 5.0):** you can optionally unlock with Windows Hello biometrics or PIN. This never stores or transmits your biometric data; it delegates the check to the Windows Hello subsystem.
- **No backdoor, no recovery:** there is deliberately no way to recover a forgotten password. If you forget it, the only option is to erase the database and start over.
- **Brute-force protection:** after 5 consecutive wrong passwords, unlocking is blocked for 30 minutes. The failure count and lock deadline persist across restarts and use a monotonic clock to resist system-time rollback.
- **Upgrade path:** databases created by Novara 2.0 used an older AES-CBC format. Novara 3.0 and later can read them and migrate them to the newer GCM format after one confirmation.

**What encryption does and does not protect:** see the Security Policy (SECURITY.md) for the full threat model. In short, encryption protects your data **at rest** (the file on disk when the app is locked or closed). It does not protect data that is already unlocked in memory, nor does it protect against malware, keyloggers, screen capture, or physical access to your unlocked machine.

## 5. Network behavior — API-key detection

Novara is designed to be **fully usable offline** and does not phone home.

- **No automatic updates:** Novara does not check for, download, or install updates on its own.
- **No telemetry or analytics:** the application never contacts any server owned by the Novara project.
- **The only network feature:** the **API-key detection** in the memo manager, which offers three tiers you trigger explicitly.

### The three detection tiers

Some memo entries are of type "API Key". If you right-click such an entry, Novara can run, against **the endpoint URL that you yourself configured for that entry**:

| Tier | What it sends | Token cost |
|------|---------------|-----------|
| **Connectivity test** | A `GET` to a models-list path such as `/v1/models`, including that entry's API key | 0 |
| **Status diagnosis** | One or more minimal chat requests to infer balance presence, read metadata, and measure latency | 1–3 |
| **Relay probe** | Randomized test prompts to check model identity, capability, billing, and more | tens–hundreds |

All three send data **only to the third-party endpoint you configured** — never to the Novara project. Any tier that consumes tokens requires a **separate confirmation** from you before it runs, and Novara shows the actual token total in the report. Your key is masked in the UI and never written to logs or reports.

This is the **only** way any of your data is sent over the network. If you do not use this feature, Novara makes **zero** network requests.

## 6. MCP Agent interface — read this carefully

Since 5.0, Novara can expose a **local MCP server** so that an AI agent can read and write your cards on your behalf. This is an **opt-in** feature, disabled by default.

- **Novara itself stays offline.** The MCP server is a local process that talks to the running Novara app over a local named pipe. It makes no network requests and sends nothing anywhere by itself.
- **Access control.** The interface is gated by a token you set, a per-process authorization whitelist (a first connection from any process requires your approval), and the database-unlock state — a locked or encrypted database refuses every request. Deletion has its own separate permission.
- **Sensitive-field redaction.** Password, key, and token fields are read back as `****` and are excluded from search results, so an agent cannot casually extract your secrets.

**The one thing to understand about cloud AI.** The MCP server never transmits your data. However, if you connect a **cloud-hosted** AI assistant (for example, an AI app that calls a remote model), then whatever that assistant reads from Novara may be sent by *that AI client* to the AI provider you chose — this is controlled by the AI app and its provider, not by Novara. If you use a **local** model, nothing leaves your machine. Please check the privacy policy of any AI client you connect before granting it access.

## 7. What Novara accesses on your device

Novara is deliberately minimal in what it touches:

- **Its own data directory** (`%LocalAppData%\Novara\`, or `Novara-Dev` for development builds) — reads and writes only here.
- **Files/folders you choose** — the "path backup" feature lets you register file or folder paths. Novara checks whether those specific paths exist (a green/red border) and, when you click, opens them in File Explorer. It does **not** scan your whole disk and does not read unrelated files.
- **File/folder pickers** — when you create or edit a path entry, the picker lets you select a file or folder yourself.
- **A local named pipe** — used only by the optional MCP interface to receive tool calls from the `NovaraMCP.exe` process on the same machine.
- **Windows registry (current user only, `HKCU`)** — Novara writes a few small, user-scoped registry entries for optional features you turn on:
  - a "start with Windows" auto-start key under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
  - right-click menu entries under `HKCU\Software\Classes\` (desktop background, and file/folder context menus).
  These are removed when you disable the features or uninstall the app.

Novara does **not** request or use: camera, microphone, location, contacts, or any network-facing permission beyond the explicit detection described in Section 5.

## 8. Logs and diagnostics

- Logs are **local only**. They are never sent to the developer or any third party.
- Crash logs may be written to local files under `%LocalAppData%\Novara\logs\` (e.g. `crash-*.txt`) so that problems can be diagnosed later if you choose to share them. Sensitive values (e.g. API keys) are redacted from these logs.
- The desktop-sticky-note helper process writes its own log file under `%LocalAppData%\Novara\logs\` (`sticknotehost_debug.txt`).
- There is **no automatic diagnostic reporting**. If you contact support, you can choose to attach a log file yourself — nothing is transmitted without your action.

## 9. Third-party components

Novara is built on standard Microsoft technologies and a few open-source libraries:

| Component | Role | Data note |
|-----------|------|-----------|
| WinUI 3 / Windows App SDK 1.6 | UI framework | Local; no data collection by Novara |
| .NET 8 | Runtime | Local |
| WebView2 (Edge Chromium) | Hosts the records editor (rich text and Markdown) | A Microsoft component; may keep its own local runtime data. Its handling is governed by Microsoft's privacy statement. The editor itself runs **offline** (the engine is bundled, no CDN). |
| H.NotifyIcon | System-tray icon | Local |
| Tiptap | Rich-text editor engine | Bundled as a local script (`tiptap.bundle.js`) and runs entirely offline |
| markdown-it | Markdown rendering in the records editor | Bundled as a local script and runs entirely offline |
| AngleSharp | HTML sanitization for the editor | Local; used to strip unsafe content from pasted HTML |

Novara does not embed advertising SDKs, analytics SDKs, or any third-party tracking code.

## 10. Data sharing

Novara **does not share, sell, rent, or transmit** your data to any third party. There is no business model that involves your data — no ads, no analytics, no affiliate tracking.

The only data that can ever leave your machine is data you send yourself:

- the API-key detection (to the endpoint you configured, only on your explicit action),
- data an AI agent reads and sends via **a cloud AI client you chose** (see Section 6), and
- files you explicitly export or back up to a location you choose.

## 11. Your control over your data

You are in full control at all times:

- **Export (native)** — export a complete plaintext backup (`.novabak`) with an integrity (MD5) header. *Note: exported files are NOT encrypted* — keep them safe. File-path entries are excluded by default (with an option to include them) for moving to a new machine.
- **CSV export/import** — export memos as CSV, or import CSV from Novara, KeePass, or Bitwarden formats.
- **PDF / HTML collection** — export all records as a printable HTML or PDF collection.
- **Markdown export** — export records as Markdown, with or without images.
- **Backup snapshots** — the built-in backup keeps rolling local snapshots (up to 10) you can restore from.
- **Import / restore** — import a backup; validation, normalization, and rollback protect against corrupt or duplicate data.
- **Recycle bin** — deleted cards go to a recycle bin and are recoverable for 7 days, after which they are permanently removed on the next startup.
- **Reset / delete** — the in-app "Reset" wipes the database and starts fresh.
- **Privacy lock** — you can enable, change, or disable encryption at any time from Settings. Disabling it re-saves the database as plaintext.
- **MCP interface** — you can enable or disable the agent interface, rotate its token, and manage its process whitelist from Settings at any time.

## 12. What we will never do

The Novara project commits to the following, as a matter of design:

- We will **never** operate a cloud server that collects your data.
- We will **never** add telemetry, analytics, or ads.
- We will **never** add a backdoor, a password-recovery bypass, or any remote mechanism to unlock or exfiltrate your data.
- We will **never** auto-update, auto-upload, or otherwise move your data without an explicit action from you.

## 13. Children's privacy

Novara does not collect any personal information from anyone, including children. Because the application is local-only and collects nothing, no special children's-privacy provisions apply.

## 14. Changes to this policy

This policy may be updated as the application evolves (for example, when a new feature is added). Updates will be published alongside new releases. An updated policy applies from its effective date and does not affect or alter the data already stored on your device.

## 15. Contact

For privacy questions or requests, contact:

- **Email:** owner@novara.xin
- **Website:** https://novara.xin
- **GitHub:** https://github.com/Novara-owner/Novara-Vault
