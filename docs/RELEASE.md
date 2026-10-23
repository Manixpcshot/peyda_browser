# Releasing Peyda Browser

## Channels and triggers

| Trigger | Result |
| --- | --- |
| Every **push to `main`** (after green build+tests) | Rolling **nightly** pre-release (tag `nightly`) is deleted and recreated with version `{VersionPrefix}-nightly.{yyyyMMdd}.{shortSha}` |
| **Git tag** `vX.Y.Z` | Stable release `vX.Y.Z` |
| **Git tag** `vX.Y.Z-beta[.N]` / `-alpha[.N]` | Pre-release of that version |
| **`workflow_dispatch`** | Same pipeline; choose channel, optionally override version |

If the build, any test, or any packaging step fails, **no release is created
or updated** - the `publish` job depends on both architecture `package` jobs.

Every push/PR also runs `build.yml` (x64), which publishes the full set of
binaries as workflow artifacts, so no successful build is ever left without a
downloadable result.

## Release assets

Each GitHub Release contains (per architecture first, then shared files):

```
PeydaBrowser-{VERSION}-win-x64-portable.zip
PeydaBrowser-{VERSION}-win-x64.exe                (self-extracting portable, 7-Zip SFX)
PeydaBrowser-Setup-{VERSION}-win-x64.exe          (Inno Setup installer + uninstaller)
PeydaBrowser-{VERSION}-win-x64.msix               (Windows app package)
PeydaBrowser-{VERSION}-win-arm64-portable.zip
PeydaBrowser-{VERSION}-win-arm64.exe
PeydaBrowser-Setup-{VERSION}-win-arm64.exe
PeydaBrowser-{VERSION}-win-arm64.msix
PeydaBrowser-{VERSION}-source.zip                 (git archive of the release commit)
PeydaBrowser-{VERSION}-source.tar.gz
SHA256SUMS.txt                                    (SHA-256 of every binary/source asset)
CHANGELOG.md
LICENSE.txt
README.md
```

(When MSIX files are signed with the development certificate, a matching
`.cer` is attached as well so the package can be sideloaded.)

## Versioning

Semantic Versioning (`MAJOR.MINOR.PATCH[-prerelease]`). The single source of
truth for normal builds is `Directory.Build.props`
(`VersionPrefix`/`VersionSuffix`); `release.yml` derives/overrides it from
the trigger (see table) and passes the same values to `dotnet publish`,
Inno Setup (`/DAppVersion`) and the MSIX manifest, so the About page,
installer names, Git tag and GitHub Release always match. Update
`CHANGELOG.md` in the same PR that bumps the version prefix.

## Installer choice: Inno Setup

WiX and MSIX-only were evaluated. Inno Setup was chosen for the classic
"Setup EXE" because it:

- builds from one readable script in CI (`choco install innosetup`),
- upgrades in place via a stable `AppId`, keeps `%LOCALAPPDATA%` data on
  upgrade, and *asks* before deleting profile data on uninstall,
- supports the WebView2 Runtime check in Pascal script
  (`InitializeSetup` in `installer/PeydaBrowser.iss`): if the runtime is
  missing, the user is offered Microsoft's official fwlink download page -
  nothing is downloaded or executed silently,
- needs no custom bootstrapper code for per-machine installs.

MSIX is the "official" Windows package and is produced in parallel from the
same publish output (`installer/New-Msix.ps1` + `makeappx`).

## Signing

No certificate, key or password ever lives in this repository. To enable real
Authenticode signing in CI, add two **GitHub Secrets**:

- `PEYDA_SIGN_PFX_BASE64` - base64 of the `.pfx` code-signing certificate
- `PEYDA_SIGN_PFX_PASSWORD` - its password

`Package-All.ps1` then signs the Setup EXE, the SFX EXE and the MSIX (the PFX
is materialised into `%TEMP%` on the agent and deleted afterwards).

Without those secrets the pipeline still succeeds, but:

- EXE files are **unsigned** (SmartScreen warnings are expected),
- MSIX files are signed with a throwaway **self-signed development
  certificate** (`CN=Peyda Browser Contributors`) whose public `.cer` is
  attached to the release for sideloading,
- the release notes state the unsigned status explicitly.

## How to cut a release

1. Update `Directory.Build.props` (`VersionPrefix`/`VersionSuffix`) and move
   the `Unreleased` notes in `CHANGELOG.md` under the new version heading.
2. Merge to `main` (CI goes green, nightly refreshes - smoke-test it).
3. Tag: `git tag v0.2.0 && git push origin v0.2.0`
   (or `v0.2.0-beta.1` for a beta).
4. Watch the **Release** workflow; verify assets + `SHA256SUMS.txt` on the
   published GitHub Release; download and install the Setup EXE once on a
   clean machine/VM.

To verify any asset afterwards:

```powershell
Get-FileHash .\PeydaBrowser-Setup-0.2.0-win-x64.exe -Algorithm SHA256
# must match the line in SHA256SUMS.txt from the same release
```
