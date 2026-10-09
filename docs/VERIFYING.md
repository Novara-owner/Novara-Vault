# Verifying your download

<p align="center"><strong>English</strong> · <a href="VERIFYING.zh-CN.md">简体中文</a></p>

> See also: [Build Provenance](PROVENANCE.md) — how a release is actually produced, and what we do not claim.

You never need to take our word for anything on this page — every release ships the material to check it yourself.

## Windows installer (Novara_Setup_x.y.z.exe)

1. Download the installer **and** the `SHA256SUMS` attachment from the release page.
2. Compare the hash. In PowerShell:

   ```powershell
   Get-FileHash .\Novara_Setup_10.0.0.exe -Algorithm SHA256
   ```

   The value must match the line in `SHA256SUMS`. If you have `sha256sum` (Git Bash, Linux), place the installer next to `SHA256SUMS` and run:

   ```bash
   sha256sum -c SHA256SUMS
   ```

## Docker image (self-hosted server, 9.0+)

Every server release pins its image digests in an `IMAGES.txt` release attachment. Pulling **by digest** pins the exact bytes regardless of tags:

```bash
docker pull ghcr.io/novara-owner/novara-sync@sha256:<digest-from-IMAGES.txt>
```

To check an image you already pulled:

```bash
docker inspect --format='{{index .RepoDigests 0}}' ghcr.io/novara-owner/novara-sync:10.0.0
```

The digest must match `IMAGES.txt`. The same digest is published to Docker Hub, so either source resolves to the same bytes.

## Signatures (Sigstore, 10.1+)

Releases from 10.1 on are signed with [Sigstore](https://www.sigstore.dev/) in keyless mode by this repository's own release workflow. **10.0.0 shipped without a signature bundle** — the signing job failed on its first run on release day and has since been fixed in the build workflow; the first actually signed release is the next tag. There is no long-lived signing key to steal: each signature carries a short-lived certificate proving which workflow, in which repository, at which tag produced the bytes, and it is recorded in the public transparency log.

From 10.1 on, the Windows installer ships a `signature.bundle` attachment. Download it alongside the installer, then:

```bash
cosign verify-blob \
  --bundle signature.bundle \
  --certificate-identity-regexp '^https://github\.com/Novara-owner/Novara-Vault/\.github/workflows/ci\.yml@refs/tags/v' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com \
  Novara_Setup_10.1.0.exe
```

The container image is signed in the registry. Verify it by digest, with the same identity checks:

```bash
cosign verify \
  --certificate-identity-regexp '^https://github\.com/Novara-owner/Novara-Vault/\.github/workflows/ci\.yml@refs/tags/v' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com \
  ghcr.io/novara-owner/novara-sync@sha256:<digest-from-IMAGES.txt>
```

Both commands print the release tag the artifact was signed at. Get `cosign` from the [Sigstore releases](https://github.com/sigstore/cosign/releases). Releases up to and including 10.0.0 are not signed and were not signed retrospectively.

## SBOM (9.0+)

Releases include a Software Bill of Materials (SPDX JSON, generated with Syft) listing every component in the product. Open it with any SPDX viewer, or list package names:

```bash
jq -r '.packages[].name' novara-sync-10.0.0.spdx.json | sort -u
```

## OpenSSF Scorecard

The badge at the top of the README links to this repository's [OpenSSF Scorecard](https://scorecard.dev) — an automated security audit of repository practices (branch protection, dependency pinning, and so on), computed by the OpenSSF infrastructure, not by us.

## Code signing

The Windows installer is **not Authenticode-signed yet**: SmartScreen may show a warning on first run. Use "More info → Run anyway" only after verifying the SHA-256 — and, for 10.1 and later, the Sigstore signature above. How the installer is signed once that changes — and who approves each release — is written down in the [code signing policy](CODE_SIGNING_POLICY.md).
