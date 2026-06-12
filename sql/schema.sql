-- Idempotent schema for the CO2 exporter inside the shared "datastore" database.
-- Creates the co2 schema, the readings table, and the least-privilege co2_writer user
-- used by the logger application (INSERT only).
--
-- Run with: sqlcmd -S <server> -d datastore -U <admin> -P <pwd> -N -b -i schema.sql -v Co2WriterPassword="<password>"

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'co2')
    EXEC ('CREATE SCHEMA co2');

IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'co2' AND t.name = N'readings')
BEGIN
    CREATE TABLE co2.readings
    (
        reading_time datetime2(3) NOT NULL,
        volts        decimal(6, 3) NOT NULL,
        ppm          int          NOT NULL
    );

    -- Time-series access pattern: cluster on the timestamp.
    CREATE CLUSTERED INDEX IX_readings_reading_time ON co2.readings (reading_time);
END

-- Least-privilege writer used by the logger application.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'co2_writer')
    CREATE USER [co2_writer] WITH PASSWORD = N'$(Co2WriterPassword)';
ELSE
    ALTER USER [co2_writer] WITH PASSWORD = N'$(Co2WriterPassword)';

GRANT INSERT ON SCHEMA::co2 TO [co2_writer];
