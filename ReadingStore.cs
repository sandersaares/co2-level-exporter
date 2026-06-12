using System.Data;
using Microsoft.Data.SqlClient;

namespace co2_level_exporter
{
    /// <summary>
    /// Persists CO2 readings to Azure SQL, retrying automatically on transient errors.
    /// </summary>
    public sealed class ReadingStore
    {
        private const string InsertSql =
            "INSERT INTO co2.readings (reading_time, volts, ppm) VALUES (@reading_time, @volts, @ppm);";

        private readonly string _connectionString;
        private readonly SqlRetryLogicBaseProvider _retryProvider;

        public ReadingStore(string connectionString)
        {
            _connectionString = connectionString;

            var options = new SqlRetryLogicOption
            {
                NumberOfTries = 5,
                DeltaTime = TimeSpan.FromSeconds(2),
                MaxTimeInterval = TimeSpan.FromSeconds(30)
            };
            _retryProvider = SqlConfigurableRetryFactory.CreateExponentialRetryProvider(options);
        }

        public async Task SaveAsync(Co2Reading reading, CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(_connectionString)
            {
                RetryLogicProvider = _retryProvider
            };
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(InsertSql, connection)
            {
                RetryLogicProvider = _retryProvider
            };
            command.Parameters.Add("@reading_time", SqlDbType.DateTime2).Value = reading.Timestamp;

            var voltsParameter = command.Parameters.Add("@volts", SqlDbType.Decimal);
            voltsParameter.Precision = 6;
            voltsParameter.Scale = 3;
            voltsParameter.Value = (decimal)reading.Volts;

            command.Parameters.Add("@ppm", SqlDbType.Int).Value = reading.Ppm;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
