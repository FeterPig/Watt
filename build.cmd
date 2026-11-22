@echo off
setlocal EnableExtensions
pushd "%~dp0" >nul

set "VERSION=2.1.0"
set "ROOT=%CD%"
set "DIST=%ROOT%\dist"
set "RELEASE=%DIST%\Watt-%VERSION%-win-x64"

set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [build] dotnet SDK was not found on PATH.
    echo [build] Install the .NET SDK 9 and try again.
    goto fail
)

echo [build] Watt %VERSION%
echo [build] repository: %ROOT%

call :build "src\Watt\Watt.csproj"
if errorlevel 1 goto fail

call :build "src\Watt.Setup\Watt.Setup.csproj"
if errorlevel 1 goto fail

for /d %%D in ("%ROOT%\src\Watt.Plugins.*") do (
    call :build "%%~fD\%%~nxD.csproj"
    if errorlevel 1 goto fail
)

echo [build] staging release folder: %RELEASE%
if exist "%RELEASE%" rmdir /s /q "%RELEASE%"
mkdir "%RELEASE%"
if errorlevel 1 goto fail
mkdir "%RELEASE%\plugins"
if errorlevel 1 goto fail

set "APPBIN=%ROOT%\src\Watt\bin\Release\net48"
set "SETUPBIN=%ROOT%\src\Watt.Setup\bin\Release\net48"

copy /Y "%APPBIN%\Watt.exe" "%RELEASE%\" >nul
if errorlevel 1 goto fail
copy /Y "%APPBIN%\Watt.exe.config" "%RELEASE%\" >nul
if errorlevel 1 goto fail
copy /Y "%APPBIN%\Watt.Plugin.Contracts.dll" "%RELEASE%\" >nul
if errorlevel 1 goto fail
copy /Y "%APPBIN%\Watt.Plugin.Support.dll" "%RELEASE%\" >nul
if errorlevel 1 goto fail
copy /Y "%SETUPBIN%\Watt.Setup.exe" "%RELEASE%\" >nul
if errorlevel 1 goto fail

for /d %%D in ("%ROOT%\src\Watt.Plugins.*") do (
    copy /Y "%%~fD\bin\Release\net48\%%~nxD.dll" "%RELEASE%\plugins\" >nul
    if errorlevel 1 goto fail
)

copy /Y "README.md" "%RELEASE%\" >nul
if errorlevel 1 goto fail
if exist "LICENSE" (
    copy /Y "LICENSE" "%RELEASE%\" >nul
    if errorlevel 1 goto fail
)
if exist "THIRD-PARTY-NOTICES.md" (
    copy /Y "THIRD-PARTY-NOTICES.md" "%RELEASE%\" >nul
    if errorlevel 1 goto fail
)
xcopy "docs" "%RELEASE%\docs\" /E /I /Y /Q >nul
if errorlevel 1 goto fail

where tar >nul 2>nul
if errorlevel 1 (
    echo [build] WARNING: tar.exe not found; release folder is ready but no zip was created.
) else (
    if exist "%DIST%\Watt-%VERSION%-win-x64.zip" del /Q "%DIST%\Watt-%VERSION%-win-x64.zip"
    tar -a -c -f "%DIST%\Watt-%VERSION%-win-x64.zip" -C "%DIST%" "Watt-%VERSION%-win-x64"
    if errorlevel 1 (
        echo [build] WARNING: zip creation failed; release folder is still usable.
    ) else (
        echo [build] zip: %DIST%\Watt-%VERSION%-win-x64.zip
    )
)

echo.
echo [build] OK
echo [build] release: %RELEASE%
echo [build] install: run Watt.Setup.exe from the release folder
popd
exit /b 0

:build
echo [build] dotnet build "%~1" -c Release
dotnet build "%~1" -c Release
exit /b %ERRORLEVEL%

:fail
echo.
echo [build] FAILED
popd
exit /b 1
