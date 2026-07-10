@echo off
echo ================================================================================
echo VOD Filter - Building Executable
echo ================================================================================
echo.

REM Check if virtual environment exists
if not exist "venv" (
    echo [ERROR] Virtual environment not found!
    echo Please run: python -m venv venv
    echo Then activate and install requirements
    pause
    exit /b 1
)

REM Activate virtual environment
call venv\Scripts\activate.bat

REM Install colorama if not present
echo [1/4] Installing colorama for colored output...
pip install colorama --quiet

REM Install PyInstaller if not present
echo [2/4] Installing PyInstaller...
pip install pyinstaller --quiet

REM Clean previous builds
echo [3/4] Cleaning previous builds...
if exist "dist" rmdir /s /q dist
if exist "build" rmdir /s /q build

REM Build executable
echo [4/4] Building executable with PyInstaller...
pyinstaller vod-filter.spec

echo.
echo ================================================================================
if exist "dist\vod-filter.exe" (
    echo [SUCCESS] Executable built successfully!
    echo Location: dist\vod-filter.exe
    echo.
    echo To run:
    echo   1. Copy dist\vod-filter.exe to your desired location
    echo   2. Make sure .env file is in the same directory
    echo   3. Double-click vod-filter.exe or run from command line
    echo.
    echo For dry-run mode: vod-filter.exe --dry-run
) else (
    echo [ERROR] Build failed!
)
echo ================================================================================
echo.

pause
