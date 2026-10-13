# Quick Start

English · [简体中文](QUICK_START.zh-CN.md)

Five steps from download to daily use. Novara is local-first: everything stays in a single data file on your machine, and nothing is sent anywhere unless you ask it to.

## 1. Download and install

Grab the latest installer from the [Releases](https://github.com/Novara-owner/Novara-Vault/releases) page (or from the official site linked in [README.md](../README.md)). Verify the download if you like — [docs/VERIFYING.md](VERIFYING.md) describes the SHA-256, Sigstore and Authenticode checks. Run the installer; no administrator rights are needed beyond the usual UAC prompt.

## 2. Walk through the first-launch wizard

The first start shows a short welcome wizard (seven pages) that introduces the tabs, the privacy lock and the data model. You can replay it later from Settings → Welcome page.

## 3. Set a privacy lock (recommended)

Settings → Privacy access lock lets you set a lock password. With the lock on, the data file is encrypted with AES-256-GCM and the app locks itself after inactivity. There is no password recovery by design, so choose a password you will remember. Windows Hello can be enabled afterwards as a convenience gate.

## 4. Learn the four tabs

- **Memo** — grouped entries for accounts, keys and notes; the most sensitive zone.
- **File path** — quick access to folders and documents you keep coming back to.
- **Plan** — todo cards with subtasks and sticky notes; notes can be pinned to the desktop.
- **Records** — diary and documents with a rich editor, full-text search and a recycle bin.

Ctrl+K opens global search from anywhere; the Settings page footer shows your version.

## 5. Know where your data lives

Everything lives in `%LocalAppData%\Novara` (`data.novadb` plus the lock file). Tools → Data backup exports an encrypted `.novaenc` backup — keep one off the machine. Tools → Security snapshot produces a shareable encrypted snapshot; the sync features (self-hosted) and the MCP agent interface are optional and documented in [docs/MCP.md](MCP.md) and [docs/BUILD.md](BUILD.md).

For development and self-hosting, see [docs/BUILD.md](BUILD.md).
