# Threat Model

<p align="center"><strong>English</strong> · <a href="threat-model.zh-CN.md">简体中文</a></p>

> See also: [Security Policy](../SECURITY.md) — how to report a vulnerability and what we commit to. · [Security Architecture](security-architecture.md) — the four trust chains at a glance. · [Verifying your download](VERIFYING.md)

**Effective date:** 2026-10-08 · **Last updated:** 2026-10-08 · **Applies to:** Novara 9.4 and later (items introduced in a specific version are marked)

---

## 1. How to read this

Novara is a local-first personal data control layer that runs on *your* machine. A useful threat model for a desktop application cannot be a list of things we "defend against" — it has to say **where each boundary is, and what has to be true for it to hold**. So this page states five things plainly: the assets, the adversary classes, the assumptions we depend on but do not control, what we deliberately do **not** protect, and the concrete mitigations that implement each boundary.

Two consequences follow, and both are intentional. Everything here assumes **you are the only person with a legitimate account on the machine**; and nothing here protects data from software that already runs with your privileges.

## 2. Assets

What is worth protecting, and in what form:

- **The database (`data.novadb`)** — diary, memos, plans and documents in a single file under `%LocalAppData%\Novara`. Its protection depends on whether the privacy lock is on: **encryption is off by default**, and a plaintext library is guarded only by your Windows account permissions.
- **The lock password and the derived key** — the password is never written to disk. `security.dat` holds only a versioned, salted PBKDF2-SHA256 hash used for lock-screen verification; it is not the encryption key and cannot decrypt the database.
- **MCP credentials** — the per-user agent token, the per-client approval records, and each approved client's permission matrix.
- **Sync secrets** — the space key (generated on your first device and never sent anywhere), the one-time enrollment secret, and the per-device tokens.
- **Exported copies** — snapshots, encrypted backups and plaintext `.novabak` exports (which carry integrity checksums only: corruption detection, not tamper protection).
- **Local logs and history** — the audit log, crash logs (redacted) and whatever the recycle bin and rolling backups currently hold.
- **Credentials you chose to store in Novara** — memos are where API keys end up, and the memo zone is treated as the most sensitive partition in the whole MCP model.

## 3. Adversaries

Seven classes. For each one, the boundary that must hold and the condition under which it fails.

