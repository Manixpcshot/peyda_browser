# Peyda Browser · مرورگر پیدا

**Your Web, Your Way** · **وب، آن‌طور که تو می‌خواهی**

Peyda is a fast, modern and highly customizable web browser for Windows 10 and
Windows 11. It is built with **WinUI 3** and uses the **Microsoft Edge WebView2**
runtime (Chromium) as its rendering engine - Peyda does not fork or reimplement
a browser engine.

> ⚠️ **Alpha software.** Peyda Browser is in early development (0.1.0-alpha).
> It is usable for everyday browsing, but expect bugs and missing features.
> Release binaries are currently **not code-signed** - always verify the
> `SHA256SUMS.txt` published with each [GitHub Release](https://github.com/Manixpcshot/peyda_browser/releases).

## Features

- **Real multi-tab browsing** - drag-to-reorder tabs, pinning, mute, duplicate,
  reopen-closed-tab, per-tab favicons/loading/crash pages, session restore.
- **Smart address bar** - understands URLs, domains and search terms, with
  history/bookmark suggestions, `!g` (Google) / `!b` (Brave) / `!d`
  (DuckDuckGo) bangs and an IDN homograph-conscious host display.
- **Your search, your choice** - Google, Brave Search and DuckDuckGo built in;
  add your own engines with `{query}` URL templates. No search APIs or keys:
  Peyda just opens the engine's public results page.
- **Deeply customizable** - JSON themes (import/export), accent color, density,
  corner radius, light/dark/system, bookmarks bar, New Tab background, clock,
  greeting, shortcuts and more.
- **English + فارسی** - full Persian UI with right-to-left layout and Persian
  calendar dates on the New Tab page.
- **Bookmarks, history, downloads** - folders, bookmarks bar, HTML
  import/export, grouped searchable history, download manager with
  pause/resume and SHA-256 verification of completed files.
- **Private windows** - ephemeral storage that is wiped on close. (Private
  browsing does **not** make you anonymous online.)
- **Privacy by default** - no telemetry, no tracking, no accounts. The update
  checker only links to the official GitHub release page; nothing installs
  itself.
- **Security gates** - certificate errors are blocked until you decide,
  site permissions (camera, microphone, location, notifications, clipboard)
  always ask first, external app protocols require confirmation, downloaded
  executables never run automatically.

## Screenshots

Screenshots will be added here after the first public alpha build is published.
(We do not publish mock-ups as screenshots.)

## Supported systems & requirements

| Item | Requirement |
| --- | --- |
| OS | Windows 10 version 2004 (10.0.19041) or newer, Windows 11 |
| Architecture | x64 (ARM64 packages are also produced by CI) |
| Runtime | **Microsoft Edge WebView2 Runtime** - pre-installed on Windows 11 and current Windows 10; the Setup installer checks for it and links to Microsoft's official download page |
| .NET | Nothing to install for release builds (self-contained) |

## Install

Download from the [latest release](https://github.com/Manixpcshot/peyda_browser/releases/latest):

- `PeydaBrowser-Setup-{version}-win-x64.exe` - installer with uninstaller
  (recommended). It keeps your profiles/settings when upgrading and asks
  before deleting them on uninstall.
- `PeydaBrowser-{version}-win-x64-portable.zip` - portable, no installation.
- `PeydaBrowser-{version}-win-x64.exe` - single-file self-extracting portable app.
- `PeydaBrowser-{version}-win-x64.msix` - Windows app package. Dev-signed
  builds require trusting the included development certificate first.

Verify your download (PowerShell):

```powershell
Get-FileHash .\PeydaBrowser-Setup-0.1.0-alpha-win-x64.exe -Algorithm SHA256
# compare with SHA256SUMS.txt from the same release
```

## Build from source

```powershell
git clone https://github.com/Manixpcshot/peyda_browser.git
cd peyda_browser
dotnet restore PeydaBrowser.sln
dotnet build   PeydaBrowser.sln -c Debug -p:Platform=x64
dotnet test    tests/PeydaBrowser.UnitTests/PeydaBrowser.UnitTests.csproj
dotnet publish src/PeydaBrowser.App/PeydaBrowser.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Prerequisites: .NET 10 SDK, Windows 10/11, WebView2 Runtime. See
[docs/BUILDING.md](docs/BUILDING.md) for the full guide (including how the
installer and MSIX are produced).

## Project layout

```
src/
  PeydaBrowser.Core/           Domain logic: omnibox, search, settings,
                               history/bookmarks/sessions, shortcuts,
                               localization, update selection (framework-free)
  PeydaBrowser.Theming/        JSON theme model + validation + built-in themes
  PeydaBrowser.Persistence/    SQLite (Microsoft.Data.Sqlite), migrations, repositories
  PeydaBrowser.WebEngine/      Navigation/permission/URL-display policies
  PeydaBrowser.Infrastructure/ Profiles & paths, safe file logging, hashing,
                               GitHub update checker
  PeydaBrowser.App/            WinUI 3 shell, WebView2 hosting, MVVM UI
tests/                         xUnit unit + integration tests
installer/                     Inno Setup script, MSIX manifest, packaging scripts
.github/workflows/             build, test, CodeQL and automated release pipelines
docs/                          Architecture, building, releasing, theming, security
```

Architecture decisions (including *why not EF Core migrations* and *why
internal pages are native XAML*) are recorded in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Roadmap

- **0.1.x (alpha)** - stabilization, screenshots, ARM64 validation, bug fixing.
- **0.2.0 (beta)** - vertical tabs, full profile-management UI, custom keyboard
  shortcuts UI, find-in-page, default-browser registration.
- **1.0.0** - signed releases, polished first-run experience, accessibility pass.

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) and
the [Code of Conduct](CODE_OF_CONDUCT.md). Security reports: see
[SECURITY.md](SECURITY.md) (please do not open public issues for vulnerabilities).

## Privacy

Peyda collects **nothing**. There is no telemetry, no advertising ID, no
third-party analytics. History and settings stay in a local SQLite database
under `%LOCALAPPDATA%\PeydaBrowser`. Details: [PRIVACY.md](PRIVACY.md).

## License

MIT - see [LICENSE](LICENSE).

Peyda Browser is an independent project. It is not affiliated with Google,
Brave, DuckDuckGo or Microsoft; those names are used only to identify the
search engines and the WebView2 runtime that users can choose.

---

## مرورگر پیدا (فارسی)

«پیدا» یک مرورگر مدرن، سریع و بسیار قابل شخصی‌سازی برای ویندوز ۱۰ و ۱۱ است که
با WinUI 3 ساخته شده و از موتور Microsoft Edge WebView2 (کرومیوم) استفاده می‌کند.

- چند زبانه واقعی با سنجاق‌کردن، بی‌صدا کردن، تکثیر و بازیابی نشست
- نوار آدرس هوشمند با تشخیص آدرس، دامنه و عبارت جستجو و میانبرهای `!g` و `!b` و `!d`
- موتورهای جستجوی گوگل، Brave و DuckDuckGo + امکان افزودن موتور دلخواه
- تم‌های JSON با قابلیت وارد/خارج کردن، رنگ تأکیدی، حالت روشن/تیره و صفحه شروع قابل شخصی‌سازی
- رابط کاربری کامل فارسی و راست‌به‌چپ، یا انگلیسی
- تاریخچه، نشانک‌ها (با ورود/خروج HTML)، مدیریت دانلود و پنجره خصوصی
- بدون هیچ‌گونه جمع‌آوری داده (Telemetry) — همه‌چیز روی دستگاه شما می‌ماند

> این پروژه در مرحله **آلفا** است. نسخه‌های فعلی امضای دیجیتالی ندارند؛ پیش از
> نصب، چک‌سام SHA-256 منتشرشده در صفحه Release را بررسی کنید.

دانلود: [آخرین نسخه در GitHub Releases](https://github.com/Manixpcshot/peyda_browser/releases/latest) ·
ساخت از سورس: [docs/BUILDING.md](docs/BUILDING.md) ·
مشارکت: [CONTRIBUTING.md](CONTRIBUTING.md)
