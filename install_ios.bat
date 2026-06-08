@echo off
echo Installing DailyFantasyIOS on connected iPhone...
set IPA_PATH=%~dp0bin\Release\net10.0-ios\publish\com.calho.dailyfantasyios.ipa

if not exist "%IPA_PATH%" (
    echo IPA not found at: %IPA_PATH%
    echo Run build_ios.bat first.
    pause
    exit /b 1
)

REM Try ideviceinstaller (install libimobiledevice from https://libimobiledevice.org)
where ideviceinstaller >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo Using ideviceinstaller...
    ideviceinstaller -i "%IPA_PATH%"
    goto done
)

echo ideviceinstaller not found.
echo Install it from: https://github.com/libimobiledevice-win32/imobiledevice-net
echo Or use iTunes / Apple Devices app on Windows to install the IPA manually.

:done
pause
