# Contributing to Novara

Thanks for taking the time to look. Novara is maintained by a single author, so the process is deliberately small and explicit.

## Before you start

- **Security issues** — do not open a public issue. Follow [SECURITY.md](SECURITY.md) and report privately.
- **Questions and bug reports** — open a GitHub issue. Include what you did, what happened, and what you expected.
- **Code changes** — open an issue describing the problem first. A small, focused change is far easier to review and land than a broad rewrite.

## Reporting a bug

Please include:

- Steps to reproduce, the actual result, and the expected result.
- Your Windows version and the Novara version (the Settings page footer shows it).
- Relevant log lines from `%LocalAppData%\Novara\logs\`.
- Any `crash-*.txt` file, if one was produced — the stack traces keep file and line numbers, so they are genuinely useful.

## Submitting a change

1. Fork the repository and branch from `main`.
2. Keep the change to a single concern, and follow the style of the surrounding code.
3. Make the build and the tests pass, and keep line coverage at or above the 80% floor that CI enforces.
4. Open a pull request against `main`. Describe the problem, the change, and how you verified it — the pull request template asks for exactly that.

Build prerequisites and the full build are in [docs/BUILD.md](docs/BUILD.md); the project layout and conventions are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Licence

By contributing, you agree that your contribution is licensed under the terms of [LICENSE.md](LICENSE.md).
