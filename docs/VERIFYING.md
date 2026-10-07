# Verifying your download

<p align="center"><strong>English</strong> · <a href="VERIFYING.zh-CN.md">简体中文</a></p>

You never need to take our word for anything on this page — every release ships the material to check it yourself.

## Windows installer (Novara_Setup_x.y.z.exe)

1. Download the installer **and** the `SHA256SUMS` attachment from the release page.
2. Compare the hash. In PowerShell:

   ```powershell
   Get-FileHash .\Novara_Setup_9.4.0.exe -Algorithm SHA256
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
docker inspect --format='{{index .RepoDigests 0}}' ghcr.io/novara-owner/novara-sync:9.4.0
```

The digest must match `IMAGES.txt`. The same digest is published to Docker Hub, so either source resolves to the same bytes.

## Signatures (Sigstore, 10.0+)

Releases from 10.0 on are signed with [Sigstore](https://www.sigstore.dev/) in keyless mode by this repository's own release workflow. There is no long-lived signing key to steal: each signature carries a short-lived certificate proving which workflow, in which repository, at which tag produced the bytes, and it is recorded in the public transparency log.

The Windows installer ships a `signature.bundle` attachment. Download it alongside the installer, then:

```bash
cosign verify-blob \
  --bundle signature.bundle \
  --certificate-identity-regexp '^https://github\.com/Novara-owner/Novara-Vault/\.github/workflows/ci\.yml@refs/tags/v' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com \
  Novara_Setup_10.0.0.exe
```

The container image is signed in the registry. Verify it by digest, with the same identity checks:

```bash
cosign verify \
  --certificate-identity-regexp '^https://github\.com/Novara-owner/Novara-Vault/\.github/workflows/ci\.yml@refs/tags/v' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com \
  ghcr.io/novara-owner/novara-sync@sha256:<digest-from-IMAGES.txt>
```

Both commands print the release tag the artifact was signed at. Get `cosign` from the [Sigstore releases](https://github.com/sigstore/cosign/releases). Releases up to and including 9.4.0 are not signed and were not signed retrospectively.

## SBOM (9.0+)

Releases include a Software Bill of Materials (SPDX JSON, generated with Syft) listing every component in the product. Open it with any SPDX viewer, or list package names:

```bash
jq -r '.packages[].name' novara-sync-9.4.0.spdx.json | sort -u
```

## OpenSSF Scorecard

The badge at the top of the README links to this repository's [OpenSSF Scorecard](https://scorecard.dev) — an automated security audit of repository practices (branch protection, dependency pinning, and so on), computed by the OpenSSF infrastructure, not by us.

## Code signing

The Windows installer is **not Authenticode-signed yet**: SmartScreen may show a warning on first run. Use "More info → Run anyway" only after verifying the SHA-256 and the Sigstore signature above. Free open-source Authenticode routes (SignPath Foundation / Certum) remain on the roadmap.
