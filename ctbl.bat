@echo off
setlocal enabledelayedexpansion

:: ================================================================
::  CTBL++ v0.0.9.9 — Build and Launch
:: ================================================================

set "ROOT=%~dp0"

:menu
echo.
echo  ================================================
echo   CTBL++ v0.0.9.9
echo  ================================================
echo   [1] Build all projects
echo   [2] Launch Installer
echo   [3] Launch UI (Desktop)
echo   [4] Launch Engine (console mode)
echo   [0] Exit
echo  ================================================
echo.
set /p choice="  Select option: "

if "%choice%"=="1" goto build
if "%choice%"=="2" goto launch_installer
if "%choice%"=="3" goto launch_ui
if "%choice%"=="4" goto launch_engine
if "%choice%"=="0" exit /b 0
echo  Invalid option.
goto menu

:: ================================================================
:build
:: ================================================================
echo.
echo  [BUILD] Building all projects...
echo.

echo  [1/5] CtblPlusPlus.Engine...
dotnet publish "%ROOT%CtblPlusPlus.Engine\CtblPlusPlus.Engine.csproj" -c Debug -o "%ROOT%_payload" --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Engine
    pause
    goto menu
)

echo  [2/5] CtblPlusPlus.Wd1...
dotnet publish "%ROOT%CtblPlusPlus.Wd1\CtblPlusPlus.Wd1.csproj" -c Debug -o "%ROOT%_payload" --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Wd1
    pause
    goto menu
)

echo  [3/5] CtblPlusPlus.Wd2...
dotnet publish "%ROOT%CtblPlusPlus.Wd2\CtblPlusPlus.Wd2.csproj" -c Debug -o "%ROOT%_payload" --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Wd2
    pause
    goto menu
)

echo         Packaging Payload.zip for Installer...
if exist "%ROOT%CtblPlusPlus.Installer\Payload.zip" del "%ROOT%CtblPlusPlus.Installer\Payload.zip"
powershell -NoProfile -Command "Compress-Archive -Path '%ROOT%_payload\*' -DestinationPath '%ROOT%CtblPlusPlus.Installer\Payload.zip' -Force"
if %errorlevel% neq 0 (
    echo  FAILED: Payload.zip creation
    pause
    goto menu
)
rmdir /s /q "%ROOT%_payload" >nul 2>&1
echo         Payload.zip updated.

echo  [4/5] CtblPlusPlus.Installer...
dotnet build "%ROOT%CtblPlusPlus.Installer\CtblPlusPlus.Installer.csproj" -c Debug --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Installer
    pause
    goto menu
)

echo  [5/5] CtblPlusPlus.Desktop (UI)...
dotnet build "%ROOT%CtblPlusPlus.Desktop\CtblPlusPlus.Desktop.csproj" -c Debug --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Desktop
    pause
    goto menu
)

echo.
echo  BUILD SUCCEEDED — all 5 projects compiled.
echo.
pause
goto menu

:: ================================================================
:launch_installer
:: ================================================================
echo.
echo  Launching Installer...
set "INSTALLER_EXE=%ROOT%CtblPlusPlus.Installer\bin\Debug\net10.0-windows\CtblPlusPlus.Installer.exe"
if not exist "%INSTALLER_EXE%" (
    echo  Installer not built yet. Run [1] Build first.
    pause
    goto menu
)
start "" "%INSTALLER_EXE%"
goto menu

:: ================================================================
:launch_ui
:: ================================================================
echo.
echo  Launching Desktop UI...
set "UI_EXE=%ROOT%CtblPlusPlus.Desktop\bin\Debug\net10.0-windows\CtblPlusPlus.exe"
if not exist "%UI_EXE%" (
    echo  Desktop UI not built yet. Run [1] Build first.
    pause
    goto menu
)
start "" "%UI_EXE%"
goto menu

:: ================================================================
:launch_engine
:: ================================================================
echo.
echo  Launching Engine in console mode...
echo  (Press Ctrl+C to stop)
echo.
set "ENGINE_EXE=%ROOT%CtblPlusPlus.Engine\bin\Debug\net10.0-windows\CtblPlusPlus.Engine.exe"
if not exist "%ENGINE_EXE%" (
    echo  Engine not built yet. Run [1] Build first.
    pause
    goto menu
)
"%ENGINE_EXE%"
pause
goto menu
