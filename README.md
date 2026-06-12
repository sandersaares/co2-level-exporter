# co2-level-exporter

A small Windows service-style console app that reads CO2 concentration from a USB voltmeter
sensor attached to this PC and logs each reading to a shared Azure SQL database
(`co2.readings`). Long-term visualization is done in Grafana Cloud.

## How it works

```
USB CO2 sensor  ──►  co2-level-exporter (this app)  ──►  Azure SQL "datastore" (co2.readings)  ──►  Grafana Cloud
```

- The sensor is a USB voltmeter (`VID_04d8&PID_fc39`); `USBM.cs` talks to it via P/Invoke,
  which is why the app targets `net10.0-windows`.
- Voltage is converted to PPM with an experimentally-determined linear factor
  (`ppm = volts * 197.5`, in `Co2Sensor.cs`).
- A reading is taken every 30 seconds (configurable) and inserted into Azure SQL with
  automatic retry on transient errors (`ReadingStore.cs`).
- A zero reading indicates a likely device fault; the app exits with a non-zero code so a
  supervisor (Scheduled Task / service) can restart it and re-initialize the device.

## Configuration (environment variables)

| Variable                      | Required | Default | Description                                    |
| ----------------------------- | -------- | ------- | ---------------------------------------------- |
| `CO2_SQL_CONNECTION_STRING`   | yes      | —       | Azure SQL connection string for the `co2_writer` user. Set by `deploy/deploy.ps1`. |
| `CO2_SAMPLE_INTERVAL_SECONDS` | no       | `30`    | Seconds between readings.                      |
| `CO2_LOG_DIRECTORY`           | no       | `%LOCALAPPDATA%\co2-level-exporter\logs` | Directory for daily-rolling log files. |
| `CO2_LOG_RETENTION_DAYS`      | no       | `30`    | Days of log files to keep (pruned at startup; `0` keeps all). |

No secrets are stored in the repo; the connection string comes from Key Vault via the deploy
script (see below).

## Setup

1. Provision the shared datastore (once, in the `home-env` repo): see its README.
2. Apply this project's schema and credentials:

   ```powershell
   ./deploy/deploy.ps1 -Verbose
   ```

   This creates the `co2` schema, the `co2.readings` table and the write-only `co2_writer`
   user, stores its secrets in Key Vault, and sets the `CO2_SQL_CONNECTION_STRING` user
   environment variable.

3. Run the logger:

   ```powershell
   dotnet run
   ```

For visualization, connect Grafana Cloud to the datastore (see `home-env/docs/grafana.md`)
and use the example queries in [docs/grafana-queries.md](docs/grafana-queries.md).

## Development

See [development.md](development.md) for prerequisites and the build/run workflow.

## Always-on operation

Install the logger as a background Windows Scheduled Task:

```powershell
# Run once from an elevated PowerShell to also enable Application event log entries.
./deploy/install-scheduled-task.ps1 -Verbose
```

This publishes the app to `%LOCALAPPDATA%\Programs\co2-level-exporter` and registers a task that
starts at log on, runs hidden as the current user, restarts automatically if the process exits
(it exits on device fault by design), and is re-launched by a watchdog if it stops. The task
inherits `CO2_SQL_CONNECTION_STRING` from the user environment. Remove it with
`./deploy/install-scheduled-task.ps1 -Uninstall`.

The SQL server accepts connections from any IP, so no firewall setup is needed on the sensor PC.

### Logging

The app writes timestamped lines to a daily-rolling file under
`%LOCALAPPDATA%\co2-level-exporter\logs` (and to the console when run from a terminal). When the
installer is run elevated once, it also registers an Application event log source named
`co2-level-exporter`, after which warnings and errors surface in Event Viewer. Tail today's file:

```powershell
Get-Content (Join-Path $env:LOCALAPPDATA "co2-level-exporter\logs\co2-level-exporter-$(Get-Date -Format yyyyMMdd).log") -Wait -Tail 20
```

> The most reliable failure signal is the data itself: a Grafana "no data" / stale-data alert on
> `co2.readings` catches the sensor PC being off or the logger being stuck, regardless of logs.
