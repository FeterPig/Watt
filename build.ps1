#Requires -Version 5.1
<#
    build.ps1 - offline Release build wrapper for Watt (plugin architecture).

    Builds:
      - the WPF tray app (src\Watt),
      - the public SDK assemblies (src\Watt.Plugin.Contracts, src\Watt.Plugin.Support),
      - every built-in plugin (src\Watt.Plugins.*),
    then stages the runtime layout into watt\dist\Watt:

      Watt.exe
      Watt.exe.config
      Watt.Plugin.Contracts.dll
      Watt.Plugin.Support.dll
      plugins\Watt.Plugins.*.dll

    The build machine has no NuGet feed; this script points the SDK at workspace-local
    state. Usage:
        .\build.ps1            # build and stage dist\Watt
        .\build.ps1 -NoStage   # build only
#>
[CmdletBinding()]
param(
    [switch]$NoStage
)

$ErrorActionPreference = 'Stop'

$projectRoot   = $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$srcRoot       = Join-Path $projectRoot 'src'
$appProject    = Join-Path $srcRoot 'Watt\Watt.csproj'
$appBin        = Join-Path $srcRoot 'Watt\bin\Release\net48'
$distDir       = Join-Path $projectRoot 'dist\Watt'
$distPlugins   = Join-Path $distDir 'plugins'
$pluginProjects = Get-ChildItem -Path $srcRoot -Directory -Filter 'Watt.Plugins.*' |
    ForEach-Object { Join-Path $_.FullName ($_.Name + '.csproj') } |
    Where-Object { Test-Path -LiteralPath $_ }

function Write-Step([string]$Message) {
    Write-Host "[build] $Message"
}

function Invoke-DotnetBuild([string]$Project) {
    Write-Step "dotnet build $Project -c Release"
    & dotnet build $Project -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[build] FAILED (dotnet exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

try {
    if (-not (Test-Path -LiteralPath $appProject)) {
        throw "Project file not found: $appProject"
    }

    $env:DOTNET_CLI_HOME = Join-Path $workspaceRoot '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $workspaceRoot '.nuget-packages'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    foreach ($dir in @($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES)) {
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }

    Invoke-DotnetBuild $appProject

    foreach ($pluginProject in $pluginProjects) {
        Invoke-DotnetBuild $pluginProject
    }

    if (-not (Test-Path -LiteralPath $appBin)) {
        Write-Host "[build] FAILED: $appBin was not produced" -ForegroundColor Red
        exit 1
    }

    if ($NoStage) {
        Write-Step 'OK (no staging)'
        exit 0
    }

    if (Test-Path -LiteralPath $distDir) {
        Remove-Item -LiteralPath $distDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    New-Item -ItemType Directory -Path $distPlugins -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $appBin 'Watt.exe') -Destination $distDir -Force
    $appConfig = Join-Path $appBin 'Watt.exe.config'
    if (Test-Path -LiteralPath $appConfig) {
        Copy-Item -LiteralPath $appConfig -Destination $distDir -Force
    }
    foreach ($sdkDll in @('Watt.Plugin.Contracts.dll', 'Watt.Plugin.Support.dll')) {
        $sdkPath = Join-Path $appBin $sdkDll
        if (Test-Path -LiteralPath $sdkPath) {
            Copy-Item -LiteralPath $sdkPath -Destination $distDir -Force
        }
        else {
            Write-Host "[build] FAILED: $sdkPath was not produced" -ForegroundColor Red
            exit 1
        }
    }

    $pluginCount = 0
    foreach ($pluginProject in $pluginProjects) {
        $pluginDir = Split-Path -Parent $pluginProject
        $pluginName = Split-Path -Leaf $pluginDir
        $pluginBin = Join-Path $pluginDir 'bin\Release\net48'
        $pluginDll = Join-Path $pluginBin ($pluginName + '.dll')
        if (-not (Test-Path -LiteralPath $pluginDll)) {
            Write-Host "[build] FAILED: plugin DLL not produced: $pluginDll" -ForegroundColor Red
            exit 1
        }
        Copy-Item -LiteralPath $pluginDll -Destination $distPlugins -Force
        $pluginCount++
    }

    if ($pluginCount -eq 0) {
        Write-Host "[build] FAILED: no plugin projects found under src\Watt.Plugins.*" -ForegroundColor Red
        exit 1
    }

    Write-Step "staged $distDir (app + $pluginCount plugins)"
    Write-Step 'OK'
    exit 0
}
catch {
    Write-Host "[build] FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
