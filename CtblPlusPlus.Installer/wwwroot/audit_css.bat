@echo off
setlocal

:: Define input and output files
set "INPUT_FILE=index.html"
set "REPORT_FILE=css_audit_report.txt"

:: Verify the target file exists
if not exist "%INPUT_FILE%" (
    echo [ERROR] Could not find '%INPUT_FILE%' in the current directory.
    exit /b 1
)

echo Starting CSS Audit on %INPUT_FILE%...
echo This operation is strictly read-only.

:: Initialize the report
echo ================================================== > "%REPORT_FILE%"
echo CSS AUDIT REPORT FOR: %INPUT_FILE% >> "%REPORT_FILE%"
echo ================================================== >> "%REPORT_FILE%"
echo. >> "%REPORT_FILE%"

:: 1. Scan for Internal <style> Blocks
echo --- INTERNAL STYLE BLOCKS --- >> "%REPORT_FILE%"
echo Searching for internal style tags...
findstr /I /N "<style" "%INPUT_FILE%" >> "%REPORT_FILE%" 2>nul
if errorlevel 1 (
    echo [None found] >> "%REPORT_FILE%"
)
echo. >> "%REPORT_FILE%"

:: 2. Scan for Inline 'style=' Attributes
echo --- INLINE CSS ATTRIBUTES --- >> "%REPORT_FILE%"
echo Searching for inline style attributes...
:: We use a search string for "style="
findstr /I /N "style=" "%INPUT_FILE%" >> "%REPORT_FILE%" 2>nul
if errorlevel 1 (
    echo [None found] >> "%REPORT_FILE%"
)
echo. >> "%REPORT_FILE%"

:: 3. Scan for closing </style> tags (for completeness)
echo --- CLOSING STYLE TAGS --- >> "%REPORT_FILE%"
findstr /I /N "</style>" "%INPUT_FILE%" >> "%REPORT_FILE%" 2>nul
if errorlevel 1 (
    echo [None found] >> "%REPORT_FILE%"
)
echo. >> "%REPORT_FILE%"

echo ================================================== >> "%REPORT_FILE%"
echo Audit Complete! 

echo.
echo Report generated successfully: %REPORT_FILE%
