@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launch-testing.ps1" -Clients 1
if errorlevel 1 pause
