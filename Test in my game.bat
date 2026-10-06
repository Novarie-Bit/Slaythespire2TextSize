@echo off
rem Double-click me. Runs scripts\windows\EasyTools.ps1 -Action Test
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\EasyTools.ps1" -Action Test
echo.
pause
