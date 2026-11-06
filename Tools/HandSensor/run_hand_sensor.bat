@echo off
rem Vision-to-Wall HandSensor launcher (exhibition PC).
rem Usage: run_hand_sensor.bat [hand_sensor.py options]   e.g. run_hand_sensor.bat --preview
rem Unity config.json must have "sensorMode": "external".
setlocal
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo [HandSensor] .venv not found. Setup first:
    echo   py -3.12 -m venv .venv
    echo   .venv\Scripts\python.exe -m pip install -r requirements.txt
    pause
    exit /b 1
)

set NEED_MODEL=
if not exist "models\hand_landmarker.task" set NEED_MODEL=1
if not exist "models\pose_landmarker_lite.task" set NEED_MODEL=1
if defined NEED_MODEL (
    ".venv\Scripts\python.exe" download_model.py
    rem Only the hand model is required; without the pose model the sensor runs hand stream only.
    if not exist "models\hand_landmarker.task" (
        pause
        exit /b 1
    )
)

".venv\Scripts\python.exe" hand_sensor.py %*
set EXITCODE=%ERRORLEVEL%
if not "%EXITCODE%"=="0" pause
exit /b %EXITCODE%
