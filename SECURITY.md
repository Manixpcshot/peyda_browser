# Security Policy

## Supported versions

Peyda Browser is pre-1.0. Security fixes are applied to the `main` branch and
shipped in the next nightly/alpha release; afterwards, only the latest stable
release line will be supported.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Instead, report privately through GitHub's
[private vulnerability reporting](https://github.com/Manixpcshot/peyda_browser/security/advisories/new)
("Report a vulnerability" on the repository's Security tab).

Please include:

- a description of the issue and its impact,
- steps to reproduce (a minimal proof of concept helps),
- affected version(s) and your Windows/WebView2 Runtime versions.

We aim to acknowledge reports within 5 working days and to agree on a
disclosure timeline with the reporter. Coordinated disclosure is appreciated.

## Scope notes

- Rendering-engine (Chromium) vulnerabilities are handled upstream by the
  Microsoft Edge WebView2 Runtime, which updates independently - keeping it
  up to date matters more than any single Peyda release.
- The detailed browser security model (navigation policy, permissions,
  certificate handling, downloads, logging rules) is documented in
  [docs/SECURITY.md](docs/SECURITY.md).
