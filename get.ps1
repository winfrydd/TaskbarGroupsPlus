<#
  Taskbar Groups+ installer / updater.

  Install or update (PowerShell):
    irm https://raw.githubusercontent.com/winfrydd/TaskbarGroupsPlus/main/get.ps1 | iex

  What it does:
    1. Makes sure the .NET 10 Desktop Runtime is installed (via winget, with your OK).
    2. Downloads the latest release from GitHub.
    3. Installs it to %LOCALAPPDATA%\Programs\TaskbarGroupsPlus (no admin rights needed).
       Running it again updates the program files and keeps your groups.
    4. Adds "Taskbar Groups+" to the Start menu and opens it.
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\TaskbarGroupsPlus'),
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'winfrydd/TaskbarGroupsPlus'
$exe = Join-Path $InstallDir 'TaskbarGroups.exe'

function Stop-Running {
    $procs = Get-Process TaskbarGroups -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, 'OrdinalIgnoreCase') }
    if ($procs) {
        Write-Host "Closing Taskbar Groups+..."
        $procs | ForEach-Object { $_.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 2
        $procs | Where-Object { -not $_.HasExited } | Stop-Process -Force
    }
}

if ($Uninstall) {
    if (-not (Test-Path $exe)) { Write-Host "Taskbar Groups+ isn't installed in $InstallDir"; return }
    Stop-Running
    & $exe --remove-start-menu | Out-Null
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir. Unpin any group buttons from the taskbar yourself (right-click > Unpin)."
    return
}

# 1. .NET Desktop Runtime 10+
function Test-Runtime {
    $roots = @("$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App",
               "$env:LOCALAPPDATA\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App")
    foreach ($r in $roots) {
        if (Test-Path $r) {
            foreach ($d in Get-ChildItem $r -Directory) {
                $v = $null
                if ([version]::TryParse(($d.Name -replace '-.*$', ''), [ref]$v) -and $v.Major -ge 10) { return $true }
            }
        }
    }
    return $false
}

if (-not (Test-Runtime)) {
    Write-Host ""
    Write-Host "Taskbar Groups+ needs the free Microsoft .NET 10 Desktop Runtime (about 55 MB)."
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        $ok = Read-Host "Install it now with winget? [Y/n]"
        if ($ok -notmatch '^[Nn]') {
            winget install --id Microsoft.DotNet.DesktopRuntime.10 -e --accept-package-agreements --accept-source-agreements
        }
    }
    if (-not (Test-Runtime)) {
        Write-Host "Get it here (choose '.NET Desktop Runtime', x64): https://dotnet.microsoft.com/download/dotnet/10.0"
        Write-Host "Then run this installer again."
        Start-Process 'https://dotnet.microsoft.com/download/dotnet/10.0'
        return
    }
}

# 2. Latest release
Write-Host "Finding the latest release..."
$headers = @{ 'User-Agent' = 'TaskbarGroupsPlus-installer'; 'Accept' = 'application/vnd.github+json' }
$rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $headers
$asset = $rel.assets | Where-Object { $_.name -like 'TaskbarGroupsPlus*.zip' } | Select-Object -First 1
if (-not $asset) { throw "The latest release ($($rel.tag_name)) has no TaskbarGroupsPlus zip attached." }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("TaskbarGroupsPlus-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    $zip = Join-Path $tmp $asset.name
    Write-Host "Downloading $($asset.name)..."
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -Headers $headers -UseBasicParsing
    $files = Join-Path $tmp 'files'
    Expand-Archive $zip $files

    # 3. Install (program files only; config\ and Shortcuts\ are left alone)
    $updating = Test-Path $exe
    Stop-Running
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Copy-Item (Join-Path $files '*') $InstallDir -Recurse -Force
    if ($updating) { Write-Host "Updated to $($rel.tag_name) in $InstallDir" }
    else { Write-Host "Installed $($rel.tag_name) to $InstallDir" }
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }

# 4. Start menu + open
& $exe --start-menu | Out-Null
Start-Process $exe
Write-Host "Done. Find 'Taskbar Groups+' in the Start menu."
