# Building, installing and uninstalling Watt

Watt is a per-user Windows tray app (no administrator rights required). This document
covers the offline build, the install/uninstall scripts, and where autostart is
registered.

## Requirements

- Windows 10/11 (developed on Windows 11 build 26300).
- .NET Framework 4.8 runtime (installed on Windows 11 by default).
- .NET SDK 9 **only for building**. The build is fully offline: no NuGet feed is needed
  because the project targets net48 and only references the locally installed .NET
  Framework 4.8 reference assemblies and the Windows SDK WinRT metadata.
- PowerShell 5.1 (or newer) for the scripts. No extra modules are required.

## Build

From the `watt` folder:

```powershell
.\build.ps1
```

What `build.ps1` does:

1. points the .NET SDK at workspace-local state
   (`..\.dotnet-home`, `..\.nuget-packages`) so no user-level SDK state is touched,
2. runs `dotnet build src\Watt\Watt.csproj -c Release` (offline) which also builds the
   `Watt.Plugin.Contracts` / `Watt.Plugin.Support` SDK assemblies,
3. builds every `src\Watt.Plugins.*` plugin project,
4. stages the runtime layout into `watt\dist\Watt\`:

   ```
   Watt.exe
   Watt.exe.config
   Watt.Plugin.Contracts.dll
   Watt.Plugin.Support.dll
   plugins\Watt.Plugins.*.dll   (5 built-in plugins)
   ```

Use `.\build.ps1 -NoStage` to build without touching `dist\`. The script exits non-zero
when the build fails.

Manual equivalent if you prefer raw dotnet (the plugin projects are next to the app):

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '..\.dotnet-home').Path
$env:NUGET_PACKAGES  = (Resolve-Path '..\.nuget-packages').Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build src\Watt\Watt.csproj -c Release
Get-ChildItem src -Directory -Filter 'Watt.Plugins.*' | ForEach-Object {
    dotnet build (Join-Path $_.FullName ($_.Name + '.csproj')) -c Release
}
```

## Install (per user)

From the `watt` folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

The script is idempotent (running it again refreshes the install) and does:

1. stops a running `Watt.exe` so the files can be replaced,
2. copies only the runtime payload (`Watt.exe`, `Watt.exe.config`, the two SDK DLLs and the
   `plugins\` folder) from `watt\dist\Watt\` into `%LOCALAPPDATA%\Programs\Watt\` — `docs\`,
   `sdk\` and the install scripts are not copied,
3. registers autostart at logon (see below),
4. creates the Start Menu shortcut
   `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Watt.lnk`,
5. starts Watt.

Optional switches: `-NoStart` (install without launching), `-NoAutostart` (skip the Run
key registration), `-NoShortcut` (skip the Start Menu shortcut).

Installed layout:

| Path | Purpose |
| --- | --- |
| `%LOCALAPPDATA%\Programs\Watt\Watt.exe` | application |
| `%LOCALAPPDATA%\Programs\Watt\Watt.exe.config` | runtime config |
| `%LOCALAPPDATA%\Programs\Watt\Watt.Plugin.Contracts.dll` | public plugin SDK |
| `%LOCALAPPDATA%\Programs\Watt\Watt.Plugin.Support.dll` | plugin helper library |
| `%LOCALAPPDATA%\Programs\Watt\plugins\*.dll` | built-in battery plugins |
| `%LOCALAPPDATA%\Programs\Watt\watt-startup.log` | diagnostics log (created at runtime) |
| `%LOCALAPPDATA%\Watt\last-known.json` | last-known battery cache (keeps sleeping devices visible) |
| `%LOCALAPPDATA%\Watt\plugins\*.dll` | optional third-party plugins (loaded after built-ins) |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Watt.lnk` | Start Menu shortcut |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `Watt` | autostart at logon |

## Autostart registration

Watt autostarts for the current user through the standard per-user Run key:

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    Watt = "C:\Users\<you>\AppData\Local\Programs\Watt\Watt.exe"
```

The value is the quoted path of the installed executable. Verify it with:

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Watt
Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Watt
```

or without PowerShell:

```cmd
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Watt
```

The app manages this value through `Watt.Services.StartupService`:
`IsEnabled` / `RegisteredCommand` to inspect, `Enable()` / `Disable()` to toggle, and
`EnsureEnabled(exePath)` to repair a stale path after the install location changes.
The tray icon's right-click menu exposes the same toggle ("开机自启"), and the app
re-registers the Run value automatically when it is started from the installed
`%LOCALAPPDATA%\Programs\Watt` folder.

## Uninstall (per user)

From the `watt` folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

The script is idempotent and does:

1. stops a running `Watt.exe`,
2. removes the `Watt` value from the HKCU Run key,
3. removes `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Watt.lnk`,
4. removes `%LOCALAPPDATA%\Programs\Watt` recursively,
5. removes the local battery cache in `%LOCALAPPDATA%\Watt`.

It does not touch `watt\dist\` or `watt\src\` (the development build). As a safety
guard it refuses to delete an install folder whose leaf name is not `Watt`.

## Verifying an install

```powershell
# 1. files
Get-ChildItem "$env:LOCALAPPDATA\Programs\Watt"
# 2. autostart
(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Watt).Watt
# 3. shortcut
Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'Watt.lnk')
# 4. run it; starting it a second time must not create a second tray icon, the
#    running instance just shows its panel
& "$env:LOCALAPPDATA\Programs\Watt\Watt.exe"
# 5. toggle Windows Settings > Personalization > Colors between Light and Dark;
#    the panel and tray icon follow the new theme
# 6. the tray icon should sit on the primary notification area (next to the input
#    method / network / volume icons), not inside the "^" overflow flyout
```

## Troubleshooting

- **Build fails without a network**: use `.\build.ps1`; if the SDK complains about NuGet,
  make sure `NUGET_PACKAGES` points at `watt\..\.nuget-packages` and that the project is
  not referencing packages (it targets net48 and references the local framework only).
- **`install.ps1` fails with "Access is denied" on the registry key**: the process is
  running with a restricted/low-integrity token (for example inside a sandboxed
  automation environment). Run the script from a normal user PowerShell window; no admin
  rights are needed, but HKCU writes must be allowed.
- **App does not start at logon**: check the Run value above, then check
  `%LOCALAPPDATA%\Programs\Watt\watt-startup.log`.
- **Two tray icons**: only one Watt process should run; the second instance signals the
  first via `Local\Watt.SingleInstance` / `Local\Watt.SingleInstance.Activate` and exits.
  Kill leftover processes with `Get-Process Watt | Stop-Process -Force`.

## Test-only script parameters

For automated testing against temporary locations (never used by normal installs):

- `install.ps1`: `-InstallDir <dir>`, `-SourceDir <dir>`, `-RunKeyPath <HKCU:\...>`,
  `-RunValueName <name>`, `-ShortcutName <name>`, `-StartMenuDir <dir>` plus the switches
  listed above.
- `uninstall.ps1`: the matching `-InstallDir`, `-RunKeyPath`, `-RunValueName`,
  `-ShortcutName`, `-StartMenuDir`.
