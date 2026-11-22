#Requires -Version 5.1
<#
    install.ps1 - per-user (no administrator) install for Watt.

    What it does:
      1. stops a running Watt,
      2. copies Watt.exe + Watt.exe.config into %LOCALAPPDATA%\Programs\Watt,
      3. registers autostart at HKCU\Software\Microsoft\Windows\CurrentVersion\Run
         value "Watt" = "C:\...\Watt.exe",
      4. creates a Start Menu shortcut %APPDATA%\Microsoft\Windows\Start Menu\Programs\Watt.lnk,
      5. starts the app (unless -NoStart).

    The script is idempotent: running it again simply refreshes the install.

    Test-only overrides (-InstallDir, -SourceDir, -RunKeyPath, -RunValueName,
    -ShortcutName, -StartMenuDir, -NoAutostart, -NoStart, -NoShortcut) let automation
    exercise the logic against a temporary directory/key without touching the real
    per-user install. Defaults always produce the real install.

    Usage:
        .\install.ps1
        .\install.ps1 -NoStart
#>
[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$SourceDir,
    [string]$RunKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    [string]$RunValueName = 'Watt',
    [string]$ShortcutName = 'Watt',
    [string]$StartMenuDir,
    [switch]$NoAutostart,
    [switch]$NoStart,
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host "[install] $Message"
}

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

try {
    $projectRoot = $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($InstallDir)) {
        $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Watt'
    }
    $InstallDir = Resolve-FullPath $InstallDir

    if ([string]::IsNullOrWhiteSpace($SourceDir)) {
        $candidates = @(
            (Join-Path $projectRoot 'dist\Watt'),
            $projectRoot
        )
        $SourceDir = $null
        foreach ($candidate in $candidates) {
            if ((Test-Path -LiteralPath (Join-Path $candidate 'Watt.exe')) -and
                (Test-Path -LiteralPath (Join-Path $candidate 'plugins'))) {
                $SourceDir = $candidate
                break
            }
        }
        if ($null -eq $SourceDir) {
            throw "No staged Watt runtime found. Run .\build.ps1 first (looked in: $($candidates -join ', '))."
        }
    }
    $SourceDir = Resolve-FullPath $SourceDir

    $sourceExe = Join-Path $SourceDir 'Watt.exe'
    if (-not (Test-Path -LiteralPath $sourceExe)) {
        throw "Source Watt.exe not found: $sourceExe. Run .\build.ps1 first."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $SourceDir 'plugins'))) {
        throw "Source plugins folder not found: $(Join-Path $SourceDir 'plugins'). Run .\build.ps1 (without -NoStage) first."
    }

    if ([string]::IsNullOrWhiteSpace($StartMenuDir)) {
        $StartMenuDir = [Environment]::GetFolderPath('Programs')
    }
    $StartMenuDir = Resolve-FullPath $StartMenuDir

    $installedExe = Join-Path $InstallDir 'Watt.exe'
    $runKey = $RunKeyPath
    $shortcutPath = Join-Path $StartMenuDir ($ShortcutName + '.lnk')
    $command = '"' + $installedExe + '"'

    Write-Step "source : $SourceDir"
    Write-Step "target : $InstallDir"

    # 1. Stop a running instance so the exe can be replaced.
    $running = @(Get-Process -Name 'Watt' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Step "stopping $($running.Count) running Watt process(es)"
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }

    # 2. Copy only the runtime payload. Docs, the SDK folder and scripts stay in the extracted
    #    release folder; they must not end up in %LOCALAPPDATA%\Programs\Watt.
    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }
    $installedPlugins = Join-Path $InstallDir 'plugins'
    if (Test-Path -LiteralPath $installedPlugins) {
        Remove-Item -LiteralPath $installedPlugins -Recurse -Force
    }

    Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
    $sourceConfig = Join-Path $SourceDir 'Watt.exe.config'
    if (Test-Path -LiteralPath $sourceConfig) {
        Copy-Item -LiteralPath $sourceConfig -Destination (Join-Path $InstallDir 'Watt.exe.config') -Force
    }
    foreach ($sdkDll in @('Watt.Plugin.Contracts.dll', 'Watt.Plugin.Support.dll')) {
        $sourceDll = Join-Path $SourceDir $sdkDll
        if (Test-Path -LiteralPath $sourceDll) {
            Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $InstallDir $sdkDll) -Force
        }
    }
    $sourcePlugins = Join-Path $SourceDir 'plugins'
    if (Test-Path -LiteralPath $sourcePlugins) {
        New-Item -ItemType Directory -Path $installedPlugins -Force | Out-Null
        Copy-Item -Path (Join-Path $sourcePlugins '*') -Destination $installedPlugins -Recurse -Force
    }
    Write-Step "copied runtime payload from $SourceDir (app + SDK DLLs + plugins)"

    # 3. Autostart registration (per-user, no admin).
    if ($NoAutostart) {
        Write-Step 'autostart registration skipped (-NoAutostart)'
    }
    else {
        if (-not (Test-Path -LiteralPath $runKey)) {
            New-Item -Path $runKey -Force | Out-Null
        }
        Set-ItemProperty -Path $runKey -Name $RunValueName -Value $command -Type String
        Write-Step "autostart registered: $RunValueName = $command"
    }

    # 4. Start Menu shortcut via WScript.Shell (no extra modules required).
    if (-not $NoShortcut) {
        if (-not (Test-Path -LiteralPath $StartMenuDir)) {
            New-Item -ItemType Directory -Path $StartMenuDir -Force | Out-Null
        }
        $wshShell = New-Object -ComObject WScript.Shell
        $shortcut = $wshShell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $installedExe
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.IconLocation = $installedExe + ',0'
        $shortcut.Description = 'Watt - battery of connected peripherals'
        $shortcut.Save()
        Write-Step "shortcut created: $shortcutPath"
    }

    # 5. Start the app.
    if (-not $NoStart) {
        Start-Process -FilePath $installedExe | Out-Null
        Write-Step "started $installedExe"
    }

    Write-Step 'OK - Watt is installed for the current user.'
    exit 0
}
catch {
    Write-Host "[install] FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
