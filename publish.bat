@echo off
pwsh -NoProfile -File "%~dp0scripts\Build-EXPCdelete.ps1"
exit /b %errorlevel%
