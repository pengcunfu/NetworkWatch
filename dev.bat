@echo off
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo dotnet not found. Install .NET SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo Starting NetworkWatch...
dotnet run --project "src\NetworkWatch\NetworkWatch.csproj" -c Debug

if errorlevel 1 pause
