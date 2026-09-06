# Novara Security Policy

<p align="center"><strong>English</strong> · <a href="SECURITY.zh-CN.md">简体中文</a></p>

> See also: [Security Architecture](docs/security-architecture.md) — the two trust chains (data at rest / agent access) at a glance, and how to verify your download. · [安全架构（中文）](docs/security-architecture.zh-CN.md)

---

> This document has two jobs: (1) tell security researchers **how to report a vulnerability** privately, and (2) explain to users **how Novara protects their data and what it does not protect**.
>
> **Effective date:** 2026-08-14 · **Last updated:** 2026-09-06 · **Applies to:** Novara 6.0 (and earlier versions where noted)

---

## 1. Supported versions

Security fixes are provided for the versions below. We strongly recommend always running the latest release.

| Version | Status | Notes |
|---------|--------|-------|
| 6.0 | ✅ Supported | Current release |
| 5.x | ✅ Supported | Receives critical fixes where feasible |
| 4.0 | ✅ Supported | Receives critical fixes where feasible |
| 3.0 | ⚠️ Legacy | Receives critical fixes where feasible |
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
| Key derivation | PBKDF2-SHA256, **3,000,000 iterations** since format v3 (5.3+; ≈340 ms unlock on the reference machine); per-user random 32-byte salt. v2 databases (100,000 iterations) migrate via a one-time opt-in prompt |
| Password | 6–64 characters; only a versioned, salted password hash is stored locally — PBKDF2-SHA256 (3,000,000 iterations) since the hash format's second revision, never the plaintext |
| Authentication | 12-byte random nonce + 16-byte GCM tag — any tampering is detected |
| Data flow | JSON → GZip compression → AES encryption → on-disk file |
| File format | Versioned header (`NOVA` magic + version + encryption flag + 16-byte integrity field) + encrypted body |
| Migration | Legacy v1 (AES-CBC, Novara 2.0) auto-migrates to v2 (GCM) after one user confirmation; v2 migrates to v3 (hardened KDF) via a one-time opt-in prompt |

**Important:** encryption is **off by default**. Without the privacy lock, the database is a plaintext JSON file (protected only by your Windows account permissions). This is a deliberate design choice so that casual users are never locked out of their own data.

## 5. Data integrity & reliability

Novara includes several layers to prevent data loss and corruption:

- **Atomic writes** — every save writes to a temporary file, flushes to disk (`Flush(true)`), then atomically replaces the original. A crash mid-write cannot corrupt the database.
- **Single-writer discipline** — a debounced/serialized save queue and a single-instance mutex prevent concurrent writers from clobbering the file.
- **Exclusive file lock + Hidden/ReadOnly attributes** — reduce the chance of third-party software accidentally modifying the database.
- **Recycle bin (soft delete)** — deleted cards are recoverable for 7 days, then permanently purged on the next startup.
- **Rolling local backups** — the built-in backup keeps up to 10 local snapshots you can restore from (4.0+).
- **Crash logs (local, redacted)** — crash details are written locally (4.0+) for later diagnosis; sensitive values (e.g. API keys) are redacted and they are never auto-uploaded.
- **Integrity checks** — plaintext exports carry a SHA-256 header (dual-header detection keeps older MD5-headered files importable); encrypted backups are authenticated by GCM; imports are validated, normalized, and rolled back on failure.

## 6. Anti-brute-force & lockout

- After **5 consecutive wrong passwords**, unlocking is blocked for **30 minutes**.
- The failure count and lock deadline persist across restarts (`lockout.dat`), so restarting the app does not reset the lockout.
- Since 4.0, the lockout uses a **monotonic clock** (`Environment.TickCount64`), preventing a bypass by rolling the system clock backward.
- The lock screen clears its input when the window loses focus, reducing shoulder-surfing.

## 7. Version compatibility & downgrade warnings

