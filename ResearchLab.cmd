@echo off
setlocal
cd /d "%~dp0"
where py >nul 2>nul
if not errorlevel 1 (
    py -3 scripts\research_lab.py --prepare --open-browser --port 0 %*
) else (
    where python >nul 2>nul
    if errorlevel 1 (
        echo Python 3.10+ is required. Install Python from python.org with the Python launcher.
        pause
        exit /b 1
    )
    python scripts\research_lab.py --prepare --open-browser --port 0 %*
)
set "lab_exit=%errorlevel%"
if not "%lab_exit%"=="0" (
    echo Research laboratory could not start. See the message above and artifacts\lab\startup logs.
    pause
)
exit /b %lab_exit%
