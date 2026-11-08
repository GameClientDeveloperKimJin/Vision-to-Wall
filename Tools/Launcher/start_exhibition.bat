@echo off
rem Vision-to-Wall: hand sensor -> game, with a watchdog that restarts either one. See README.md.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0exhibition.ps1" start %*
exit /b %ERRORLEVEL%
