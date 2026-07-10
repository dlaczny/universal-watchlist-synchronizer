<#
.SYNOPSIS
    Setup Windows Scheduled Tasks for VOD Filter

.DESCRIPTION
    Creates scheduled tasks for:
    - Main sync (Letterboxd -> Plex/Radarr) - Every 10 minutes
    - Library sync (Plex Library -> Watchlist) - Every hour

.NOTES
    Run this script as Administrator
#>

param(
    [switch]$DryRun = $false
)

# Load configuration from .env
$EnvFile = Join-Path $PSScriptRoot ".env"

if (-not (Test-Path $EnvFile)) {
    Write-Error ".env file not found at $EnvFile"
    exit 1
}

Write-Host "Loading configuration from .env..." -ForegroundColor Cyan

# Parse .env file
$Config = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith("#")) {
        $parts = $line -split "=", 2
        if ($parts.Count -eq 2) {
            $key = $parts[0].Trim()
            $value = $parts[1].Trim()
            $Config[$key] = $value
        }
    }
}

# Get intervals (default values if not set)
$MainInterval = [int]($Config["SYNC_INTERVAL_MAIN"] ?? 10)
$LibraryInterval = [int]($Config["SYNC_INTERVAL_LIBRARY"] ?? 60)
$CleanupInterval = [int]($Config["SYNC_INTERVAL_CLEANUP"] ?? 60)

Write-Host "`nConfiguration:" -ForegroundColor Green
Write-Host "  Main Sync Interval: $MainInterval minutes"
Write-Host "  Library Sync Interval: $LibraryInterval minutes"
Write-Host "  Cleanup Interval: $CleanupInterval minutes"
Write-Host ""

# Paths
$ProjectPath = $PSScriptRoot
$ExePath = Join-Path $ProjectPath "dist\vod-filter.exe"
$LibraryScript = Join-Path $ProjectPath "sync_library_to_watchlist.py"
$CleanupScript = Join-Path $ProjectPath "cleanup_removed_movies.py"

# Find Python
$PythonPath = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $PythonPath) {
    Write-Warning "Python not found in PATH. Tasks will use 'python' command."
    $PythonPath = "python"
}

Write-Host "Paths:" -ForegroundColor Green
Write-Host "  Project: $ProjectPath"
Write-Host "  Executable: $ExePath"
Write-Host "  Python: $PythonPath"
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN MODE - No tasks will be created" -ForegroundColor Yellow
    Write-Host ""
}

# Function to create or update task
function Set-VodFilterTask {
    param(
        [string]$TaskName,
        [string]$Description,
        [string]$Program,
        [string]$Arguments,
        [string]$WorkingDirectory,
        [int]$IntervalMinutes,
        [int]$StartOffsetMinutes = 0
    )

    Write-Host "Setting up task: $TaskName" -ForegroundColor Cyan
    Write-Host "  Interval: Every $IntervalMinutes minutes"
    Write-Host "  Program: $Program"
    if ($Arguments) {
        Write-Host "  Arguments: $Arguments"
    }

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would create/update task" -ForegroundColor Yellow
        return
    }

    # Check if task exists
    $ExistingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($ExistingTask) {
        Write-Host "  Task already exists, updating..." -ForegroundColor Yellow
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    }

    # Create action
    if ($Arguments) {
        $Action = New-ScheduledTaskAction -Execute $Program -Argument $Arguments -WorkingDirectory $WorkingDirectory
    } else {
        $Action = New-ScheduledTaskAction -Execute $Program -WorkingDirectory $WorkingDirectory
    }

    # Create triggers:
    # 1. At system startup (runs immediately when system boots)
    # 2. Daily recurring (repeat every X minutes)
    $TriggerStartup = New-ScheduledTaskTrigger -AtStartup

    $StartTime = (Get-Date -Hour 0 -Minute $StartOffsetMinutes -Second 0)
    $TriggerRecurring = New-ScheduledTaskTrigger -Daily -At $StartTime
    $TriggerRecurring.Repetition = (New-ScheduledTaskTrigger -Once -At $StartTime -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) -RepetitionDuration ([TimeSpan]::FromDays(1))).Repetition

    $Triggers = @($TriggerStartup, $TriggerRecurring)

    # Create settings
    $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 30)

    # Register task
    try {
        Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Triggers -Settings $Settings -Description $Description -ErrorAction Stop | Out-Null
        Write-Host "  ✓ Task created successfully (runs at startup + every $IntervalMinutes min)" -ForegroundColor Green
    } catch {
        Write-Error "  ✗ Failed to create task: $_"
    }

    Write-Host ""
}

