@echo off
rem Double-click to uninstall Battery and Performance Manager (Windows asks for administrator rights once).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
