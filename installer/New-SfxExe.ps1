<#
.SYNOPSIS
  Builds the standalone `PeydaBrowser-{version}-win-{arch}.exe`: a 7-Zip
  self-extracting archive of the portable layout that extracts to a temp
  folder and starts PeydaBrowser.exe. WinUI 3 apps are multi-file and do not
  support PublishSingleFile, so an SFX package is the honest way to ship one
  runnable .exe (see docs/RELEASE.md).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $OutExe,
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'

$sevenZip = @('C:\Program Files\7-Zip\7z.exe', 'C:\Program Files (x86)\7-Zip\7z.exe') |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $sevenZip) {
    $cmd = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($cmd) { $sevenZip = $cmd.Source }
}
if (-not $sevenZip) { throw '7-Zip (7z.exe) is required to build the self-extracting EXE.' }

$sfxModule = Join-Path (Split-Path $sevenZip -Parent) '7z.sfx'
if (-not (Test-Path $sfxModule)) { throw "7z.sfx module not found next to $sevenZip" }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("peyda-sfx-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    $archive = Join-Path $work 'app.7z'
    & $sevenZip a -t7z $archive (Join-Path $PublishDir '*') -mx=5 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z archive creation failed with exit code $LASTEXITCODE" }

    $config = Join-Path $work 'config.txt'
    @"
;!@Install@!UTF-8!
Title="Peyda Browser $Version (portable)"
RunProgram="PeydaBrowser.exe"
GUIMode="2"
;!@InstallEnd@!
"@ | Set-Content $config -Encoding utf8NoBOM

    $outDir = Split-Path $OutExe -Parent
    if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    if (Test-Path $OutExe) { Remove-Item $OutExe -Force }

    # sfx module + config + archive, concatenated in order
    $outStream = [System.IO.File]::Create($OutExe)
    try {
        foreach ($part in @($sfxModule, $config, $archive)) {
            $bytes = [System.IO.File]::ReadAllBytes($part)
            $outStream.Write($bytes, 0, $bytes.Length)
        }
    }
    finally { $outStream.Dispose() }

    Write-Host "Self-extracting EXE created: $OutExe"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
