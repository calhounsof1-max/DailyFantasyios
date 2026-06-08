@echo off
echo Building DailyFantasyIOS for iOS...
dotnet publish "%~dp0DailyFantasyIOS.csproj" -f net10.0-ios -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Build FAILED
    exit /b 1
)
echo.
echo Build complete. IPA located at:
echo   bin\Release\net10.0-ios\publish\
echo.
echo To install on device, use:
echo   ios-deploy --bundle bin\Release\net10.0-ios\publish\DailyFantasyIOS.app
pause
