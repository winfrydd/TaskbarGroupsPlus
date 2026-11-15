<#
  Builds Taskbar Groups and installs it into an existing (or new) program folder.

  - Backs up the target folder first (to <target>.backup-<date>).
  - Keeps your groups (config\) and shortcuts (Shortcuts\); replaces only program files.
  - Taskbar pins keep working as long as the folder path stays the same.

  Usage:  powershell -ExecutionPolicy Bypass -File install.ps1 -Target D:\Programs\TaskbarGroups
#>
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [switch]$NoBackup
)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$out = Join-Path $repo 'publish'

Write-Host "Building..."
& $dotnet publish (Join-Path $repo 'src\TaskbarGroups') -c Release -o $out -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$running = Get-Process TaskbarGroups -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Target*" }
if ($running) { throw "Taskbar Groups is running from $Target. Close it first." }

if ((Test-Path $Target) -and -not $NoBackup) {
    $backup = "$Target.backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Write-Host "Backing up to $backup"
    Copy-Item $Target $backup -Recurse
}
New-Item -ItemType Directory -Force $Target | Out-Null

# Files left behind by the original (tjackenpacken) version that this build no longer uses.
$obsolete = 'ChinhDo.Transactions.FileManager.dll', 'Microsoft.WindowsAPICodePack.dll',
            'Microsoft.WindowsAPICodePack.Shell.dll', 'TaskbarGroups.exe.config', 'TaskbarGroups.pdb',
            'Windows.winmd', 'JITComp'
foreach ($o in $obsolete) {
    $p = Join-Path $Target $o
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

Copy-Item (Join-Path $out '*') $Target -Force
Write-Host "Installed to $Target"
