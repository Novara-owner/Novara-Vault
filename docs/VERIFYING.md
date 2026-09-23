# Verifying your download

You never need to take our word for anything on this page — every release ships the material to check it yourself.

## Windows installer (Novara_Setup_x.y.z.exe)

1. Download the installer **and** the `SHA256SUMS` attachment from the release page.
2. Compare the hash. In PowerShell:

   ```powershell
   Get-FileHash .\Novara_Setup_8.0.0.exe -Algorithm SHA256
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
docker inspect --format='{{index .RepoDigests 0}}' ghcr.io/novara-owner/novara-sync:9.0.0
```

The digest must match `IMAGES.txt`. The same digest is published to Docker Hub, so either source resolves to the same bytes.

## SBOM (9.0+)

Releases include a Software Bill of Materials (SPDX JSON, generated with Syft) listing every component in the product. Open it with any SPDX viewer, or list package names:

```bash
jq -r '.packages[].name' novara-9.0.0.spdx.json | sort -u
```

## OpenSSF Scorecard

The badge at the top of the README links to this repository's [OpenSSF Scorecard](https://scorecard.dev) — an automated security audit of repository practices (branch protection, dependency pinning, and so on), computed by the OpenSSF infrastructure, not by us.

## Code signing

The Windows installer is **not code-signed yet**: SmartScreen may show a warning on first run. Use "More info → Run anyway" only after verifying the SHA-256 above. Free open-source signing routes (SignPath Foundation / Certum) and keyless artifact signing (Sigstore/cosign) for the container images are on the roadmap.
