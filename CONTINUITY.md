# Continuity

English · [简体中文](CONTINUITY.zh-CN.md)

Novara is maintained by one person, and this page states, in plain terms, how the project keeps working if that maintainer suddenly becomes unable to act — the OpenSSF Best Practices "access continuity" requirement, met with a real arrangement rather than an intention.

## The arrangement

A sealed set of credentials is entrusted to a designated trusted custodian, kept **offline on an encrypted, dedicated USB drive**. The set covers everything required to keep the project alive:

- **The GitHub account** of the maintainer (repositories, releases, issues, CI settings, signing-key registrations).
- **The registrar account for the `novara.xin` domain** (the official site and download endpoint).
- **The mailbox `owner@novara.xin`** (security reports routed through it stay reachable).
- **The signing material** — the update-manifest signing key and the SSH tag-signing key registration, so releases remain verifiable and publishable.

## What the custodian can do

With that set, the custodian can — within a week of the maintainer becoming unable to act — create and close issues, review and accept changes, publish releases with signed artifacts, and keep the download endpoint and the reporting mailbox alive. The roles this maps to are defined in [docs/CODE_SIGNING_POLICY.md](docs/CODE_SIGNING_POLICY.md), and the governance model is in [GOVERNANCE.md](GOVERNANCE.md).

## Boundaries of the statement

- The custodian is a person, not an automation; the week window is a commitment, not a service level.
- The sealed set is refreshed whenever any of the covered credentials changes.
- The custodian acts in the project's interest and under the project's published policies (licence, privacy, security); no third party gains standing access to user data — there is no user data on any project-operated service to begin with.

## Changes to this document

The maintainer updates this page whenever the arrangement changes, in the same way as [GOVERNANCE.md](GOVERNANCE.md) — a dated, self-contained commit.

---

*Last updated: 2026-10-13*
