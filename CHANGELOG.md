# Changelog

All notable changes to Peyda Browser are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0-alpha] - 2026-10-23

First public alpha. **This is early software**: expect rough edges, and do not
rely on it as your only browser yet. Builds are currently **not code-signed**;
verify `SHA256SUMS.txt` from the GitHub Release before installing.

### Added
- WinUI 3 (Windows App SDK) browser shell for Windows 10/11 with a custom
  title bar, light/dark themes, right-to-left (Persian) layout support.
- Microsoft Edge WebView2 (Chromium) rendering, one isolated control per tab.
- Tabs: create/close/reorder (drag & drop), duplicate, pin/unpin, mute/unmute,
  close others / close to the right, reopen closed tab, favicons, loading and
  crash states, session restore.
- Omnibox: URL vs. search detection, domain auto-https, `!g` / `!b` / `!d`
  bang searches, history + bookmark suggestions, paste-and-go / paste-and-search,
  HTTPS + IDN homograph-aware host display.
- Search engines: Google, Brave Search and DuckDuckGo built in; add/edit/delete/
  reorder custom engines with `{query}` URL templates; per-tab session engine.
- Native local New Tab page (clock, date, greeting, search, shortcuts,
  background image) - works fully offline.
- History (grouped, searchable, ranged clearing), bookmarks (folders, bar,
  HTML import/export), downloads (progress, speed, pause/resume/cancel,
  SHA-256 of completed files, warnings for executables).
- Private windows with ephemeral storage; profile architecture with isolated
  storage per profile; guest profile support in the profile manager.
- Customization: themes as validated JSON (4 built in, import/export), accent,
  density, corner radius, bookmark-bar/sidebar visibility, home page, startup
  behaviour, download folder.
- English and Persian (فارسی) UI, 199 localized strings, full RTL for Persian.
- SQLite storage with parameterized queries and versioned migrations.
- Security: certificate-error interstitial (deny by default), per-site
  permission prompts (camera/mic/location/notifications/clipboard), pop-up
  control, external-protocol confirmation, blocked dangerous schemes,
  no script injection, no host objects, secret-redacting logs.
- Privacy: no telemetry, no tracking; optional update *checker* that only
  links to the official GitHub release page.
- 130+ automated tests (xUnit) and GitHub Actions (build, test, CodeQL,
  Dependabot, automated releases).
- Packaging: portable ZIP, self-extracting EXE, Inno Setup installer with
  uninstaller + WebView2 Runtime check, MSIX package, source archives and
  SHA256SUMS on every release.

### Known limitations
- Vertical tabs are not implemented yet (the setting is reserved).
- Some keyboard shortcuts only trigger when keyboard focus is outside web
  content (WebView2 XAML hosting limitation; see docs/ARCHITECTURE.md).
- No built-in password manager/autofill UI; WebView2 password autosave is off.
- Not yet a registered default browser; `file://` pages open only when typed.
- Builds are unsigned until a code-signing certificate is configured in CI.

[Unreleased]: https://github.com/Manixpcshot/peyda_browser/compare/v0.1.0-alpha...HEAD
[0.1.0-alpha]: https://github.com/Manixpcshot/peyda_browser/releases/tag/v0.1.0-alpha
