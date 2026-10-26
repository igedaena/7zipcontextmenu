@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Portable-Menu.ps1" -Action Register
set "menu_result=%errorlevel%"
pause
exit /b %menu_result%
