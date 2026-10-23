# Privacy Policy - Peyda Browser

_Last updated: 2026-10-23 · Applies to Peyda Browser 0.1.0-alpha and later
unless a newer version of this document says otherwise._

## The short version

Peyda Browser does not collect, sell, or share your personal data. There is no
telemetry, no analytics, no advertising identifier, and no account system.
Your browsing data stays on your device.

## What Peyda stores locally (on your device only)

To work as a browser, Peyda stores the following under
`%LOCALAPPDATA%\PeydaBrowser` in a local SQLite database and in the WebView2
profile folder:

- browsing history (URL, page title, visit time/count) - can be disabled in
  Settings → Privacy, and is never recorded in private windows;
- bookmarks, downloads list, open-tab sessions, recently closed tabs;
- your settings, search engines, themes, New Tab shortcuts, site permissions;
- cookies, cache and site data (managed by the WebView2/Chromium engine, per
  profile).

None of this leaves your device except through normal web browsing itself
(for example, cookies are sent only to the sites that set them).

## Network requests Peyda itself makes

- **Web pages you visit** and **search queries** go directly to the website /
  the search engine you selected (Google, Brave Search, DuckDuckGo, or your
  own). Peyda has no search proxy and sees nothing extra - but those services
  have their own privacy policies.
- **Favicon requests** for sites you bookmark or pin as shortcuts go directly
  to those sites.
- **Update check (optional, on by default)**: Peyda asks
  `api.github.com` for the public release list of this repository to tell you
  whether a newer version exists. No identifier and no browsing data is sent;
  the request only reveals your IP address to GitHub, like any web request.
  Downloads never start automatically. You can turn this off in
  Settings → Privacy → "Check for updates automatically".
- **Crash reporting**: not implemented. The settings toggle exists for a
  future, fully opt-in implementation; in current builds nothing is sent even
  if you turn it on, and the UI says exactly that.

## Private windows

Private windows use a temporary profile: history, cookies, sessions and site
data are kept in a temp folder and deleted when the window closes; Peyda's
normal profile database is never written. Private browsing does **not** make
you anonymous: websites, your network provider, and your employer/school can
still observe your traffic. Peyda makes no claim to prevent tracking by the
sites you visit.

## Your controls

- Settings → Privacy: disable history, block unsolicited pop-ups, manage
  per-site permissions, clear history/cookies/cache/site data for a chosen
  time range.
- Uninstalling via the Setup uninstaller asks whether local profile data
  should also be deleted.

## Changes

If this policy changes, the updated document will be committed to this
repository and noted in the changelog before the change ships in a release.
