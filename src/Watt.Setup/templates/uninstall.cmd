@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

if not exist "Watt.Setup.exe" (
    echo [uninstall] Watt.Setup.exe not found in this folder.
    echo [uninstall] Use the release folder under dist, or run Watt.Setup.exe uninstall directly.
    pause
    exit /b 1
)

"Watt.Setup.exe" uninstall %*
set "CODE=%ERRORLEVEL%"

if not "%CODE%"=="0" (
    echo.
    pause
    exit /b %CODE%
)

if "%~1"=="" pause
exit /b 0
