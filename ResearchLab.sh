#!/bin/sh
cd "$(dirname "$0")" || exit 1
if ! command -v python3 >/dev/null 2>&1; then
    echo "Не найден python3. Нужен Python 3.10 или новее: https://www.python.org/downloads/"
    echo "Он открывает лабораторию. Экономику считает .NET, Godot для неё не нужен."
    exit 1
fi
exec python3 scripts/research_lab.py --prepare --open-browser --port 0 "$@"
