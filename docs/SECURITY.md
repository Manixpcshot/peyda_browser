# Peyda Browser security model

This document describes how Peyda makes security decisions. For reporting a
vulnerability, see [/SECURITY.md](../SECURITY.md).

## Engine

- Rendering uses the **Evergreen Microsoft Edge WebView2 Runtime**; engine
  security updates arrive with the runtime, independent of Peyda releases.
  The app shows the detected runtime version on `peyda://about` and degrades
  gracefully (native crash page) if the runtime is missing.
- WebView2 settings hardening: `AreHostObjectsAllowed = false`,
  `IsWebMessageEnabled = false` (Peyda injects no script into pages and
  consumes no page-posted messages - there is no message bridge to validate,
  because the surface does not exist), `IsStatusBarEnabled = false`,
  DevTools only when the user enables it in Settings (F12 included),
  password autosave off until a real credential store exists.

## Navigation policy (single gate: `NavigationPolicy.Decide`)

Every main-frame navigation and new-window request is classified before the
engine proceeds:

- `http`, `https`, `about`, `data`, `blob` → **allow**
- `peyda://` → open the mapped **native** internal page (main frame only;
  sub-frame attempts are blocked)
- `javascript:`, `vbscript:`, `ms-msdt:`, `search-ms:` and other dangerous
  schemes → **block**; `file://` is allowed **only** for addresses the user
  typed explicitly, never for page-initiated navigation
- anything else (`mailto:`, `tel:`, app protocols…) → cancel + ask the user
  with a dialog before handing it to Windows (`Launcher.LaunchUriAsync`)

Pop-ups: `NewWindowRequested` is always intercepted. User-initiated window
opens become new tabs (configurable); unsolicited ones are dropped when
"block pop-ups" is on (default).

## Certificates

`ServerCertificateErrorDetected` defaults to **Deny**. The user sees an
explanation dialog with host + error; only an explicit "Proceed anyway
(unsafe)" sets `AlwaysAllow` for that flow. Nothing proceeds silently.

## Site permissions

Camera, microphone, geolocation, notifications and clipboard read always
start at **Ask**. The dialog shows the exact origin and capability; "remember
for this site" stores the decision in the profile's `SitePermissions` table
(never in private windows) and decisions can be reviewed/revoked under
Settings → Privacy. Peyda sets `SavesInProfile = false` so WebView2 does not
keep a second, invisible permission store.

## Downloads

- Nothing executes automatically - ever. Peyda renders its own download UI
  (`Handled = true`), chooses the target path (settings folder or an
  explicit save picker), and de-duplicates file names.
- Executable extensions and deceptive double extensions
  (`invoice.pdf.exe`) are flagged (`DangerousFileClassifier`); opening such a
  file from the Downloads page requires an extra confirmation.
- SHA-256 of every completed file is computed and stored with the download
  record.

## Data at rest

- SQLite via `Microsoft.Data.Sqlite`; **parameterized queries only** (no
  string-built SQL), foreign keys on, WAL journaling, versioned migrations.
- One database + WebView2 user-data folder per profile; profile ids are
  sanitized before becoming paths (no traversal). Ephemeral
  private/guest data is created under `%TEMP%` and deleted on window close.
- Peyda stores no passwords or tokens. Themes/settings/profile index files
  are parsed defensively: corrupt input falls back to defaults instead of
  crashing (covered by tests).
- Logs pass through `SensitiveDataRedactor` (password/token/cookie/
  Authorization patterns) and code rules forbid logging such data at all;
  logs are local files, never uploaded.

## Address-bar trust display

- Internationalized domains are shown in Unicode only when the host uses a
  single writing system; mixed-script hosts (Latin+Cyrillic etc.) are shown
  as punycode to avoid homograph phishing (`UrlDisplaySafety`).
- The omnibox itself never navigates to non-HTTP(S) schemes: unknown schemes
  become search queries, and domain detection requires a plausible host
  (validated labels/TLD or an IP/`localhost`).

## Supply chain / CI

- Pinned, stable NuGet versions via Central Package Management
  (`Directory.Packages.props`); Dependabot watches NuGet and GitHub Actions.
- CodeQL analysis on push/PR + weekly schedule.
- GitHub Actions run with least-privilege `permissions:`; the only job that
  can write releases is `release.yml`'s publish job.
- Signing certificates exist only as GitHub Secrets and are materialised to
  `%TEMP%` during packaging; `.gitignore` blocks `*.pfx/*.key/*.pem` etc.
- Every release ships `SHA256SUMS.txt`; unsigned/dev-signed status is stated
  in the release notes (see [RELEASE.md](RELEASE.md)).

## Out of scope / honest limitations

- Peyda is not a hardened anti-tracking browser: it does not block trackers
  or fingerprinting beyond what Chromium/WebView2 does by default.
- Private windows protect against local traces, not network observers.
- See "Known WebView2 constraints" in [ARCHITECTURE.md](ARCHITECTURE.md).