# Task 1: Main Sync
Write-Host "=" * 80 -ForegroundColor Cyan
Write-Host "Task 1: Main Sync (Letterboxd -> Plex/Radarr)" -ForegroundColor Cyan
Write-Host "=" * 80 -ForegroundColor Cyan

if (Test-Path $ExePath) {
    Set-VodFilterTask `
        -TaskName "VOD Filter - Main Sync" `
        -Description "Sync Letterboxd watchlist to Plex and Radarr every $MainInterval minutes" `
        -Program $ExePath `
        -Arguments $null `
        -WorkingDirectory (Split-Path $ExePath) `
        -IntervalMinutes $MainInterval `
        -StartOffsetMinutes 0
} else {
    Write-Warning "Executable not found at $ExePath"
    Write-Host "Please build the executable first with: python -m PyInstaller vod-filter.spec" -ForegroundColor Yellow
    Write-Host ""
}

# Task 2: Library Sync (offset by 5 minutes to avoid conflicts)
Write-Host "=" * 80 -ForegroundColor Cyan
Write-Host "Task 2: Library Sync (Plex Library -> Watchlist)" -ForegroundColor Cyan
Write-Host "=" * 80 -ForegroundColor Cyan

if (Test-Path $LibraryScript) {
    Set-VodFilterTask `
        -TaskName "VOD Filter - Library Sync" `
        -Description "Sync Plex library to watchlist every $LibraryInterval minutes" `
        -Program $PythonPath `
        -Arguments $LibraryScript `
        -WorkingDirectory $ProjectPath `
        -IntervalMinutes $LibraryInterval `
        -StartOffsetMinutes 5
} else {
    Write-Warning "Script not found at $LibraryScript"
}

# Task 3: Cleanup (offset by 55 minutes to avoid conflicts, runs on app start)
Write-Host "=" * 80 -ForegroundColor Cyan
Write-Host "Task 3: Cleanup Removed Movies (Delete from Plex when removed from Letterboxd)" -ForegroundColor Cyan
Write-Host "=" * 80 -ForegroundColor Cyan

if (Test-Path $CleanupScript) {
    Set-VodFilterTask `
        -TaskName "VOD Filter - Cleanup" `
        -Description "Remove movies from Plex when removed from Letterboxd every $CleanupInterval minutes" `
        -Program $PythonPath `
        -Arguments $CleanupScript `
        -WorkingDirectory $ProjectPath `
        -IntervalMinutes $CleanupInterval `
        -StartOffsetMinutes 55
} else {
    Write-Warning "Script not found at $CleanupScript"
}

# Summary
Write-Host "=" * 80 -ForegroundColor Green
if ($DryRun) {
    Write-Host "DRY RUN COMPLETE" -ForegroundColor Yellow
    Write-Host "Run without -DryRun to actually create tasks" -ForegroundColor Yellow
} else {
    Write-Host "SETUP COMPLETE" -ForegroundColor Green
    Write-Host ""
    Write-Host "Created scheduled tasks:" -ForegroundColor White
    Write-Host "  1. VOD Filter - Main Sync (at startup + every $MainInterval minutes)"
    Write-Host "  2. VOD Filter - Library Sync (at startup + every $LibraryInterval minutes at :05)"
    Write-Host "  3. VOD Filter - Cleanup (at startup + every $CleanupInterval minutes at :55)"
    Write-Host ""
    Write-Host "To view tasks:" -ForegroundColor White
    Write-Host '  Get-ScheduledTask | Where-Object {$_.TaskName -like "*VOD Filter*"}' -ForegroundColor Gray
    Write-Host ""
    Write-Host "To remove tasks:" -ForegroundColor White
    Write-Host '  Unregister-ScheduledTask -TaskName "VOD Filter - Main Sync" -Confirm:$false' -ForegroundColor Gray
    Write-Host '  Unregister-ScheduledTask -TaskName "VOD Filter - Library Sync" -Confirm:$false' -ForegroundColor Gray
    Write-Host '  Unregister-ScheduledTask -TaskName "VOD Filter - Cleanup" -Confirm:$false' -ForegroundColor Gray
}
Write-Host "=" * 80 -ForegroundColor Green
