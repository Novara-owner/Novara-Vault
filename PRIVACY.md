# Novara Privacy Policy

<p align="center"><strong>English</strong> · <a href="PRIVACY.zh-CN.md">简体中文</a></p>

---

**Effective date:** 2026-08-14
**Last updated:** 2026-08-17
**Applies to:** Novara 4.0 (and, where the behavior described below already existed, Novara 3.0 and 2.0)

> This policy describes the Novara **desktop application** for Windows. The official website (novara.xin) and the GitHub repository are separate properties; this document focuses on the software you install and run on your machine.

---

## TL;DR

- **Novara is 100% local.** Your data stays on your computer and never leaves it unless you explicitly make it leave.
- **We collect nothing.** No account, no telemetry, no usage analytics, no ads, no device fingerprinting.
- **It works fully offline.** The only network activity in the entire application is the optional API-key connectivity test, which **you** trigger manually.
- **Encryption is optional and OFF by default.** Read Section 4 carefully so you know exactly what is and is not protected.

---

## 1. What Novara is

Novara is a local-first personal knowledge manager for Windows, built with C# and WinUI 3. It combines memos, file-path bookmarks, to-do lists, sticky notes, and a rich-text diary into a single database that is stored entirely on your own device.

There is **no cloud backend**, no account system, and no server operated by the Novara project that could receive your data. The application is designed to be fully functional with no internet connection at all.

## 2. What we collect: nothing

The Novara application does **not** collect, transmit, or store on any remote server:

