@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect-clients.ps1" %*
