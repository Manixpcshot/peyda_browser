# Peyda Browser - Architecture

## Overview

Peyda is a layered WinUI 3 desktop browser. Rendering is delegated to the
**Microsoft Edge WebView2** runtime (Chromium). Peyda owns everything around
the engine: tabs, omnibox, history, bookmarks, downloads, profiles, themes,
permissions and updates.

```
┌─────────────────────────────────────────────────────────────┐
│ PeydaBrowser.App (net10.0-windows, WinUI 3, MVVM)           │
│  MainWindow · BrowserToolbar · BrowserTabView (WebView2)    │
│  Pages: NewTab / Settings / History / Bookmarks / Downloads │
└───────────────┬─────────────────────────────────────────────┘
                │ depends on
┌───────────────▼─────────────┐   ┌───────────────────────────┐
│ PeydaBrowser.WebEngine      │   │ PeydaBrowser.Infrastructure│
│ navigation & permission     │   │ profile paths, logging,    │
│ policies, IDN safety,       │   │ hashing, update checker    │
│ peyda:// routing            │   └─────────────┬─────────────┘
└───────────────┬─────────────┘                 │
┌───────────────▼─────────────┐   ┌─────────────▼─────────────┐
│ PeydaBrowser.Persistence    │   │ PeydaBrowser.Core          │
│ SQLite + SQL migrations,    ├──►│ models, omnibox/search,   │
│ repositories                │   │ settings/history/session   │
└─────────────────────────────┘   │ services, localization     │
┌─────────────────────────────┐   └───────────────────────────┘
│ PeydaBrowser.Theming        │        (Core/Theming: no deps)
│ JSON themes + validation    │
└─────────────────────────────┘
```

### Why this split?

- **Core** is framework-free and unit-tested (address-bar parsing, search URL
  building, settings sanitation, history grouping, SemVer, bookmark HTML).
- **Persistence** hides SQL behind Core interfaces; tests use real temp SQLite
  files, services use fakes.
