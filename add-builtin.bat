@echo off
rem ======================================================================
rem  Adds every archive in a folder to Lab Reset as BUILT-IN.
rem  Built-in archives are stored in the Library folder and cannot be
rem  removed from inside the app.
rem
rem  Usage:  add-builtin.bat "C:\__RivanVMs"     (default if no folder given)
rem ======================================================================
setlocal
cd /d "%~dp0"
set "SRC=%~1"
if "%SRC%"=="" set "SRC=C:\__RivanVMs"
if not exist "%SRC%\" ( echo Folder not found: %SRC% & pause & exit /b 1 )
if not exist Library mkdir Library

for %%P in (*.zip *.7z *.rar *.tar *.tar.gz *.tgz) do (
  for %%F in ("%SRC%\%%P") do (
    echo Copying %%~nxF ...
    robocopy "%SRC%" Library "%%~nxF" /COPY:DAT /R:1 /W:1 /NJH /NJS /NP >nul
    findstr /i /x /c:"%%~nxF" Library\builtin.txt >nul 2>&1 || (echo %%~nxF>>Library\builtin.txt)
  )
)
echo.
echo Done. Built-in archives are listed in Library\builtin.txt
if "%~2"=="" pause
