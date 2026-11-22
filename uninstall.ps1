#Requires -Version 5.1
<#
    uninstall.ps1 - per-user (no administrator) uninstall for Watt.

    What it does:
      1. stops a running Watt,
      2. removes the autostart value from HKCU Run,
      3. removes the Start Menu shortcut,
      4. removes %LOCALAPPDATA%\Programs\Watt (the installed copy only; a build under
         watt\dist or watt\src is never touched).

    Idempotent: every step tolerates "already gone" and the script still exits 0.

    Test-only overrides match install.ps1 (-InstallDir, -RunKeyPath, -RunValueName,
    -ShortcutName, -StartMenuDir).

    Usage:
        .\uninstall.ps1
#>
[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$DataDir,
    [string]$RunKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    [string]$RunValueName = 'Watt',
    [string]$ShortcutName = 'Watt',
    [string]$StartMenuDir
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host "[uninstall] $Message"
}

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

try {
    if ([string]::IsNullOrWhiteSpace($InstallDir)) {
        $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Watt'
    }
    $InstallDir = Resolve-FullPath $InstallDir

    if ([string]::IsNullOrWhiteSpace($StartMenuDir)) {
        $StartMenuDir = [Environment]::GetFolderPath('Programs')
    }
    $StartMenuDir = Resolve-FullPath $StartMenuDir

    if ([string]::IsNullOrWhiteSpace($DataDir)) {
        $DataDir = Join-Path $env:LOCALAPPDATA 'Watt'
    }
    $DataDir = Resolve-FullPath $DataDir

    $installedExe = Join-Path $InstallDir 'Watt.exe'
    $runKey = $RunKeyPath
    $shortcutPath = Join-Path $StartMenuDir ($ShortcutName + '.lnk')

    # 1. Stop a running instance.
    $running = @(Get-Process -Name 'Watt' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Step "stopping $($running.Count) running Watt process(es)"
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
    else {
        Write-Step 'no running Watt process'
    }

    # 2. Remove autostart value.
    $runExists = Test-Path -LiteralPath $runKey
    if ($runExists) {
        $value = Get-ItemProperty -Path $runKey -Name $RunValueName -ErrorAction SilentlyContinue
        if ($null -ne $value) {
            Remove-ItemProperty -Path $runKey -Name $RunValueName -ErrorAction SilentlyContinue
            Write-Step "autostart removed: $RunValueName"
        }
        else {
            Write-Step "autostart value not present: $RunValueName"
        }
    }
    else {
        Write-Step 'HKCU Run key not present'
    }

    # 3. Remove Start Menu shortcut.
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Step "shortcut removed: $shortcutPath"
    }
    else {
        Write-Step "shortcut not present: $shortcutPath"
    }

    # 4. Remove the installed folder. Guard against deleting anything that is not a
    #    "Watt" folder just in case an override points somewhere unexpected.
    if (Test-Path -LiteralPath $InstallDir) {
        $leaf = Split-Path -Path $InstallDir -Leaf
        if ($leaf -eq 'Watt') {
            Remove-Item -LiteralPath $InstallDir -Recurse -Force
            Write-Step "removed $InstallDir"
        }
        else {
            Write-Warning "Refusing to remove '$InstallDir' (folder name is '$leaf', expected 'Watt')"
        }
    }
    else {
        Write-Step "install folder not present: $InstallDir"
    }

    # 5. Remove the local battery cache (keeps sleeping devices visible between restarts).
    if (Test-Path -LiteralPath $DataDir) {
        $dataLeaf = Split-Path -Path $DataDir -Leaf
        if ($dataLeaf -eq 'Watt') {
            Remove-Item -LiteralPath $DataDir -Recurse -Force
            Write-Step "removed local cache $DataDir"
        }
        else {
            Write-Warning "Refusing to remove '$DataDir' (folder name is '$dataLeaf', expected 'Watt')"
        }
    }
    else {
        Write-Step "local cache not present: $DataDir"
    }

    # 6. Remove the notification-area placement entry for this app.
    $notifyRoot = 'HKCU:\Control Panel\NotifyIconSettings'
    if (Test-Path -LiteralPath $notifyRoot) {
        $removed = 0
        foreach ($notifyKey in Get-ChildItem -LiteralPath $notifyRoot -ErrorAction SilentlyContinue) {
            try {
                $registered = (Get-ItemProperty -LiteralPath $notifyKey.PSPath -Name 'ExecutablePath' -ErrorAction SilentlyContinue).ExecutablePath
                if ([string]::IsNullOrWhiteSpace($registered)) { continue }
                $same = $false
                try { $same = ([System.IO.Path]::GetFullPath($registered) -ieq $installedExe) } catch { $same = ($registered -ieq $installedExe) }
                if (-not $same) { continue }
                Remove-Item -LiteralPath $notifyKey.PSPath -Recurse -Force
                $removed++
            }
            catch {
                Write-Warning "could not remove notification-area entry: $($_.Exception.Message)"
            }
        }
        Write-Step "notification-area entries removed: $removed"
    }

    Write-Step 'OK - Watt is uninstalled for the current user.'
    exit 0
}
catch {
    Write-Host "[uninstall] FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
