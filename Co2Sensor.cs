using Prometheus;
using System.Diagnostics;

namespace co2_level_exporter
{
    /// <summary>
    /// Connects to the input where the CO2 sensor is connected and reports the data receievd.
    /// </summary>
    public static class Co2Sensor
    {
        /// <summary>
        /// Entry point - starts observing the sensor and keeps doing it forever.
        /// </summary>
        public static void StartObserving()
        {
            new Thread((ThreadStart)ObserverThread)
            {
                IsBackground = true,
                Name = "Sensor observer thread"
            }.Start();
        }

        private static void ObserverThread()
        {
            try
            {
                var device = new USBM();

                if (!device.OpenDevice())
                    throw new Exception("Could not open device.");

                while (true)
                {
                    var volts = device.GetMeasuredValue();

                    if (volts == 0.0f)
                        throw new Exception("Value read from device was zero. This indicates the device may not be working. Restarting to try to recover.");

                    var ppmPerVolt = 197.5f; // Experimentally determined - looks perfectly linear.
                    var ppm = (int)(volts * ppmPerVolt);

                    Measurements.Inc();
                    Volts.Set(volts);
                    Ppm.Set(ppm);

                    Console.WriteLine($"{volts:#00.000} V\t\t{ppm} PPM");

                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                // Oh no! This is fatal error.
                Console.WriteLine(ex.ToString());
                Console.WriteLine("Will restart after sleeping for a while.");
                Thread.Sleep(TimeSpan.FromSeconds(30));
                Process.GetCurrentProcess().Kill();
            }
        }

        private static readonly Gauge Volts = Metrics.CreateGauge("co2_sensor_reading_volts", "Voltage level of the CO2 sensor.");
        private static readonly Gauge Ppm = Metrics.CreateGauge("co2_sensor_reading_ppm", "PPM level of the CO2 sensor (converted from voltage).");
        private static readonly Counter Measurements = Metrics.CreateCounter("co_sensor_measurements_total", "Count of measurements.");
    }
}
