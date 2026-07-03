@echo off
title vtecPoint - Adjust Point
cd /d "%~dp0"

echo ========================================
echo   vtecPoint - Adjust Point Tool
echo ========================================
echo.
echo Starting... browser will open automatically.
echo Press Ctrl+C to stop.
echo.

start "" "http://localhost:5252"
vtecPoint.exe --urls "http://localhost:5252"
