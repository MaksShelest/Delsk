@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
set PYTHONUTF8=1
set PYTHONIOENCODING=utf-8
title Delsk Research Lab
where py >nul 2>nul
if not errorlevel 1 (
    py -3 scripts\research_lab.py --prepare --open-browser --port 0 %*
) else (
    where python >nul 2>nul
    if errorlevel 1 (
        echo.
        echo Не найден Python 3.10 или новее.
        echo Он нужен, чтобы открыть лабораторию и очередь исследований.
        echo Саму экономику считает .NET. Godot и Ollama для обычного опыта не нужны.
        echo Установите Python с https://www.python.org/downloads/ и включите py launcher.
        echo.
        pause
        exit /b 1
    )
    python scripts\research_lab.py --prepare --open-browser --port 0 %*
)
set "lab_exit=%errorlevel%"
if not "%lab_exit%"=="0" (
    echo.
    echo Лаборатория не запустилась. Сообщение выше и журнал: artifacts\lab\startup
    echo Окно останется открытым, чтобы текст ошибки можно было прочитать.
    echo.
    pause
)
exit /b %lab_exit%
