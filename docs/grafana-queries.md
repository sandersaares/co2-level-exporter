# Example Grafana queries

Queries for the **Microsoft SQL Server** data source pointed at the `datastore` database
(connected via the shared read-only `grafana` user — see `home-env/docs/grafana.md`).

`co2.readings` columns: `reading_time` (UTC, `datetime2`), `volts` (`decimal`), `ppm` (`int`).
`reading_time` is stored in UTC; Grafana converts to the dashboard time zone for display.

## CO2 (PPM) over time — raw

For short ranges where every sample is useful.

```sql
SELECT
    reading_time AS time,
    ppm
FROM co2.readings
WHERE $__timeFilter(reading_time)
ORDER BY reading_time;
```

## CO2 (PPM) over time — downsampled

Averages into buckets so long ranges stay fast and readable. Adjust the interval (`5m`,
`1h`, …) to taste, or use Grafana's `$__interval` variable.

```sql
SELECT
    $__timeGroup(reading_time, '5m') AS time,
    AVG(CAST(ppm AS float)) AS ppm
FROM co2.readings
WHERE $__timeFilter(reading_time)
GROUP BY $__timeGroup(reading_time, '5m')
ORDER BY 1;
```

## Sensor voltage over time

Useful for diagnosing the sensor itself.

```sql
SELECT
    $__timeGroup(reading_time, '5m') AS time,
    AVG(CAST(volts AS float)) AS volts
FROM co2.readings
WHERE $__timeFilter(reading_time)
GROUP BY $__timeGroup(reading_time, '5m')
ORDER BY 1;
```

## Latest reading (Stat panel)

```sql
SELECT TOP 1
    reading_time AS time,
    ppm
FROM co2.readings
ORDER BY reading_time DESC;
```

## Daily min / avg / max (Table panel)

```sql
SELECT
    CAST(reading_time AS date) AS day,
    MIN(ppm) AS min_ppm,
    AVG(ppm) AS avg_ppm,
    MAX(ppm) AS max_ppm
FROM co2.readings
WHERE $__timeFilter(reading_time)
GROUP BY CAST(reading_time AS date)
ORDER BY day;
```

## Tips

- Set the panel unit to **ppm** and add thresholds (e.g. 800 / 1200 / 2000) to highlight
  poor ventilation.
- For very long ranges, prefer the downsampled query (or bind the bucket to `$__interval`)
  to limit the number of points returned.
