@echo off
setlocal
rem Standalone CapFrameX smoke-test launcher. No registry or system .NET changes.
for %%I in ("%~dp0..\..") do set "ROOT=%%~fI"
set "DOTNET_ROOT_X64=%ROOT%\runtime"
set "DOTNET_ROOT=%ROOT%\runtime"
set "G_TOTAL_PRIVATE_DOTNET=%ROOT%\runtime"
set "DOTNET_MULTILEVEL_LOOKUP=0"
if not exist "%DOTNET_ROOT_X64%\dotnet.exe" (
  echo Missing private .NET 10 runtime: "%DOTNET_ROOT_X64%\dotnet.exe"
  pause
  exit /b 1
)
if not exist "%~dp0CapFrameX.exe" (
  echo Missing CapFrameX.exe in the test module.
  pause
  exit /b 2
)
if not exist "%~dp0Portable\Logs" mkdir "%~dp0Portable\Logs"
rem .NET 10 host trace confirms the loaded runtime path for diagnosis.
set "DOTNET_HOST_TRACE=1"
set "DOTNET_HOST_TRACEFILE=%~dp0Portable\Logs\private-dotnet-host-trace.txt"
"%~dp0CapFrameX.exe" %*
set "EXIT=%ERRORLEVEL%"
if not "%EXIT%"=="0" (
  echo CapFrameX exited with error %EXIT%.
  echo See Portable\Logs\private-dotnet-host-trace.txt and CapFrameX logs.
  pause
)
exit /b %EXIT%