- **WebEngine** contains *policies* (pure functions: "may this navigation
  proceed?", "how do we display this host?"). The WebView2 control itself must
  live in the App project (the XAML control requires the Windows TFM), but it
  contains no policy decisions.
- **App** is MVVM with CommunityToolkit.Mvvm; each window gets its own DI
  container bound to one profile (see "Profiles" below).

## Key decisions (ADRs)

### ADR-001: WebView2 (Evergreen) instead of forking Chromium or CEF
Chromium-forking is out of scope and a security liability for a small team.
The Evergreen WebView2 Runtime is serviced by Microsoft through Windows
Update/Edge channels, is pre-installed on Windows 11, and ships with a stable
.NET API. Trade-offs are listed under "Known WebView2 constraints" below.

### ADR-002: Hand-written SQL migrations instead of EF Core migrations
Storage is SQLite via `Microsoft.Data.Sqlite` with explicit, immutable,
versioned SQL migrations tracked by `PRAGMA user_version`
(`Persistence/Migrations/SchemaMigrations.cs`). Rationale: one small
dependency, deterministic schema, trivially testable upgrades
(v1→latest is covered by an integration test), and no EF tooling needed to
author migrations in CI. All queries are parameterized; the test suite
includes a SQL-injection-as-data case. EF Core remains possible later behind
the same repository interfaces.

### ADR-003: Internal pages are native XAML, not HTML in WebView2
`peyda://settings|history|downloads|bookmarks|about|newtab|private` are
routed *inside the app* (`InternalPageRouter`) and rendered as native pages.
Consequences: web content can never run script in an internal page, no custom
scheme handler is registered with the OS or the engine, and navigations from
web content to `peyda://` are cancelled unless they are main-frame,
user/app-initiated. The New Tab page is therefore fully offline.

### ADR-004: Localization via tested JSON catalogs
UI strings live in embedded JSON resources (`Strings.en.json`,
`Strings.fa.json`, 199 keys). A unit test enforces identical key sets and that
every generated `LocKeys.*` constant exists in both languages. This gives
runtime language switching, full RTL for Persian, `fa-IR` calendar dates, and
zero PRI/x:Uid fragility in unpackaged builds. The brand name "Peyda" is
identical in both languages; URLs always stay left-to-right.

### ADR-005: One DI container and one SQLite DB per profile/window
`AppServiceFactory.Build(ProfileContext)` composes persistence, services and
logging per profile. Profiles live in `%LOCALAPPDATA%\PeydaBrowser\Profiles\<id>\`
with separate `peyda-profile.db`, `WebView2\` user-data and `Logs\`. Private
and guest windows get an ephemeral context under `%TEMP%` that is deleted when
the window closes - the *same code paths* run, but history recording and
session persistence are disabled by policy, so nothing leaks into the real
profile.

### ADR-006: Updates are check-only in v1
`GitHubUpdateService` compares the running SemVer with GitHub Releases
(channel aware: stable/beta/nightly) and links to the release page. Peyda
never downloads or executes an installer by itself. `FileIntegrity`
(SHA-256 verification + `SHA256SUMS.txt` parsing) is implemented and tested
for a future, user-confirmed download flow.

### ADR-007: Unpackaged-first distribution
The app builds unpackaged (`WindowsPackageType=None`, self-contained).
CI turns the same publish output into: portable ZIP, a 7-Zip self-extracting
EXE (WinUI 3 cannot `PublishSingleFile`), an Inno Setup installer, and an
MSIX packed with `makeappx` from `installer/AppxManifest.xml`. See
[RELEASE.md](RELEASE.md).

## Data model

One SQLite database per profile. Tables: `Settings`, `SearchEngines`,
`History`, `Bookmarks`, `BookmarkFolders`, `Downloads`, `Sessions`,
`SessionTabs`, `ClosedTabs`, `Profiles`, `Themes`, `Shortcuts`,
`SitePermissions`. Times are stored as UTC Unix milliseconds; enums as text.
Passwords/tokens are never stored by Peyda (WebView2 password autosave is
disabled until a proper credential vault exists).

## Flow examples

**Typing in the omnibox** → `AddressBarParser` classifies (internal page /
URL / domain→https / bang search / search) → `BrowserTabViewModel` resolves
the search engine via `SearchEngineService` → engine template + URL-encoded
query → `BrowserTabView.NavigateTo` → `NavigationPolicy` gate → WebView2.

**Navigation inside the page** → `NavigationStarting` → `NavigationPolicy`
(allow / block / internal page / ask-external-app) → certificate errors,
permissions, pop-ups, downloads each have their own gate in `BrowserTabView`,
all delegating to `PeydaBrowser.WebEngine` policies and the per-profile
permission store.

## Known WebView2 constraints (honest list)

1. **Keyboard shortcuts vs. web content focus.** The XAML WebView2 control
   hosts the page in a child window. Peyda disables
   `AreBrowserAcceleratorKeysEnabled` and routes `KeyDown` events from the
   control plus window-level `KeyboardAccelerator`s through one shortcut
   table, but depending on focus, some chords may still be consumed by the
   page first. This is the main reason "Ctrl+T while a page has focus" can
   behave differently from Chrome. Mitigation path: host via the composition
   controller and `AcceleratorKeyPressed`.
2. **No true "in-private" profile flag via the XAML control.**
   `CoreWebView2Profile.IsInPrivateModeEnabled` is settable only through
   controller options, which the XAML wrapper does not expose. Peyda instead
   gives private windows a separate ephemeral user-data folder + database and
   deletes both on close - equivalent data isolation, different mechanism.
3. **Extensions.** WebView2 supports browser extensions only with
   restrictions and they cannot be installed from stores the way Edge does.
   Peyda 0.1 does not expose extension UI; this is a deliberate non-goal.
4. **Download pause/resume** maps to `CoreWebView2DownloadOperation`
   (`Pause`/`Resume`/`CanResume`); a download that the server cannot resume
   can only be re-started - the UI reflects `CanResume` honestly.
5. **No native zoom API on the XAML control.** Unlike the WPF WebView2
   control, `Microsoft.UI.Xaml.Controls.WebView2` has no `ZoomFactor` and
   hides the underlying controller. Peyda applies Chromium's CSS `zoom` on
   the document element and re-applies it after each navigation
   (`BrowserTabView.ZoomFactor`).
6. **Default-browser registration** and protocol/file associations need
   additional manifest capabilities and Settings-page UX; deferred post-0.1.
   `file://` is navigable only when typed by the user (pages cannot navigate
   frames to local files).

## Testing strategy

- **UnitTests** (fast, no UI): parser matrix, search builders, settings
  fallbacks, theme sanitation, history/session rules, SemVer/channel
  selection, policies, IDN safety, localization parity, download math,
  log redaction, SHA-256, repositories against temp SQLite.
- **IntegrationTests**: v1→latest migration keeping data, first-run seeding,
  profile isolation, private-context wipe, bookmark HTML round-trip across
  profiles, ranged history clearing, theme file round-trip.
- CI runs both on every push/PR; a release is only produced if everything -
  build, tests and packaging - succeeds.
