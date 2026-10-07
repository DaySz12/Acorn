@echo off
rem Acorn setup for Windows.
rem  - Checks for the .NET SDK (does NOT download or install anything).
rem  - Restores packages and publishes a Release build into dist\.
rem  - Creates Desktop and Start Menu shortcuts (skip with --no-shortcuts).
rem Safe to run again. Never touches your vault in %APPDATA%\Acorn.
setlocal EnableExtensions
cd /d "%~dp0"

set "REQUIRED_MAJOR=10"
set "CREATE_SHORTCUTS=1"
if /i "%~1"=="--no-shortcuts" set "CREATE_SHORTCUTS=0"

echo.
echo === Acorn setup (Windows) ===
echo.

where dotnet >nul 2>nul
if errorlevel 1 goto :nodotnet

set "HAVE_SDK="
for /f "tokens=1 delims=. " %%v in ('dotnet --list-sdks 2^>nul') do (
    if %%v GEQ %REQUIRED_MAJOR% set "HAVE_SDK=1"
)
if not defined HAVE_SDK goto :nodotnet

set "RID=win-x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RID=win-arm64"
set "OUT=%~dp0dist\%RID%"

echo [1/3] Restoring NuGet packages (needs internet the first time only)...
dotnet restore "src\Acorn.App\Acorn.App.csproj" -r %RID%
if errorlevel 1 goto :fail

echo [2/3] Building Release into dist\%RID% ...
dotnet publish "src\Acorn.App\Acorn.App.csproj" -c Release -r %RID% --self-contained false --no-restore -o "%OUT%"
if errorlevel 1 goto :fail

if "%CREATE_SHORTCUTS%"=="0" (
    echo [3/3] Skipping shortcuts ^(--no-shortcuts^).
    goto :done
)

echo [3/3] Creating Desktop and Start Menu shortcuts...
set "ACORN_TARGET=%OUT%\Acorn.exe"
powershell -NoProfile -Command "$shell = New-Object -ComObject WScript.Shell; foreach ($dir in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) { $link = $shell.CreateShortcut((Join-Path $dir 'Acorn.lnk')); $link.TargetPath = $env:ACORN_TARGET; $link.WorkingDirectory = (Split-Path $env:ACORN_TARGET); $link.IconLocation = $env:ACORN_TARGET; $link.Description = 'Acorn offline vault'; $link.Save() }"
if errorlevel 1 echo     (Could not create shortcuts. You can run "%OUT%\Acorn.exe" directly.)

:done
echo.
echo Done. Acorn is in: %OUT%\Acorn.exe
echo Your vault will be stored in: %APPDATA%\Acorn
echo After this first setup Acorn works fully offline.
echo.
exit /b 0

:nodotnet
echo The .NET SDK %REQUIRED_MAJOR% or newer was not found.
echo Download it from the official site, install it, then run setup.bat again:
echo     https://dotnet.microsoft.com/download/dotnet/%REQUIRED_MAJOR%.0
echo (This script never downloads or runs installers by itself.)
echo.
exit /b 1

:fail
echo.
echo Setup failed. See the messages above. Your vault was not touched.
exit /b 1
