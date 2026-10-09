# Code signing policy

<p align="center"><strong>English</strong> · <a href="CODE_SIGNING_POLICY.zh-CN.md">简体中文</a></p>

> See also: [Verifying your download](VERIFYING.md) — the checks you can run yourself. · [Build Provenance](PROVENANCE.md) — how a release is produced. · [Privacy Policy](../PRIVACY.md)

**Last updated:** 2026-10-08

Free code signing provided by SignPath.io, certificate by SignPath Foundation

The signing service is [SignPath.io](https://signpath.io/); the certificate is issued to [SignPath Foundation](https://signpath.org/), which vouches that a signed binary is an automated build of this repository's source.

## What is signed

`Novara_Setup_x.y.z.exe` — the Windows installer attached to the release page.

It is built by this repository's own CI workflow ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) from the tagged source, and every signing request is approved by hand for that release.

The programs inside the installer come from that same build. Binaries of upstream open-source projects (the .NET runtime, the Windows App SDK, Win2D) are redistributed unsigned — signing those is their maintainers' responsibility, not ours.

## Roles

Novara is maintained by a single account, which holds every role. The roles are the ones SignPath Foundation defines.

| Role | Who |
|---|---|
| Authors — may change the source without an additional review | [@Novara-owner](https://github.com/Novara-owner) |
| Reviewers — review changes proposed by anyone who is not a committer | [@Novara-owner](https://github.com/Novara-owner) |
| Approvers — decide, for each release, whether it may be signed | [@Novara-owner](https://github.com/Novara-owner) |

## Privacy

The project's privacy policy is [PRIVACY.md](../PRIVACY.md) ([简体中文](../PRIVACY.zh-CN.md)). In short: Novara is local-first, and it transfers no information to us or to any third party unless you ask it to.

## Status

The SignPath Foundation certificate covers the first installer published after this policy takes effect. Installers published before that are not Authenticode-signed and were not signed retrospectively — for those, check the SHA-256 and the Sigstore signature described in [VERIFYING.md](VERIFYING.md). Sigstore signatures ship with every release from 10.1 on.

## Related documents

- [Verifying your download](VERIFYING.md) — the checks themselves
- [Build Provenance](PROVENANCE.md) — the pipeline, what every release ships, and what we do not claim
- [PRIVACY.md](../PRIVACY.md) — what the application does with your data
- [LICENSE.md](../LICENSE.md) — MIT
