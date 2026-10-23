# Building Peyda Browser

## Prerequisites

| Tool | Why | Get it |
| --- | --- | --- |
| Windows 10 2004+ / Windows 11 (x64 or ARM64) | WinUI 3 is Windows-only | - |
| .NET SDK 10.0.x (LTS) | build & test | <https://dotnet.microsoft.com/download> |
| Microsoft Edge WebView2 Runtime (Evergreen) | running the app | pre-installed on Win11; <https://developer.microsoft.com/microsoft-edge/webview2/consumer/> |
| Inno Setup 6 *(optional)* | building the Setup EXE | `choco install innosetup` or <https://jrsoftware.org/isdl.php> |
| 7-Zip *(optional)* | self-extracting EXE + source tar.gz | <https://www.7-zip.org> |
| Windows 10/11 SDK *(optional)* | `makeappx.exe`/`signtool.exe` for MSIX | Visual Studio Installer → "Windows 11 SDK" |

Visual Studio 2022/2026 with the ".NET desktop development" and "Windows
application development" workloads works too; everything below is plain
`dotnet` CLI so CI and local builds match.

## Clone, restore, build, test

```powershell
git clone https://github.com/Manixpcshot/peyda_browser.git
cd peyda_browser

dotnet restore PeydaBrowser.sln

# Debug build of the whole solution (x64 is the primary platform)
dotnet build PeydaBrowser.sln -c Debug -p:Platform=x64

# Unit + integration tests (these run on any OS with .NET 10 in principle;
# the App project it references for compile checks is Windows-only)
dotnet test tests/PeydaBrowser.UnitTests/PeydaBrowser.UnitTests.csproj -c Release
dotnet test tests/PeydaBrowser.IntegrationTests/PeydaBrowser.IntegrationTests.csproj -c Release

# Run the browser (F5 in Visual Studio works as well)
dotnet run --project src/PeydaBrowser.App/PeydaBrowser.App.csproj -c Debug -p:Platform=x64
```

For ARM64, use `-p:Platform=ARM64` and `-r win-arm64` below.

## Publish (portable layout)

```powershell
dotnet publish src/PeydaBrowser.App/PeydaBrowser.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/publish/win-x64
```

The output folder is the complete app: run `PeydaBrowser.exe` from it
(no .NET installation needed on the target machine; the WebView2 Runtime is
still required - it is a shared system component by design).

## Package everything (same script CI uses)

```powershell
# requires Inno Setup + 7-Zip + Windows SDK (see table above)
./installer/Package-All.ps1 `
  -Version 0.1.0-alpha `
  -Arch x64 `
  -PublishDir artifacts/publish/win-x64 `
  -OutDir artifacts/release
```

Produces in `artifacts/release`:

- `PeydaBrowser-0.1.0-alpha-win-x64-portable.zip`
- `PeydaBrowser-0.1.0-alpha-win-x64.exe` (self-extracting portable)
- `PeydaBrowser-Setup-0.1.0-alpha-win-x64.exe` (Inno Setup)
- `PeydaBrowser-0.1.0-alpha-win-x64.msix` (+ `.cer` dev certificate when no
  signing certificate is configured)
- `SHA256SUMS.txt`

Individual pieces: `installer/New-Msix.ps1`, `installer/New-SfxExe.ps1`,
and the Inno script `installer/PeydaBrowser.iss` (defines at the top of the
file: `AppVersion`, `Arch`, `PublishDir`, `OutputDir`).

### Signing locally

Set these environment variables before running `Package-All.ps1` (never
commit a certificate):

```powershell
$env:PEYDA_SIGN_PFX = 'C:\path\to\codesigning.pfx'   # used by New-Msix.ps1
$env:PEYDA_SIGN_PFX_PASSWORD = '...'
# or, exactly like CI: base64 PFX in PEYDA_SIGN_PFX_BASE64
```

Without them, the EXE stays unsigned and the MSIX is signed with a throwaway
self-signed development certificate; build output and release notes say so.

## Version numbers

`Directory.Build.props` is the single source of truth
(`VersionPrefix` + `VersionSuffix`). CI overrides both from the Git tag /
channel when releasing, so the app's About page, installer file names and the
GitHub Release always agree. See [RELEASE.md](RELEASE.md).

## Troubleshooting

- **WebView2 Runtime missing** → the app still starts (internal pages work)
  and offers Microsoft's official download page; install the Evergreen
  runtime and restart Peyda.
- **`dotnet` picks the wrong SDK** → `global.json` pins 10.0.x with
  `rollForward: latestFeature`; check `dotnet --list-sdks`.
- **MSIX will not install** → dev-signed packages require trusting the
  exported `.cer` (right-click → Install Certificate → Local Machine →
  Trusted People) or Developer Mode sideloading. Store distribution would use
  a real publisher certificate instead.
- **Preserved data between runs**: profiles live in
  `%LOCALAPPDATA%\PeydaBrowser`. Delete that folder for a truly clean slate.
