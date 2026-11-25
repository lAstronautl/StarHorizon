@echo off
cd /d "%~dp0"
python ui_editor.py %*
if errorlevel 1 pause
