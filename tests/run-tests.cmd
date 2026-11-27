@echo off
setlocal EnableExtensions
pushd "%~dp0.." >nul

set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"

echo [tests] building host, plugins and test runner...
dotnet build ".\src\Watt\Watt.csproj" -c Release
if errorlevel 1 goto fail
dotnet build ".\src\Watt.Plugins.Windows\Watt.Plugins.Windows.csproj" -c Release
if errorlevel 1 goto fail
dotnet build ".\src\Watt.Plugins.BleGatt\Watt.Plugins.BleGatt.csproj" -c Release
if errorlevel 1 goto fail
dotnet build ".\tests\Watt.Tests\Watt.Tests.csproj" -c Release
if errorlevel 1 goto fail

echo [tests] running automated tests...
".\tests\Watt.Tests\bin\Release\net48\Watt.Tests.exe" %*
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" goto failed

echo [tests] OK
popd
exit /b 0

:failed
echo [tests] FAILED
popd
exit /b %RESULT%

:fail
echo [tests] BUILD FAILED
popd
exit /b 1
