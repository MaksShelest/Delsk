#!/usr/bin/env python3
"""Compatibility entry point for the current browser/runner acceptance gate.
Author: Шелестов Максим Евгеньевич.
"""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name('check_research_workspace.py')),run_name='__main__')
