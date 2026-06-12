# Development prerequisites

This is a Windows-only .NET console app (it uses USB P/Invoke), targeting `net10.0-windows`.

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

The app reads `CO2_SQL_CONNECTION_STRING` (and optional `CO2_SAMPLE_INTERVAL_SECONDS`) from
the environment. `deploy/deploy.ps1` sets `CO2_SQL_CONNECTION_STRING` as a user environment
variable (sourced from Key Vault), so restart your shell after deploying. Never commit the
connection string.

## Project layout

| Path                  | Purpose                                                     |
| --------------------- | ----------------------------------------------------------- |
| `Program.cs`          | Entry point: the read → store loop and graceful shutdown.   |
| `Co2Sensor.cs`        | Reads the device and converts voltage to PPM.               |
| `ReadingStore.cs`     | Inserts readings into Azure SQL with transient-error retry. |
| `USBM.cs`             | Low-level USB voltmeter interop (P/Invoke). Leave as-is.    |
| `sql/schema.sql`      | Idempotent `co2` schema, `co2.readings` table, `co2_writer`.|
| `deploy/deploy.ps1`   | Applies the schema and configures credentials.              |
| `docs/grafana-queries.md` | Example Grafana SQL queries.                            |