- **A1 — Another person at the same machine.** A shared PC, a borrowed laptop, someone reading over your shoulder. *Boundary:* the privacy lock plus your Windows account permissions. *Holds while* the library is encrypted **and** the app is locked. *Fails when* the privacy lock is off — the file is then plaintext JSON — or when the app is unlocked and left unattended. The lock screen clears its input when the window loses focus, and unlocking is blocked for 30 minutes after 5 consecutive wrong passwords.
- **A2 — Malware, or a compromised Windows account, running as you.** Keyloggers, screen recorders, remote-access trojans. *Boundary:* none, by construction. Code running with your privileges can read the plaintext the app reads, capture the password as you type it, screenshot the window, and read keys out of process memory while the library is unlocked.
- **A3 — An offline attacker holding a copy of your data.** A disk image, a stolen backup, a snapshot file shared too widely, a decommissioned drive. *Boundary:* AES-256-GCM plus PBKDF2-SHA256 at 3,000,000 iterations with a per-user random salt. *Holds while* the artifact is an encrypted container **and** the password is strong. *Fails when* the copy is a plaintext library or a plaintext export, or the password is guessable — the KDF multiplies the cost of each guess, it does not make a weak password safe.
- **A4 — A network attacker on the sync path.** *Boundary:* the ciphertext and the transport. The server keeps one versioned ciphertext blob plus a small plaintext envelope (container / crypto / sync version numbers, the base version the payload was built on, a device id and a timestamp) and holds no key; the protocol assumes HTTPS, and the bundled server binds to loopback by default. *Note:* the sync server does listen for connections — it is a component you choose to run, and its deployment (TLS termination, exposure to the internet) is yours to get right.
- **A5 — An AI client you approved, and whatever its vendor does upstream.** *Boundary:* the unlock gate, per-client approval, the permission matrix, field redaction, the deletion master switch and the audit log. *Fails when* a client that was granted broad permissions forwards what it legitimately read to a cloud service — Novara can control what a client may read and write locally, but not what its vendor does with that data.
- **A6 — Someone attacking the channel we distribute through.** The release artifacts, the container image, the update manifest, our dependencies. *Boundary, 10.0+:* CI must be green before artifacts are produced, every release carries `SHA256SUMS`, image digests are pinned in `IMAGES.txt`, an SBOM is published, and the installer and the image carry keyless [Sigstore](https://www.sigstore.dev/) signatures that pin the producing workflow and tag. See [Verifying your download](VERIFYING.md).
- **A7 — Physical access, or anything below the operating system.** Boot-level tampering, a compromised firmware or hypervisor, coercion of the person who knows the password. **Out of scope** — see section 5.

## 4. Assumptions

Things the design depends on and does not itself guarantee:

- **Your operating system and account are not compromised.** Every boundary above is an at-rest or in-process boundary; none of them survives an attacker who already runs as you (A2).
- **You choose a strong password.** The 3,000,000-iteration KDF multiplies the cost of each guess; it cannot rescue a weak one. There is no password recovery, by design.
- **You decide how much of your own estate is encrypted.** If your disk is not encrypted by the operating system, a copy of Novara's files taken outside the app is only as protected as those files are.
- **On the sync path, transport security is your deployment.** Novara assumes HTTPS and recommends a TLS-terminating reverse proxy; exposing the server over plain HTTP or to the open internet is a deployment choice with consequences.
- **Windows Hello is a convenience gate, not a cryptographic binding.** It only gates retrieval of your password from the operating system credential vault; the encryption chain is unchanged, and its absence or failure always falls back to the password.
- **What your AI client does beyond the boundary is governed by your agreement with it.** See A5.
- **Your backups and exported copies are stored as carefully as the originals.** A snapshot or backup is protected by its password and by wherever you chose to put it.
- **Your clock is broadly trustworthy** — with one deliberate exception: the login lockout uses a monotonic clock, so rolling the system clock backward cannot shorten it.

## 5. Non-goals

We do not claim protection against the following. These are deliberate positions, not oversights.

- **An unlocked session** — while the app is unlocked and in use, someone who can operate your machine sees what you see.
- **Memory extraction** — keys exist in process memory while the library is unlocked.
- **A lost or forgotten password** — there is no recovery path, no backdoor and no remote unlock.
- **Coercion and duress** — there is no duress password and no plausible-deniability mode.
- **Multi-user isolation** — this is a single-user desktop application, not a multi-tenant system.
- **Secure erasure** — deleting data removes it from the database and purges the recycle bin, but we make no claim about remnants at the storage-device level.
- **Availability of a server you host yourself** — denial of service against it, or its loss, is outside the model.
- **An operating system, firmware or hypervisor that is already compromised**, and physical attacks below the operating system.
- **Anything an official cloud would have needed to defend.** We do not operate one: there is no account, no telemetry, no analytics and no server that can read your data, by commitment (see the Security Policy, section 9).
- **Your network's metadata** — Novara does not hide the existence of the connections you trigger from your network operator. The paths are listed in the in-app network activity panel.

## 6. Mitigations

Each mechanism, what it is actually for, and what it does not cover:

| Mitigation | Protects against | Does not protect against |
|---|---|---|
| AES-256-GCM (12-byte nonce, 16-byte tag) over a versioned header | Silent tampering and unauthorized reading of the encrypted file (A1, A3) | A plaintext library; an unlocked session |
| PBKDF2-SHA256 at 3,000,000 iterations with a per-user random salt | The cost of guessing offline (A3) | Weak passwords; observation by software on the machine |
| Lockout: 5 failures then 30 minutes, persisted in `lockout.dat`, monotonic clock | Guessing at the lock screen (A1) | Offline attacks on a copied file |
| Atomic writes with `Flush(true)`, a serialized save queue, single-instance mutex and exclusive file lock | Corruption and concurrent clobbering | Confidentiality — this is a data-integrity control |
| Recycle bin and rolling local backups | Accidental deletion | Deliberate destruction of the machine |
| `.novaenc` v4 container, a mandatory password on every export, credentials stripped before sealing | Plaintext leaving the machine; snapshot leakage (A3) | A weak export password; a copied file with a guessable password |
| Server stores ciphertext only, key separation, device tokens kept as SHA-256 and compared in constant time with rate limiting | A curious or compromised server (A4) | A deployment without TLS; the server's own availability |
| Browser reader keeps plaintext in memory only: nothing persisted, clipboard cleared after 30 seconds, memory cleared after 5 minutes idle | Data lingering on a shared browser | A compromised browser or extension |
| MCP off by default, unlock gate, per-client approval, permission matrix, deletion master switch, field redaction, local audit log | An over-broad or curious agent client (A5) | A vendor's handling of data a client legitimately reads |
| Import and editor sanitization against a strict whitelist (AngleSharp), no evaluation of untrusted input | Injection through imported content | Malicious content rendered inside an already-compromised renderer |
| No listening ports in the desktop app, outbound traffic only on paths you trigger, a local-only sticky-note helper | Remote attack surface (A4) | Your own decisions about what to connect |
| CI gates, `SHA256SUMS`, pinned image digests, an SBOM, keyless Sigstore signatures (10.0+) | Release-channel tampering (A6) | A compromise of the platform or of the signing infrastructure itself |

## 7. Reporting

Found a gap between this page and the shipped product? That is a security report — use the private channel in [SECURITY.md](../SECURITY.md) section 2. A threat model that contradicts the software is a defect, and we would rather fix the page than defend it.
