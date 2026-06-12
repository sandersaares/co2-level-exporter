namespace co2_level_exporter
{
    public static class Program
    {
        public static async Task<int> Main()
        {
            var logger = new Logger();

            var connectionString = Environment.GetEnvironmentVariable("CO2_SQL_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger.Error("CO2_SQL_CONNECTION_STRING environment variable is not set.");
                return 1;
            }

            var interval = ResolveSampleInterval();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var sensor = new Co2Sensor();
            try
            {
                sensor.Open();
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to open the CO2 sensor device: {ex.Message}");
                return 1;
            }

            var store = new ReadingStore(connectionString);

            logger.Info(
                $"Started. Sampling every {interval.TotalSeconds:0} s. Logs: {logger.Directory}. " +
                $"Event log: {(logger.EventLogEnabled ? "enabled" : "disabled")}.",
                toEventLog: true);

            while (!cts.IsCancellationRequested)
            {
                Co2Reading reading;
                try
                {
                    reading = sensor.Read();
                }
                catch (Exception ex)
                {
                    // A zero/failed reading indicates the device may be malfunctioning. Exit so the
                    // host (scheduled task) restarts the process and re-initializes the device.
                    logger.Error($"Device read failed; exiting for supervisor restart: {ex.Message}");
                    return 2;
                }

                try
                {
                    await store.SaveAsync(reading, cts.Token);
                    logger.Info($"{reading.Volts,7:0.000} V  {reading.Ppm,5} ppm");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Persisting failed even after retries. Drop this reading and keep logging so a
                    // transient database outage does not terminate the process.
                    logger.Warning($"Failed to save reading: {ex.Message}");
                }

                try
                {
                    await Task.Delay(interval, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            logger.Info("Stopped.", toEventLog: true);
            return 0;
        }

        private static TimeSpan ResolveSampleInterval()
        {
            const int defaultSeconds = 30;
            var raw = Environment.GetEnvironmentVariable("CO2_SAMPLE_INTERVAL_SECONDS");
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
            return TimeSpan.FromSeconds(defaultSeconds);
        }
    }
}
