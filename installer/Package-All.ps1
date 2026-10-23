<#
.SYNOPSIS
  Produces every binary release asset for one architecture into -OutDir:
    PeydaBrowser-{Version}-win-{Arch}-portable.zip
    PeydaBrowser-{Version}-win-{Arch}.exe            (self-extracting portable)
    PeydaBrowser-Setup-{Version}-win-{Arch}.exe      (Inno Setup, with uninstaller)
    PeydaBrowser-{Version}-win-{Arch}.msix           (+ .cer when dev-signed)
    SHA256SUMS.txt                                    (this arch only; release.yml rebuilds a combined file)

  Used identically by build.yml (artifacts) and release.yml (GitHub Releases),
  so what CI validates on every push is exactly what gets published.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidateSet('x64','arm64')] [string] $Arch,
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$PublishDir = (Resolve-Path $PublishDir).Path

# Materialise the signing certificate from CI secrets, when provided. The PFX
# is written outside the repo, used for Setup EXE + MSIX signing, then removed.
$pfxPath = $null
if ($env:PEYDA_SIGN_PFX_BASE64) {
    $pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) ('peyda-sign-' + [guid]::NewGuid().ToString('N') + '.pfx')
    [System.IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:PEYDA_SIGN_PFX_BASE64))
    $env:PEYDA_SIGN_PFX = $pfxPath
    Write-Host 'Code signing certificate available (from CI secrets).'
}
else {
    Write-Warning 'No code-signing certificate configured: EXE will be UNSIGNED, MSIX will use a development certificate.'
}

try {
    # 1) Portable ZIP
    $zip = Join-Path $OutDir "PeydaBrowser-$Version-win-$Arch-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Portable ZIP: $zip"

    # 2) Self-extracting portable EXE
    $sfx = Join-Path $OutDir "PeydaBrowser-$Version-win-$Arch.exe"
    & (Join-Path $PSScriptRoot 'New-SfxExe.ps1') -PublishDir $PublishDir -OutExe $sfx -Version $Version

    # 3) Inno Setup installer
    $iscc = @('C:\Program Files (x86)\Inno Setup 6\ISCC.exe', 'C:\Program Files\Inno Setup 6\ISCC.exe') |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    if (-not $iscc) { throw 'Inno Setup compiler (ISCC.exe) not found. Install with: choco install innosetup' }

    & $iscc "/DAppVersion=$Version" "/DArch=$Arch" "/DPublishDir=$PublishDir" "/DOutputDir=$OutDir" (Join-Path $PSScriptRoot 'PeydaBrowser.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    $setupExe = Join-Path $OutDir "PeydaBrowser-Setup-$Version-win-$Arch.exe"
    if (-not (Test-Path $setupExe)) { throw "Expected setup file was not produced: $setupExe" }

    # 4) MSIX package
    $msix = Join-Path $OutDir "PeydaBrowser-$Version-win-$Arch.msix"
    & (Join-Path $PSScriptRoot 'New-Msix.ps1') -PublishDir $PublishDir -OutMsix $msix -Version $Version -Arch $Arch

    # 5) Sign the plain EXEs too when a certificate is available
    if ($pfxPath) {
        $signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
            Where-Object { $_.Name -match '^10\.' } | Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($signTool) {
            & $signTool sign /fd SHA256 /f $pfxPath /p $env:PEYDA_SIGN_PFX_PASSWORD /tr http://timestamp.digicert.com /td SHA256 $setupExe $sfx
            if ($LASTEXITCODE -ne 0) { throw "signtool failed for EXE signing with exit code $LASTEXITCODE" }
        }
    }

    # 6) Checksums for this architecture's binaries
    $sums = @()
    foreach ($file in @($zip, $sfx, $setupExe, $msix)) {
        $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums += "$hash  $(Split-Path $file -Leaf)"
    }
    $sums | Set-Content (Join-Path $OutDir 'SHA256SUMS.txt') -Encoding ascii

    Write-Host "Packaging complete for Peyda Browser $Version ($Arch):"
    Get-ChildItem $OutDir | Select-Object Name, @{n='SizeMB'; e={ [math]::Round($_.Length/1MB, 2) }}
}
finally {
    if ($pfxPath -and (Test-Path $pfxPath)) { Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:PEYDA_SIGN_PFX -ErrorAction SilentlyContinue
}
