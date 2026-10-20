# Roadmap

English · [简体中文](ROADMAP.zh-CN.md)

This is the working roadmap for Novara. Dates are estimates and the order may shift; what actually shipped is always in [CHANGELOG.md](CHANGELOG.md) and the [Releases](https://github.com/Novara-owner/Novara-Vault/releases) page.

## 11.0 — Hardened (current work)

The foundation moves to **.NET 10 (LTS)**, and the release hardens the features that already exist. No new feature areas: this release is deliberately about a deeper base and a stricter security posture. Representative work:

- Runtime migration of the desktop application to .NET 10.
- Update-channel signing: the update manifest is verified against an embedded ECDSA P-521 public key before anything is parsed (fail-closed).
- A dedicated detection-history store for the API connectivity probes.
- Transport-failure backoff and same-round retries in the sync engine.
- Stricter key-derivation defaults for the local database (Argon2id support with self-describing headers), plus integrity hardening of the storage format.
- Content-Security-Policy hardening for the editor and reader surfaces, snapshot-integrity manifests, and generation-based retention for backups.
- Partition-level incremental sync (container v5) with a generation lock that protects legacy clients.

## 12.0 — Vault & Time Capsule (designed, next)

A product spec is already written for a vault-and-time-capsule feature: sealed, encrypted storage for particularly sensitive entries, and entries that unlock at a chosen future date. Implementation follows the spec; details will be documented when the release ships.

## Later candidates (no dates)

- **Signing**: completing the move from the SignPath Foundation pipeline to a standing release-signing arrangement.
- **Distribution**: Windows Package Manager (winget) submission.
- **Badge work**: the OpenSSF Best Practices Silver items, and a review of what Gold would require.
- **Sync**: iterating on the incremental protocol introduced in 11.0 (client breadth, tooling, self-hosting ergonomics).

---

*Last updated: 2026-10-13*