- your notes, passwords, diary entries, to-do items, or any content you create;
- your identity, name, or email address (there is no registration);
- usage statistics, feature-usage logs, or behavioral analytics;
- device information, hardware identifiers, or crash reports (see Section 7);
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
| `data.novadb` | Your entire database (memos, paths, to-dos, notes, diary, settings) as a single file |
| `security.dat` | Only salted hashes of your lock password (never the password itself); see Section 4 |
| `lockout.dat` | Lockout state after repeated wrong passwords (failure count and lock expiry) |
| `language.dat` | A plain-text hint of your selected interface language (needed to show the lock screen in the right language before the database is unlocked) |
| `stickies.json` | Data used to synchronize desktop sticky notes with the helper process |
| `backups\` | Rolling local snapshots created by the built-in backup feature (4.0+) |
| `logs\` | Local log files (see Section 7) |

Nothing in this directory is uploaded anywhere. Deleting this directory (or using the in-app "Reset" function) removes your data.

## 4. Encryption — read this carefully

Encryption in Novara is **optional and turned OFF by default**.

- **Default state:** your database is stored as a plaintext JSON file on your local disk. It is protected by the Windows user account's file permissions, but it is **not encrypted** unless you enable the privacy lock.
- **When you enable the privacy lock:** the entire database is encrypted with **AES-256-GCM** (authenticated encryption). The encryption key is derived from your password using **PBKDF2-SHA256 with 100,000 iterations** and a per-user random salt. GCM also detects tampering: if the encrypted file is modified, Novara reports it as corrupted instead of silently misreading it.
- **Password:** the lock password is **6 to 64 characters**. Only salted **SHA-256 hashes** of the password are stored locally — the plaintext password is never written to disk.
- **No backdoor, no recovery:** there is deliberately no way to recover a forgotten password. If you forget it, the only option is to erase the database and start over.
- **Brute-force protection:** after 5 consecutive wrong passwords, unlocking is blocked for 30 minutes. The failure count and lock deadline persist across restarts, and since 4.0 the lockout uses a monotonic clock to resist attempts to bypass it by changing the system time.
- **Upgrade path:** databases created by Novara 2.0 used an older AES-CBC format. Novara 3.0/4.0 can read them and migrates them to the newer GCM format after one confirmation.

**What encryption does and does not protect:** see the Security Policy (SECURITY.md) for the full threat model. In short, encryption protects your data **at rest** (the file on disk when the app is locked or closed). It does not protect data that is already unlocked in memory, nor does it protect against malware, keyloggers, screen capture, or physical access to your unlocked machine.

## 5. Network behavior

Novara is designed to be **fully usable offline** and does not phone home.

- **No automatic updates:** Novara does not check for, download, or install updates on its own.
- **No telemetry or analytics:** the application never contacts any server owned by the Novara project.
- **The one and only network feature:** the **API-key connectivity test** in the memo manager.

### The API-key connectivity test (the only network call)

Some memo entries are of type "API Key". If you right-click such an entry and choose **Test Connectivity**, Novara will:

1. send a request (typically a `GET` to a models-list path such as `/v1/models`) **to the endpoint URL that you yourself configured for that entry**, including that entry's API key in the request;
2. match the endpoint domain against a built-in list of common providers (OpenAI, OpenRouter, Anthropic, Google Gemini, Azure OpenAI, Groq, Together, Perplexity, Zhipu, Qwen, DeepSeek, Moonshot, and others) to determine the authentication style and path; unknown endpoints fall back to generic OpenAI-compatible probing;
3. show the result locally (success / auth-failed / no-models-endpoint / network-error).

This is the **only** moment any of your data is sent over the network, and it is:

- triggered **only by an explicit action from you**;
- sent **only to the third-party endpoint you configured** — never to the Novara project;
- not saved, logged, or forwarded anywhere else by Novara.

If you do not use this feature, Novara makes **zero** network requests.

## 6. What Novara accesses on your device

Novara is deliberately minimal in what it touches:

- **Its own data directory** (`%LocalAppData%\Novara\`, or `Novara-Dev` for development builds) — reads and writes only here.
- **Files/folders you choose** — the "path backup" feature lets you register file or folder paths. Novara checks whether those specific paths exist (a green/red border) and, when you click, opens them in File Explorer. It does **not** scan your whole disk and does not read unrelated files.
- **File/folder pickers** — when you create or edit a path entry, the picker lets you select a file or folder yourself.
- **Windows registry (current user only, `HKCU`)** — Novara writes a few small, user-scoped registry entries for optional features you turn on:
  - a "start with Windows" auto-start key under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
  - right-click menu entries under `HKCU\Software\Classes\` (desktop background, and file/folder context menus).
  These are removed when you disable the features or uninstall the app.

Novara does **not** request or use: camera, microphone, location, contacts, or any network-facing permission beyond the explicit connectivity test described above.

## 7. Logs and diagnostics

- Logs are **local only**. They are never sent to the developer or any third party.
- Starting with 4.0, crash logs may be written to local files under `%LocalAppData%\Novara\logs\` (e.g. `crash-*.txt`) so that problems can be diagnosed later if you choose to share them. Sensitive values (e.g. API keys) are redacted from these logs.
- The desktop-sticky-note helper process writes its own log file under `%LocalAppData%\Novara\logs\` (`sticknotehost_debug.txt`).
- There is **no automatic diagnostic reporting**. If you contact support, you can choose to attach a log file yourself — nothing is transmitted without your action.

## 8. Third-party components

Novara is built on standard Microsoft technologies and a few open-source libraries:

| Component | Role | Data note |
|-----------|------|-----------|
| WinUI 3 / Windows App SDK 1.6 | UI framework | Local; no data collection by Novara |
| .NET 8 | Runtime | Local |
| WebView2 (Edge Chromium) | Hosts the rich-text diary editor | A Microsoft component; may keep its own local runtime data. Its handling is governed by Microsoft's privacy statement. The diary editor itself runs **offline** (the editor engine is bundled, no CDN). |
| H.NotifyIcon | System-tray icon | Local |
| Tiptap | Diary editor engine | Bundled as a local script (`tiptap.bundle.js`) and runs entirely offline |
| AngleSharp | HTML sanitization for the diary editor | Local; used to strip unsafe content from pasted HTML |

Novara does not embed advertising SDKs, analytics SDKs, or any third-party tracking code.

## 9. Data sharing

Novara **does not share, sell, rent, or transmit** your data to any third party. There is no business model that involves your data — no ads, no analytics, no affiliate tracking.

The only data that can ever leave your machine is data you send yourself:

- the API-key connectivity test (to the endpoint you configured), and
- files you explicitly export or back up to a location you choose.

## 10. Your control over your data

You are in full control at all times:

- **Export (native)** — export a complete plaintext backup (`.novabak`) with an integrity (MD5) header. *Note: exported files are NOT encrypted* — keep them safe. File-path entries are excluded by default (with an option to include them) for moving to a new machine.
- **Markdown export** — export a read-only summary of your data as Markdown, with or without images (4.0+). This is for reading/sharing; it cannot be re-imported.
- **Backup snapshots** — the built-in backup keeps rolling local snapshots (up to 10) you can restore from (4.0+).
- **Import / restore** — import a backup; validation, normalization, and rollback protect against corrupt or duplicate data.
- **Recycle bin** — deleted cards go to a recycle bin and are recoverable for 7 days, after which they are permanently removed on the next startup.
- **Reset / delete** — the in-app "Reset" wipes the database and starts fresh.
- **Privacy lock** — you can enable, change, or disable encryption at any time from Settings. Disabling it re-saves the database as plaintext.

## 11. What we will never do

The Novara project commits to the following, as a matter of design:

- We will **never** operate a cloud server that collects your data.
- We will **never** add telemetry, analytics, or ads.
- We will **never** add a backdoor, a password-recovery bypass, or any remote mechanism to unlock or exfiltrate your data.
- We will **never** auto-update, auto-upload, or otherwise move your data without an explicit action from you.

## 12. Children's privacy

Novara does not collect any personal information from anyone, including children. Because the application is local-only and collects nothing, no special children's-privacy provisions apply.

## 13. Changes to this policy

This policy may be updated as the application evolves (for example, when a new feature is added). Updates will be published alongside new releases. An updated policy applies from its effective date and does not affect or alter the data already stored on your device.

## 14. Contact

For privacy questions or requests, contact:

- **Email:** owner@novara.xin
- **Website:** https://novara.xin
- **GitHub:** https://github.com/Novara-owner/Novara-Vault
