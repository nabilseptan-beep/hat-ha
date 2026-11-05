@echo off
rem "run.bat" runs the unit tests: how yt-dlp and ffmpeg command lines are built and their output read.
rem "run.bat e2e" also runs the real engines on a video it makes with ffmpeg, downloads it through yt-dlp
rem from a web server on this machine (no internet needed), and opens and closes the window. Run Hatha.exe
rem once before, so the engines are installed, and close it.
setlocal
cd /d "%~dp0.."

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "OUT=%TEMP%\hatha_tests"
if not exist "%OUT%" mkdir "%OUT%"

"%FW%\csc.exe" /nologo /codepage:65001 /target:exe /out:"%OUT%\unit_tests.exe" src\YtDlp.cs src\Ffmpeg.cs src\Updater.cs tests\UnitTests.cs
if errorlevel 1 exit /b 1
"%OUT%\unit_tests.exe"
if errorlevel 1 exit /b 1

if /i not "%~1"=="e2e" exit /b 0

call "%~dp0..\build.bat"
if errorlevel 1 exit /b 1

"%FW%\csc.exe" /nologo /codepage:65001 /target:exe /out:"%OUT%\e2e.exe" ^
    /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ^
    src\YtDlp.cs src\Ffmpeg.cs src\Tools.cs tests\E2E.cs
if errorlevel 1 exit /b 1
"%OUT%\e2e.exe" "%CD%\Hatha.exe" "%OUT%\scratch"
