@echo off
rem Start the exhibition at Windows login (current user Startup folder). Run it yourself.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0autostart.ps1" install %*
pause
