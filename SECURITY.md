# Novara Security Policy

<p align="center"><strong>English</strong> · <a href="SECURITY.zh-CN.md">简体中文</a></p>

---

> This document has two jobs: (1) tell security researchers **how to report a vulnerability** privately, and (2) explain to users **how Novara protects their data and what it does not protect**.
>
> **Effective date:** 2026-08-14 · **Last updated:** 2026-08-17 · **Applies to:** Novara 4.0 (and earlier versions where noted)

---

## 1. Supported versions

Security fixes are provided for the versions below. We strongly recommend always running the latest release.

| Version | Status | Notes |
|---------|--------|-------|
| 4.0 | ✅ Supported | Current release |
| 3.0 | ✅ Supported | Receives critical fixes where feasible |
| 2.0 | ⚠️ Legacy | Uses the older AES-CBC encryption; **upgrade recommended** (see Section 7) |
| < 2.0 | ❌ Unsupported | |

## 2. Reporting a vulnerability

We take security reports seriously and will never penalize good-faith security research.

**Please report privately first** — do not open a public issue with exploit details.

- **Preferred:** GitHub → **Security** tab → **Report a vulnerability** (Private Vulnerability Reporting).
- **Alternative:** Email **owner@novara.xin** with the subject line `[Security] ...`.

Please include:

1. the affected version(s);
2. a clear description of the issue and its potential impact;
3. steps to reproduce (or a proof of concept), if available;
4. any suggested fix (optional).

**What to expect:**

- We aim to acknowledge your report within **5 business days**.
- We will keep you updated on our assessment and fix plan.
- **High-severity issues will not be disclosed publicly until a fix is released.** Lower-severity issues will be coordinated with you.
- Credit: we are happy to credit reporters in release notes unless you ask to remain anonymous.

## 3. Security model & threat model

**What we protect.** Novara is a *local-first* application. Its security goal is to protect your data **at rest** — the single database file on disk — against:

- casual or unauthorized reading when the app is locked or closed (via optional encryption);
- silent tampering of the encrypted file (via AES-GCM authentication);
- accidental data loss (via atomic writes, a recycle bin, and local backups);
- brute-force guessing of the lock password (via lockout and a monotonic clock).

**What we do NOT protect (honest limits).** A local application cannot defend against everything. Novara does **not** protect against:

- **An unlocked session** — while the app is unlocked and in use, an attacker who can operate your machine can see what you see.
- **Malware on your machine** — keyloggers, screen recorders, RATs, or a compromised Windows account can observe or exfiltrate data regardless of Novara's encryption.
- **Physical access** — someone with physical access to your unlocked device, or who can reset your Windows password, can potentially reach your files.
- **A lost/forgotten password** — by design there is no recovery. Encryption that has no backdoor also means no password reset.
- **Memory extraction** — encryption keys exist in memory while the app is unlocked; a sophisticated local attacker with the right tools may extract them.

> **Bottom line:** Novara's encryption protects your data *at rest on disk*. It is not a substitute for a healthy, malware-free, physically-secured machine.

## 4. Encryption architecture

When the privacy lock is enabled, Novara encrypts the entire database:

| Aspect | Detail |
|--------|--------|
| Cipher | AES-256-GCM (authenticated encryption), 256-bit key |
| Key derivation | PBKDF2-SHA256, 100,000 iterations, per-user random 32-byte salt |
| Password | 6–64 characters; only salted SHA-256 hashes are stored locally (never the plaintext) |
| Authentication | 12-byte random nonce + 16-byte GCM tag — any tampering is detected |
| Data flow | JSON → GZip compression → AES encryption → on-disk file |
| File format | Versioned header (`NOVA` magic + version + encryption flag + 16-byte integrity field) + encrypted body |
| Migration | Legacy v1 (AES-CBC, Novara 2.0) is read-only and auto-migrates to v2 (GCM) after one user confirmation |

