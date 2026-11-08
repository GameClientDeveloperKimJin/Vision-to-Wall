@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0exhibition.ps1" status %*
pause
