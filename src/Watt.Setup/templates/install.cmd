@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

if not exist "Watt.Setup.exe" (
    echo [install] Watt.Setup.exe not found in this folder.
    echo [install] Run build.cmd first and use the release folder under dist.
    pause
    exit /b 1
)

"Watt.Setup.exe" install %*
set "CODE=%ERRORLEVEL%"

if not "%CODE%"=="0" (
    echo.
    pause
    exit /b %CODE%
)

if "%~1"=="" pause
exit /b 0