| Scenario | Behavior |
|----------|----------|
| 2.0 data opened in 3.0+ | ✅ Read and migrated from AES-CBC (v1) to AES-GCM (v2) after one confirmation |
| v2 data opened in 5.3+ | ✅ Readable as-is; migrates to v3 (3,000,000-iteration KDF) via a one-time opt-in prompt |
| v3 data opened in 5.2.0 or earlier | ❌ **Not readable** — older versions do not understand the v3 KDF parameters |
| v2/v3 data opened in 2.0 | ❌ **Not readable** — 2.0 does not understand the GCM format |

> ⚠️ **Before downgrading or rolling back to 2.0**, export a plaintext backup. Once a database has been migrated to GCM (v2) or v3, older versions cannot open it. The v3 migration dialog states this downgrade limit before you opt in.

## 8. Application hardening

- **XSS protection in the diary editor** — rich-text HTML is sanitized on load, on save, and after navigation against a strict tag/attribute/URL whitelist, using a real HTML parser (AngleSharp). Script tags, `on*` event attributes (including entity-encoded variants such as `o&#110;load`), and dangerous protocols (`javascript:`, `vbscript:`, non-image `data:`) are stripped. Titles are rendered as plain text.
- **No code evaluation of untrusted input** — imported HTML and JSON are parsed and normalized, never executed.
- **Local-only helper process** — the desktop-sticky-note helper communicates with the main app via local files and named events on the same machine; it makes no network requests and opens no listening ports.
- **Minimal surface** — no listening network ports, no HTTP server, no remote-procedure-call surface exposed to the network. The only outbound network calls are the user-triggered API-key detection tiers (see the Privacy Policy). The optional MCP server is a local named-pipe endpoint only, reachable from the same machine.

## 9. Developer commitments

As the project owner, I commit to:

1. **Never** operating a cloud server that collects user data.
2. **Never** adding a backdoor, a password-recovery bypass, or any remote unlock/exfiltration mechanism.
3. **Never** adding telemetry, analytics, or ads.
4. **Never** auto-updating or auto-uploading data without explicit user action.
5. **Disclosing** security issues transparently: fix first, then publish, with credit to reporters.

## 10. MCP interface security model

The optional MCP interface lets an AI agent read and write cards. Its security model is defense-in-depth:

- **Off by default** — the interface is disabled until you enable it in Settings and copy a token.
- **Token authentication** — a per-user token, compared in fixed time (`CryptographicOperations.FixedTimeEquals`), passed via `NOVARA_MCP_TOKEN` or `--token`.
- **Database-unlock gate** — a locked or encrypted database refuses every request; an agent can never read encrypted data without unlocking.
- **Per-client approval** — the first connection from any client process requires explicit approval, persisted locally.
- **Per-client permission matrix** — each approved client holds its own 20-bit matrix (read / create / update / delete across the five data types). New clients start **read-only everywhere except memos**; a mixed (all-types) listing never leaks data from partitions the client cannot read.
- **Deletion master switch** — deleting requires both the client's own delete bit and a global toggle; the two are AND-ed so flipping one alone cannot enable deletion.
- **Audit log** — every call, including denied attempts, is recorded locally with process, time, tool, target, and result; fields are length-capped and credential-shaped values are masked.
- **Sensitive-field redaction** — password/key/token fields are returned as `****`, excluded from search, and protected against relabeling-based extraction.
- **Architecture** — `NovaraMCP.exe` is a zero-logic stdio frontend; all data access happens inside the running Novara process (the single data authority), never by the agent touching files directly. Malformed client input answers a spec-compliant error instead of hanging the connection.

The core principle — Novara itself never transmits your data — is preserved. See the Privacy Policy for the data boundary when a cloud-hosted AI client is connected.

## 11. Future changes

Security hardening continues across releases. The 6.0 round alone tightened MCP input handling, export/import round-trips, and backup-path validation; future updates will keep narrowing the attack surface. Each release's changelog lists security-relevant changes.

## 12. Contact

- **Email:** owner@novara.xin
- **GitHub:** https://github.com/Novara-owner/Novara-Vault