**Important:** encryption is **off by default**. Without the privacy lock, the database is a plaintext JSON file (protected only by your Windows account permissions). This is a deliberate design choice so that casual users are never locked out of their own data.

## 5. Data integrity & reliability

Novara includes several layers to prevent data loss and corruption:

- **Atomic writes** — every save writes to a temporary file, flushes to disk (`Flush(true)`), then atomically replaces the original. A crash mid-write cannot corrupt the database.
- **Single-writer discipline** — a debounced/serialized save queue and a single-instance mutex prevent concurrent writers from clobbering the file.
- **Exclusive file lock + Hidden/ReadOnly attributes** — reduce the chance of third-party software accidentally modifying the database.
- **Recycle bin (soft delete)** — deleted cards are recoverable for 7 days, then permanently purged on the next startup.
- **Rolling local backups** — the built-in backup keeps up to 10 local snapshots you can restore from (4.0+).
- **Crash logs (local, redacted)** — crash details are written locally (4.0+) for later diagnosis; sensitive values (e.g. API keys) are redacted and they are never auto-uploaded.
- **Integrity checks** — the export format carries a 16-byte MD5 header; imports are validated, normalized, and rolled back on failure.

## 6. Anti-brute-force & lockout

- After **5 consecutive wrong passwords**, unlocking is blocked for **30 minutes**.
- The failure count and lock deadline persist across restarts (`lockout.dat`), so restarting the app does not reset the lockout.
- Since 4.0, the lockout uses a **monotonic clock** (`Environment.TickCount64`), preventing a bypass by rolling the system clock backward.
- The lock screen clears its input when the window loses focus, reducing shoulder-surfing.

## 7. Version compatibility & downgrade warnings

| Scenario | Behavior |
|----------|----------|
| 2.0 data opened in 3.0/4.0 | ✅ Read and migrated from AES-CBC (v1) to AES-GCM (v2) after one confirmation |
| 3.0/4.0 data opened in 2.0 | ❌ **Not readable** — 2.0 does not understand the GCM format |
| 3.0 ↔ 4.0 | ✅ Same format (v2 GCM); compatible |

> ⚠️ **Before downgrading or rolling back to 2.0**, export a plaintext backup. Once a database has been migrated to GCM, older versions cannot open it.

## 8. Application hardening

- **XSS protection in the diary editor** — rich-text HTML is sanitized on load, on save, and after navigation against a strict tag/attribute/URL whitelist, using a real HTML parser (AngleSharp). Script tags, `on*` event attributes (including entity-encoded variants such as `o&#110;load`), and dangerous protocols (`javascript:`, `vbscript:`, non-image `data:`) are stripped. Titles are rendered as plain text.
- **No code evaluation of untrusted input** — imported HTML and JSON are parsed and normalized, never executed.
- **Local-only helper process** — the desktop-sticky-note helper communicates with the main app via local files and named events on the same machine; it makes no network requests and opens no listening ports.
- **Minimal surface** — no listening network ports, no HTTP server, no remote-procedure-call surface exposed to the network. The only outbound network call in the entire app is the user-triggered API-key connectivity test (see the Privacy Policy).

## 9. Developer commitments

As the project owner, I commit to:

1. **Never** operating a cloud server that collects user data.
2. **Never** adding a backdoor, a password-recovery bypass, or any remote unlock/exfiltration mechanism.
3. **Never** adding telemetry, analytics, or ads.
4. **Never** auto-updating or auto-uploading data without explicit user action.
5. **Disclosing** security issues transparently: fix first, then publish, with credit to reporters.

## 10. Future changes

The next major version (5.0) plans a local Agent/MCP interface so that an AI assistant can read and write cards on your behalf. When that feature ships, this policy will be updated with a dedicated section covering its permission model, authentication, and data boundaries. The core principle — Novara itself never transmits your data — will be preserved.

## 11. Contact

- **Email:** owner@novara.xin
- **GitHub:** https://github.com/Novara-owner/Novara-Vault
