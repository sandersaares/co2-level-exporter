<#
.SYNOPSIS
    Installs (or removes) the CO2 exporter as an always-on background Scheduled Task.

.DESCRIPTION
    Publishes the app to a stable location and registers a Scheduled Task that runs it in the
    background for the current user. The task:
      - starts at log on (and immediately, on demand, when this script finishes);
      - is restarted automatically if the process exits (the app exits on device fault by design
        so the device can be re-initialized);
      - is re-launched by a periodic "watchdog" repetition if it is found not running, while
        MultipleInstances=IgnoreNew prevents duplicate instances.

    The app logs to a daily-rolling file (default %LOCALAPPDATA%\co2-level-exporter\logs) and,
    once its event source is registered (run this script from an elevated PowerShell once), also
    writes warnings and errors to the Windows Application event log so failures are visible in
    Event Viewer.

    The task relies on the CO2_SQL_CONNECTION_STRING user environment variable that
    deploy/deploy.ps1 sets. Configure the sample interval and log location with the optional
    parameters below (they are stored as user environment variables the app reads at startup).

.EXAMPLE
    # Install. Run once from an elevated PowerShell to also register the event log source.
    ./install-scheduled-task.ps1 -Verbose

.EXAMPLE
    # Re-install after a code change (re-publishes and re-registers the task).
    ./install-scheduled-task.ps1 -Verbose

.EXAMPLE
    # Remove the task (leaves published files and the event log source in place).
    ./install-scheduled-task.ps1 -Uninstall -Verbose
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$TaskName = 'co2-level-exporter',
    [string]$PublishDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\co2-level-exporter'),
    [string]$EventLogSource = 'co2-level-exporter',
    [int]$WatchdogIntervalMinutes = 5,

    # Optional: stored as user environment variables the app reads at startup.
    [string]$LogDirectory,
    [int]$SampleIntervalSeconds,

    # Publish a self-contained build (no dependency on a separately installed .NET runtime).
    [switch]$SelfContained,

    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$projectPath = Join-Path $repoRoot 'co2-level-exporter.csproj'
$exePath = Join-Path $PublishDirectory 'co2-level-exporter.exe'

function Test-IsElevated {
    Write-Verbose "Test-IsElevated: checking for administrator role"
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

function Test-EventLogSourceExists {
    param([Parameter(Mandatory)][string]$Source)
    try {
        return [System.Diagnostics.EventLog]::SourceExists($Source)
    }
    catch {
        Write-Verbose "Test-EventLogSourceExists: could not query source '$Source' - $($_.Exception.Message)"
        return $false
    }
}

# --- Uninstall path -----------------------------------------------------------------------------

if ($Uninstall) {
    Write-Verbose "install-scheduled-task.ps1: removing scheduled task '$TaskName'"
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        if ($PSCmdlet.ShouldProcess($TaskName, 'Unregister scheduled task')) {
            Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            Write-Host "Removed scheduled task '$TaskName'." -ForegroundColor Green
        }
    }
    else {
        Write-Host "Scheduled task '$TaskName' was not found (nothing to remove)." -ForegroundColor Yellow
    }
    Write-Verbose "install-scheduled-task.ps1: left published files in '$PublishDirectory' and event log source '$EventLogSource' untouched"
    return
}

# --- Preconditions ------------------------------------------------------------------------------

Write-Verbose "install-scheduled-task.ps1: starting install"
if (-not (Test-Path $projectPath)) {
    throw "Project not found at '$projectPath'."
}

if ([string]::IsNullOrWhiteSpace($env:CO2_SQL_CONNECTION_STRING) -and
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('CO2_SQL_CONNECTION_STRING', 'User'))) {
    Write-Warning "CO2_SQL_CONNECTION_STRING is not set for the current user. The logger exits immediately without it; run deploy/deploy.ps1 first."
}

# --- Optional configuration as user environment variables ---------------------------------------

if ($PSBoundParameters.ContainsKey('LogDirectory')) {
    Write-Verbose "install-scheduled-task.ps1: setting CO2_LOG_DIRECTORY user env var to '$LogDirectory'"
    [Environment]::SetEnvironmentVariable('CO2_LOG_DIRECTORY', $LogDirectory, 'User')
}
if ($PSBoundParameters.ContainsKey('SampleIntervalSeconds')) {
    Write-Verbose "install-scheduled-task.ps1: setting CO2_SAMPLE_INTERVAL_SECONDS user env var to '$SampleIntervalSeconds'"
    [Environment]::SetEnvironmentVariable('CO2_SAMPLE_INTERVAL_SECONDS', [string]$SampleIntervalSeconds, 'User')
}

