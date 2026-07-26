@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-upload-openmsa.ps1"
if %errorlevel% neq 0 exit /b %errorlevel%
