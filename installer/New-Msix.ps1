<#
.SYNOPSIS
  Packs the published Peyda Browser layout into an MSIX package with makeappx,
  then signs it.

  Signing strategy (see docs/RELEASE.md):
    * If env PEYDA_SIGN_PFX (path to a .pfx materialised by CI from GitHub
      Secrets) exists, sign with that certificate.
    * Otherwise generate a throw-away self-signed development certificate
      (CN=Peyda Browser Contributors) on the build agent and export its public
      .cer next to the package. Such MSIX files are sideloadable after the
      user trusts the dev certificate; release notes always say so clearly.
  The certificate is never generated into, or read from, the repository.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $OutMsix,
    [Parameter(Mandatory)] [string] $Version,   # semver, e.g. 0.1.0-alpha
    [Parameter(Mandatory)] [ValidateSet('x64','arm64')] [string] $Arch
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

function Find-SdkTool([string] $Name) {
    $kitsRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (-not (Test-Path $kitsRoot)) { throw "Windows 10 SDK not found (looked in $kitsRoot)." }
    $candidate = Get-ChildItem $kitsRoot -Directory |
        Where-Object { $_.Name -match '^10\.' } |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $candidate) { throw "$Name not found in the Windows SDK." }
    return $candidate
}

# MSIX versions must be four-part numeric: 0.1.0-alpha -> 0.1.0.0
$numeric = ($Version -split '-')[0]
$quadParts = @($numeric.Split('.'))
while ($quadParts.Count -lt 4) { $quadParts += '0' }
$quadVersion = ($quadParts[0..3] -join '.')

$layout = Join-Path ([System.IO.Path]::GetTempPath()) ("peyda-msix-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $layout | Out-Null
try {
    Copy-Item (Join-Path $PublishDir '*') $layout -Recurse -Force

    $manifestTemplate = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
    $manifest = $manifestTemplate.Replace('{{VERSION}}', $quadVersion).Replace('{{ARCH}}', $Arch)
    Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding UTF8

    $makeAppx = Find-SdkTool 'makeappx.exe'
    $signTool = Find-SdkTool 'signtool.exe'

    $outDir = Split-Path $OutMsix -Parent
    if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    if (Test-Path $OutMsix) { Remove-Item $OutMsix -Force }

    & $makeAppx pack /d $layout /p $OutMsix /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE" }

    if ($env:PEYDA_SIGN_PFX -and (Test-Path $env:PEYDA_SIGN_PFX)) {
        Write-Host "Signing MSIX with the certificate provided via CI secrets."
        & $signTool sign /fd SHA256 /f $env:PEYDA_SIGN_PFX /p $env:PEYDA_SIGN_PFX_PASSWORD $OutMsix
    }
    else {
        Write-Warning "No code-signing certificate configured - generating a self-signed DEVELOPMENT certificate for this build."
        $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=Peyda Browser Contributors' `
            -KeyUsage DigitalSignature -FriendlyName 'Peyda Browser (dev signing)' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        & $signTool sign /fd SHA256 /sha1 $cert.Thumbprint $OutMsix
        if ($LASTEXITCODE -eq 0) {
            $cerPath = [System.IO.Path]::ChangeExtension($OutMsix, '.cer')
            Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
            Write-Host "Exported development certificate: $cerPath"
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
    Write-Host "MSIX created: $OutMsix (version $quadVersion, $Arch)"
}
finally {
    Remove-Item $layout -Recurse -Force -ErrorAction SilentlyContinue
}