# --- Register the Application event log source (needs elevation; best-effort) -------------------

if (Test-EventLogSourceExists -Source $EventLogSource) {
    Write-Verbose "install-scheduled-task.ps1: event log source '$EventLogSource' already exists"
}
elseif (Test-IsElevated) {
    Write-Verbose "install-scheduled-task.ps1: creating event log source '$EventLogSource' in the Application log"
    New-EventLog -LogName Application -Source $EventLogSource
    Write-Host "Registered Application event log source '$EventLogSource'." -ForegroundColor Green
}
else {
    Write-Warning "Event log source '$EventLogSource' is not registered and this session is not elevated. The app will log to file only. Re-run this script from an elevated PowerShell once to enable Application event log entries."
}

# --- Publish the app to a stable location -------------------------------------------------------

Write-Verbose "install-scheduled-task.ps1: stopping any running instance of task '$TaskName' to release the executable"
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

$publishArgs = @('publish', $projectPath, '-c', 'Release', '-o', $PublishDirectory, '--nologo')
if ($SelfContained) {
    $rid = "win-$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())"
    Write-Verbose "install-scheduled-task.ps1: publishing self-contained for runtime '$rid'"
    $publishArgs += @('--self-contained', 'true', '-r', $rid)
}
else {
    Write-Verbose "install-scheduled-task.ps1: publishing framework-dependent"
    $publishArgs += @('--self-contained', 'false')
}

Write-Verbose "install-scheduled-task.ps1: dotnet $($publishArgs -join ' ')"
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
if (-not (Test-Path $exePath)) { throw "Expected published executable not found at '$exePath'." }
Write-Verbose "install-scheduled-task.ps1: published executable is '$exePath'"

# --- Build and register the scheduled task ------------------------------------------------------

$userId = "$env:USERDOMAIN\$env:USERNAME"
Write-Verbose "install-scheduled-task.ps1: building task definition for user '$userId'"

$action = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $PublishDirectory

# Run at log on, then repeat as a watchdog: if the process has stopped, the next repetition
# relaunches it; IgnoreNew (below) keeps a single instance while it is alive.
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
$watchdog = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes $WatchdogIntervalMinutes)
$trigger.Repetition = $watchdog.Repetition

$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited

$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -RestartCount 3 `
    -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
$settings.Hidden = $true

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Verbose "install-scheduled-task.ps1: unregistering existing task '$TaskName' before re-creating"
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

if ($PSCmdlet.ShouldProcess($TaskName, 'Register scheduled task')) {
    Write-Verbose "install-scheduled-task.ps1: registering task '$TaskName'"
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description 'CO2 level exporter: reads the USB sensor and logs readings to Azure SQL.' | Out-Null

    Write-Verbose "install-scheduled-task.ps1: starting task '$TaskName'"
    Start-ScheduledTask -TaskName $TaskName
}

# --- Summary ------------------------------------------------------------------------------------

$resolvedLogDir = [Environment]::GetEnvironmentVariable('CO2_LOG_DIRECTORY', 'User')
if ([string]::IsNullOrWhiteSpace($resolvedLogDir)) {
    $resolvedLogDir = Join-Path $env:LOCALAPPDATA 'co2-level-exporter\logs'
}

Write-Host ""
Write-Host "CO2 exporter installed as a background scheduled task." -ForegroundColor Green
Write-Host "  Task name   : $TaskName"
Write-Host "  Executable  : $exePath"
Write-Host "  Runs as     : $userId (at log on; watchdog every $WatchdogIntervalMinutes min)"
Write-Host "  Log files   : $resolvedLogDir"
Write-Host "  Event log   : $(if (Test-EventLogSourceExists -Source $EventLogSource) { "Application / source '$EventLogSource'" } else { 'file only (source not registered)' })"
Write-Host ""
Write-Host "Check status : Get-ScheduledTaskInfo -TaskName $TaskName"
Write-Host "Tail logs    : Get-Content (Join-Path '$resolvedLogDir' ('co2-level-exporter-' + (Get-Date -Format yyyyMMdd) + '.log')) -Wait -Tail 20"
Write-Host "Uninstall    : ./install-scheduled-task.ps1 -Uninstall"
