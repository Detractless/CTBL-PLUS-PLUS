@echo off
setlocal enabledelayedexpansion

:: ================================================================
::  CTBL++ v0.2.1.2 — Build and Launch
:: ================================================================

set "ROOT=%~dp0"

:menu
echo.
echo  ================================================
echo   CTBL++ v0.2.1.2
echo  ================================================
echo   [1] Build all projects
echo   [2] Launch Installer
echo   [3] Launch Engine (console mode)
echo   [0] Exit
echo  ================================================
echo.
set /p choice="  Select option: "

if "%choice%"=="1" goto build
if "%choice%"=="2" goto launch_installer
if "%choice%"=="3" goto launch_engine
if "%choice%"=="0" exit /b 0
echo  Invalid option.
goto menu

:: ================================================================
:build
:: ================================================================
echo.
echo  [BUILD] Building all projects...
echo.

echo  [1/4] CtblPlusPlus.Engine...
dotnet publish "%ROOT%CtblPlusPlus.Engine\CtblPlusPlus.Engine.csproj" -c Debug -o "%ROOT%_payload" --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Engine
    pause
    goto menu
)

echo  [2/4] CtblPlusPlus.Wd1...
dotnet publish "%ROOT%CtblPlusPlus.Wd1\CtblPlusPlus.Wd1.csproj" -c Debug -o "%ROOT%_payload" --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Wd1
    pause
    goto menu
)

echo  [3/4] CtblPlusPlus.Wd2...
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

echo  [4/4] CtblPlusPlus.Installer...
dotnet build "%ROOT%CtblPlusPlus.Installer\CtblPlusPlus.Installer.csproj" -c Debug --nologo -v quiet
if %errorlevel% neq 0 (
    echo  FAILED: Installer
    pause
    goto menu
)

echo.
echo  BUILD SUCCEEDED — all 4 projects compiled.
echo  (CtblPlusPlus.Core library builds transitively as an Engine/Wd/Installer dependency.)
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
