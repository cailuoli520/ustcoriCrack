@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0check-runtime.ps1" -RuntimePath "%~dp0." %*
exit /b %errorlevel%
