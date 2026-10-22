#!/bin/sh
cd "$(dirname "$0")" || exit 1
exec python3 scripts/research_lab.py --prepare --open-browser --port 0 "$@"
