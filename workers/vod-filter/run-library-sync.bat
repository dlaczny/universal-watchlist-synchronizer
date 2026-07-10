@echo off
REM Sync Plex Library to Watchlist
REM Designed to run as a scheduled task (cron job)

cd /d "%~dp0"
python sync_library_to_watchlist.py

exit /b %ERRORLEVEL%
