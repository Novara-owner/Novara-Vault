# Build Provenance

<p align="center"><strong>English</strong> · <a href="PROVENANCE.zh-CN.md">简体中文</a></p>

> See also: [Verifying your download](VERIFYING.md) — the commands that check a release against this page. · [Threat Model](threat-model.md) — assets, adversaries, assumptions and non-goals. · [SECURITY.md](../SECURITY.md)

**Last updated:** 2026-10-08 · **Applies to:** Novara 10.0 and later, where marked

## What this page is

This is the single source for **how a Novara release is produced**: what the pipeline runs, what every release ships, how the build is pinned, and — just as important — what we do **not** claim. [VERIFYING.md](VERIFYING.md) tells you *how to check* a download; this page tells you *what you are checking against*.

## The pipeline

Releases are produced by the CI workflow in this repository ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) on a version tag. One stage still runs outside it: the Windows installer is packaged on the release machine (see below).

| Stage | What it does | Can it block the release? |
|---|---|---|
| Build and test | Restores in locked mode, builds the core and server projects in Release, runs the full unit-test suite, and enforces a 80% line-coverage floor on the test run | Yes |
| Dependency and license audit | Scans every project for known vulnerabilities and **fails the run on any Critical advisory**; records High advisories for manual review; verifies the committed third-party license inventory | Yes (Critical) |
| Desktop build | Builds the WinUI desktop application | Yes |
| Release artifacts | Publishes the self-contained sync-server binary | Yes |
| Sign release artifacts | **10.0+** signs the container image and the installer (see below) | Yes |

The Windows installer is packaged on the release machine from the **same sources**, attached to the release page, and then signed by the CI job — the signature is produced by the pipeline, not by hand. Installers signed under the [code signing policy](CODE_SIGNING_POLICY.md) are built by the CI workflow from the tagged source.

## What every release ships

| Asset | What it pins |
|---|---|
| `Novara_Setup_x.y.z.exe` | The Windows installer |
| `SHA256SUMS` | The SHA-256 of the installer |
| `IMAGES.txt` | The exact container image digests — the same digest on GHCR and Docker Hub |
| `novara-sync-x.y.z.spdx.json` | A Software Bill of Materials (SPDX, generated with Syft) |
| `signature.bundle` | **10.0+** the Sigstore signature bundle for the installer |

## How the build is pinned (10.0 and later)

- **SDK version** is pinned exactly in `global.json` with roll-forward disabled, so the compiler and the SDK-provided build tools cannot drift silently.
- **Dependencies** are pinned by committed NuGet lock files and CI restores in **locked mode**: if the resolved graph does not match the lock files, the restore fails instead of quietly resolving something else.
- **Source paths** are mapped to a neutral root in release builds, so published binaries do not carry the build machine's directory layout.
- **Release artifacts are reproducible at Level 1** — same frozen commit, same environment, byte-identical output. The release procedure requires building twice and comparing every file, and compiling the installer twice and comparing hashes, before anything is published.

## What we do NOT claim

We would rather state the limits than let you assume more than we can show.

- **Not reproducible from this public repository.** This repository is a *release snapshot*: comments are stripped and a few machine-specific build settings are removed before publishing. Stripping comments does not change compiled code, but the sanitizer also rewrites files that ship **inside the installer** (the helper `.cmd` files), so a build from the public tree cannot reproduce the published bytes. Reproduction is defined against **the frozen source commit in our development repository**, and each release records its toolchain versions.
- **Not reproducible across machines.** Level 1 means *same environment*. A different Visual Studio / Windows SDK installation supplies different XAML and resource compilers, which changes the output. We would rather record the toolchain than pretend it does not matter.
- **The installer is not Authenticode-signed yet**, so SmartScreen may still warn on first run — see [VERIFYING.md](VERIFYING.md).
- **The shipped binaries contain debug information.** We ship `.pdb` files next to the executable so that a crash report from your machine can name the exact source line. Release builds map source paths to a neutral root, but the debug metadata still records the build's own file layout.
- **No independent audit.** The OpenSSF Scorecard is automated, and our threat model is our own. Neither is a third-party security audit.

## Signing (10.0 and later)

Container images and installers are signed with [Sigstore](https://www.sigstore.dev/) in **keyless** mode, by the same workflow that produces the release. There is no long-lived signing key to steal: each signature carries a short-lived certificate that pins the repository, the workflow file and the tag, and it is recorded in the public transparency log. The verification commands are in [VERIFYING.md](VERIFYING.md).

## Related documents

- [Verifying your download](VERIFYING.md) — the checks themselves
- [Threat Model](threat-model.md) — what the product protects, and what it does not
- [SECURITY.md](../SECURITY.md) — reporting a vulnerability, supported versions, developer commitments
- [DEVELOPMENT.md](DEVELOPMENT.md) — how the application is built internally
- [BUILD.md](BUILD.md) — building it yourself from source
