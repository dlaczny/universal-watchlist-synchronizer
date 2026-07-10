@echo off
echo ================================================================================
echo VOD Filter - Building COMPLETE Executable (All 3 Syncs)
echo ================================================================================
echo.

echo [1/2] Cleaning previous builds...
if exist "dist\vod-filter-complete.exe" del /q "dist\vod-filter-complete.exe"
if exist "build\vod-filter-complete" rmdir /s /q "build\vod-filter-complete"

echo [2/2] Building complete executable with PyInstaller...
python -m PyInstaller vod-filter-complete.spec --clean

echo.
echo ================================================================================
if exist "dist\vod-filter-complete.exe" (
    echo [SUCCESS] Complete executable built successfully!
    echo Location: dist\vod-filter-complete.exe
    echo.
    echo This executable runs ALL 3 sync operations:
    echo   1. Cleanup ^(remove deleted movies^)
    echo   2. Main Sync ^(Letterboxd -^> Plex/Radarr^)
    echo   3. Library Sync ^(Plex Library -^> Watchlist^)
    echo.
    echo To run:
    echo   dist\vod-filter-complete.exe           ^(production mode^)
    echo   dist\vod-filter-complete.exe --dry-run ^(test mode^)
) else (
    echo [ERROR] Build failed!
    echo Check the error messages above.
)
echo ================================================================================
echo.

pause
