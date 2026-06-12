using System.Diagnostics;

namespace co2_level_exporter
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Writes timestamped log lines to the console, a daily-rolling file, and (best-effort) the
    /// Windows Application event log. Logging never throws: a failure in any sink is swallowed so
    /// it cannot take down the long-running logger.
    /// </summary>
    /// <remarks>
    /// The log directory defaults to <c>%LOCALAPPDATA%\co2-level-exporter\logs</c> and can be
    /// overridden with <c>CO2_LOG_DIRECTORY</c>. Files older than <c>CO2_LOG_RETENTION_DAYS</c>
    /// (default 30) are deleted at startup. Event log entries are only written once the source has
    /// been registered (deploy/install-scheduled-task.ps1 does this when run elevated).
    /// </remarks>
    public sealed class Logger
    {
        public const string EventLogSourceName = "co2-level-exporter";

        private const string FilePrefix = "co2-level-exporter-";
        private const int DefaultRetentionDays = 30;

        private readonly object _sync = new();
        private readonly string _directory;
        private readonly EventLog? _eventLog;

        public Logger()
        {
            _directory = ResolveDirectory();
            TryCreateDirectory(_directory);
            TryDeleteOldFiles(_directory);
            _eventLog = TryOpenEventLog();
        }

        /// <summary>The directory log files are written to.</summary>
        public string Directory => _directory;

        /// <summary>True when warnings and errors are also written to the Application event log.</summary>
        public bool EventLogEnabled => _eventLog is not null;

        public void Info(string message, bool toEventLog = false) => Write(LogLevel.Info, message, toEventLog);

        public void Warning(string message) => Write(LogLevel.Warning, message, toEventLog: true);

        public void Error(string message) => Write(LogLevel.Error, message, toEventLog: true);

        private void Write(LogLevel level, string message, bool toEventLog)
        {
            var now = DateTime.UtcNow;
            var line = $"{now:yyyy-MM-ddTHH:mm:ss.fffZ} [{level.ToString().ToUpperInvariant()}] {message}";

            lock (_sync)
            {
                if (level == LogLevel.Error)
                    Console.Error.WriteLine(line);
                else
                    Console.WriteLine(line);

                try
                {
                    var path = Path.Combine(_directory, $"{FilePrefix}{now:yyyyMMdd}.log");
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch
                {
                    // A logging failure must never crash the logger.
                }

                if (toEventLog && _eventLog is not null)
                {
                    try
                    {
                        var entryType = level switch
                        {
                            LogLevel.Error => EventLogEntryType.Error,
                            LogLevel.Warning => EventLogEntryType.Warning,
                            _ => EventLogEntryType.Information
                        };
                        _eventLog.WriteEntry(message, entryType);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string ResolveDirectory()
        {
            var configured = Environment.GetEnvironmentVariable("CO2_LOG_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "co2-level-exporter", "logs");
        }

        private static void TryCreateDirectory(string directory)
        {
            try
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            catch
            {
            }
        }

        private static void TryDeleteOldFiles(string directory)
        {
            var retentionDays = ResolveRetentionDays();
            if (retentionDays <= 0)
                return;

            try
            {
                var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
                foreach (var file in System.IO.Directory.EnumerateFiles(directory, $"{FilePrefix}*.log"))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static int ResolveRetentionDays()
        {
            var raw = Environment.GetEnvironmentVariable("CO2_LOG_RETENTION_DAYS");
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var days) && days >= 0)
                return days;
            return DefaultRetentionDays;
        }

        private static EventLog? TryOpenEventLog()
        {
            try
            {
                if (!EventLog.SourceExists(EventLogSourceName))
                    return null;

                return new EventLog("Application") { Source = EventLogSourceName };
            }
            catch
            {
                return null;
            }
        }
    }
}
