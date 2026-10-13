# Assurance Case

English · [简体中文](ASSURANCE.zh-CN.md)

This page assembles the project's security case: what the software claims, against which threats, with which design principles, and where the evidence lives. It summarises and links — the normative sources are [threat-model.md](threat-model.md), [security-architecture.md](security-architecture.md), [SECURITY.md](../SECURITY.md), [docs/PROVENANCE.md](PROVENANCE.md) and [docs/CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## 1. System overview and security claims

Novara is a local-first personal knowledge tool for Windows: memos, file-path bookmarks, plans and diary records in a single local file, with optional end-to-end encrypted sync to a server you host, a read-only web companion, and a permission-gated agent (MCP) interface. There is no account, no telemetry and no vendor-operated server that can read your data.

The case rests on these claims:

| # | Claim | Evidence |
|---|---|---|
| C1 | Data at rest is protected by AES-256-GCM with a versioned, authenticated header; the lock password never touches the disk | [security-architecture.md](security-architecture.md) §1; the mitigation table in [threat-model.md](threat-model.md) §6 |
| C2 | Exported and synced artifacts leave the machine only in forms the user chose: encrypted containers with mandatory passwords, or ciphertext the server cannot read | [security-architecture.md](security-architecture.md) §3–4; [threat-model.md](threat-model.md) §2, §6 |
| C3 | The agent interface is closed by default, gated per client, and auditable | [security-architecture.md](security-architecture.md) §2; [docs/MCP.md](MCP.md) |
| C4 | Release artifacts are reproducible to check: checksums, pinned image digests, an SBOM, Sigstore keyless signatures, and a signed update manifest | [docs/VERIFYING.md](VERIFYING.md); [docs/PROVENANCE.md](PROVENANCE.md); [docs/CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) |
| C5 | The desktop application exposes no listening ports and makes only outbound connections the user triggers | [threat-model.md](threat-model.md) §6 (last row); the in-app network-activity panel |
| C6 | The server holds ciphertext only, authenticates devices with hashed tokens under rate limiting, and never learns the space key | [security-architecture.md](security-architecture.md) §4; [threat-model.md](threat-model.md) §3 (A4) |

## 2. Threat model summary

The full model is [threat-model.md](threat-model.md). Its structure is the structure of this case:

- **Assets** — the database, the lock password and derived key, MCP credentials, sync secrets, exported copies, local logs, and whatever credentials the user chooses to store.
- **Adversaries** — seven classes, each with the boundary that must hold and the condition under which it fails: another person at the machine (A1), malware running as you (A2 — explicitly no boundary, by construction), an offline attacker holding a copy (A3), a network attacker on the sync path (A4), an approved AI client and its vendor (A5), an attacker on the distribution channel (A6), and physical or below-OS access (A7 — out of scope).
- **Assumptions and non-goals** — a strong password, an uncompromised OS account, user-controlled deployment security, and explicit positions such as: no password recovery, no telemetry, no vendor cloud.

The threat model is maintained as part of the code base: "a threat model that contradicts the software is a defect."

## 3. Trust boundaries

[security-architecture.md](security-architecture.md) describes four chains, each with the boundary drawn where untrusted input is transformed into trusted state:

1. **Data at rest** — the privacy-lock chain: password → versioned KDF → AES-256-GCM over a versioned header; the header is bound as associated data.
2. **Agent access** — the MCP chain: unlock gate → per-client approval → permission matrix → field redaction → deletion master switch → audit log.
3. **Snapshot export** — the connected chain: a mandatory export password, credentials stripped before sealing, a versioned container.
4. **Cross-device sync** — the two-way chain: keywrap sealed per space, a versioned ciphertext envelope with a plaintext version stamp the server can route on, optimistic concurrency (If-Match), and a generation lock that protects legacy clients from a format they cannot read.

Trust boundaries on the wire: the sync server validates every request against the space/device binding and never parses payload bodies; the web reader keeps decrypted data in memory only; the update channel verifies a signature over the manifest bytes before parsing.

## 4. Design principles as applied

| Principle | Where it applies |
|---|---|
| Fail-safe defaults | Encryption off is a visible, stated state — but the MCP interface is off by default; the update channel refuses rather than downgrades (a missing or malformed signature fails the whole check); unknown fields in sync envelopes are accepted, unknown values fail closed |
| Complete mediation | Every sync request is authenticated per space and device; every MCP call passes the unlock gate and the permission matrix; export always requires the password |
| Least privilege | Device tokens are per-device and revocable; the web read token cannot write; the editor credential is a single dedicated identity; MCP permissions are per-client and field-level with a deletion master switch |
| Minimised attack surface | No listening ports in the desktop app; the self-hosted server is opt-in and loopback-bound by default; the browser reader persists nothing |
| Separation of duties | CI produces artifacts only when green; signing is keyless and pinned to the producing workflow and tag; the update-manifest key is independent of the release infrastructure |
| Economy of mechanism | One container format across backup, snapshot and sync payloads; one stable-serialisation source; a single, documented storage schema |
| Reluctance to trust | Client input is never evaluated: HTML is sanitised against a strict whitelist (AngleSharp); import normalises ids and validates before applying; the sync server treats payloads as opaque ciphertext |

## 5. Common-weakness mapping

Relevant entries from the CWE Top 25 and OWASP Top 10 for a desktop C# application, and where each is addressed:

| Weakness | Mitigation |
|---|---|
| Injection (CWE-78/79/89) | HTML sanitisation via a strict whitelist; no SQL (single-file JSON store); no shell-outs with user data; editor content rendered in a sandboxed viewer with a content security policy |
| Cryptographic failures (CWE-327/321) | AES-256-GCM everywhere, PBKDF2-SHA256 at 3,000,000 iterations (Argon2id support in 11.0), per-user salts, no hardcoded keys — the space key is user-generated and never transmitted |
| Insecure deserialisation (CWE-502) | Fail-closed envelope parsing (missing fields throw), bounded iteration windows on container headers, strict manifest-format checks, payload bodies never parsed server-side |
| Broken authentication / session (CWE-287/384) | Hashed device tokens compared in constant time, rate-limited auth failures, a monotonic-clock lockout, single-instance and exclusive file locks |
| Path traversal (CWE-22) | Server-side identifier validation (`IsSafeId`, 64-hex chunk ids), path construction from validated segments only |
| Privilege escalation (CWE-250/269) | Per-client MCP permission matrices, per-device revocable tokens, role separation in the signing policy |
| SSRF / unintended network access (CWE-918) | Outbound connections only on user-triggered paths, all listed in the network-activity panel; the desktop app listens on no ports |
| Supply-chain (CWE-1357/OWASP A8) | Pinned dependencies (`packages.lock.json`), Dependabot, CI-enforced zero warnings, `SHA256SUMS`, pinned image digests, SBOM, keyless Sigstore signatures, an ECDSA-signed update manifest |
| Logging and monitoring failures (CWE-778) | A local audit log for sync and MCP actions, redacted crash logs, no secrets in logs (enforced by a frozen baseline on shipped configuration files) |

## 6. How to check the claims

Every claim above is checkable without trusting this page: C1–C3 by reading the linked architecture pages against the source; C4 by following [docs/VERIFYING.md](VERIFYING.md) on a released artifact; C5–C6 by inspecting the endpoints and the server's data layout. Where this page and the product disagree, the product is wrong — report it privately through [SECURITY.md](../SECURITY.md) section 2.

---

*Last updated: 2026-10-13*
