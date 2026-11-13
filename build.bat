@echo off
rem Builds LabReset.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No Visual Studio or downloads needed. Just double-click this file.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the C# compiler. Make sure .NET Framework 4.8 is installed.
  pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:LabReset.exe ^
  /win32manifest:src\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.VisualBasic.dll /r:System.ServiceProcess.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED - see the messages above.
  if "%~1"=="" pause
  exit /b 1
)

rem LabResetAgent.exe is copied to each lab PC so it can extract its own copy of an archive.
"%CSC%" /nologo /target:exe /optimize+ /platform:anycpu /out:LabResetAgent.exe ^
  /r:System.dll /r:System.Core.dll /r:System.ServiceProcess.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  agent\*.cs src\Paths.cs src\PcNames.cs src\Safety.cs src\VmFolder.cs src\ArchiveSignature.cs src\SevenZip.cs src\FileOps.cs src\Logger.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED ^(LabResetAgent.exe^) - see the messages above.
  if "%~1"=="" pause
  exit /b 1
)

if not exist Library mkdir Library
echo.
echo Built LabReset.exe and LabResetAgent.exe successfully.
if "%~1"=="" pause
