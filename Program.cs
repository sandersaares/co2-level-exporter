using Prometheus;

namespace co2_level_exporter
{
    // dotnet run --urls=http://localhost:2295/
    public static class Program
    {
        public static void Main(string[] args)
        {
            Co2Sensor.StartObserving();

            var builder = WebApplication.CreateBuilder(args);

            var app = builder.Build();

            app.MapMetrics();

            app.Run();
        }
    }
}
