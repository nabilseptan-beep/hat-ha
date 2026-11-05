@echo off
rem "run.bat" runs the unit tests: how yt-dlp and ffmpeg command lines are built and their output read.
setlocal
cd /d "%~dp0.."

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "OUT=%TEMP%\hatha_tests"
if not exist "%OUT%" mkdir "%OUT%"

"%FW%\csc.exe" /nologo /codepage:65001 /target:exe /out:"%OUT%\unit_tests.exe" src\YtDlp.cs tests\UnitTests.cs
if errorlevel 1 exit /b 1
"%OUT%\unit_tests.exe"
if errorlevel 1 exit /b 1
