# Development prerequisites

This is a Windows-only .NET console app (it uses USB P/Invoke), targeting `net10.0-windows`.
It is built as a `WinExe`, so it runs without a console window when launched in the background
(e.g. by the Scheduled Task), while still printing to the console when launched from a terminal.

## Required tools

| Tool          | Purpose                                  | Install                                   |
| ------------- | ---------------------------------------- | ----------------------------------------- |
| .NET 10 SDK   | Build and run the app                    | https://dotnet.microsoft.com/download     |
| Azure CLI     | Read secrets from Key Vault at deploy    | https://aka.ms/installazurecliwindows     |
| sqlcmd (go)   | Apply the SQL schema (`deploy/deploy.ps1`) | `winget install Microsoft.Sqlcmd` *(see home-env/development.md for a no-elevation install)* |

The shared Azure **datastore** must already be provisioned from the `home-env` repository.

## Build, run, test

```powershell
dotnet build            # compile (expects 0 warnings)
dotnet run              # run the logger (requires CO2_SQL_CONNECTION_STRING)
```

There is no automated test suite. Verify the data path by running the logger and querying
the database:

```powershell
# Quick local smoke test with a faster sample interval:
$env:CO2_SAMPLE_INTERVAL_SECONDS = '5'
dotnet run
```

```sql
SELECT TOP 5 reading_time, volts, ppm FROM co2.readings ORDER BY reading_time DESC;
```

## Configuration

The app reads `CO2_SQL_CONNECTION_STRING` (and optional `CO2_SAMPLE_INTERVAL_SECONDS`,
`CO2_LOG_DIRECTORY`, `CO2_LOG_RETENTION_DAYS`) from the environment — see the README for the
full table. `deploy/deploy.ps1` sets `CO2_SQL_CONNECTION_STRING` as a user environment
variable (sourced from Key Vault), so restart your shell after deploying. Never commit the
connection string.

For always-on background operation, install the logger as a Scheduled Task with
`deploy/install-scheduled-task.ps1` (see the README's "Always-on operation").

## Project layout

| Path                  | Purpose                                                     |
| --------------------- | ----------------------------------------------------------- |
| `Program.cs`          | Entry point: the read → store loop and graceful shutdown.   |
| `Co2Sensor.cs`        | Reads the device and converts voltage to PPM.               |
| `ReadingStore.cs`     | Inserts readings into Azure SQL with transient-error retry. |
| `Logger.cs`           | Console + daily-rolling file + Application event log logging.|
| `USBM.cs`             | Low-level USB voltmeter interop (P/Invoke). Leave as-is.    |
| `sql/schema.sql`      | Idempotent `co2` schema, `co2.readings` table, `co2_writer`.|
| `deploy/deploy.ps1`   | Applies the schema and configures credentials.              |
| `deploy/install-scheduled-task.ps1` | Installs/removes the background Scheduled Task. |
| `docs/grafana-queries.md` | Example Grafana SQL queries.                            |
