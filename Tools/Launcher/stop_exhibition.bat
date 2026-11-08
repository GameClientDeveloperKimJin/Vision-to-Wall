@echo off
rem Stops the watchdog, the hand sensor (normal stop: camera exposure back to auto), then the game.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0exhibition.ps1" stop %*
exit /b %ERRORLEVEL%
