namespace co2_level_exporter
{
    /// <summary>
    /// Reads CO2 measurements from the USB sensor device.
    /// </summary>
    public sealed class Co2Sensor
    {
        // Experimentally determined conversion factor; the response looks linear.
        private const float PpmPerVolt = 197.5f;

        private readonly USBM _device = new();
        private bool _isOpen;

        /// <summary>
        /// Opens the sensor device. Throws if the device cannot be opened.
        /// </summary>
        public void Open()
        {
            if (!_device.OpenDevice())
                throw new Exception("Could not open the CO2 sensor device.");

            _isOpen = true;
        }

        /// <summary>
        /// Reads the current measurement. Throws if the device is not open or returns a zero
        /// value, which indicates the device may not be working.
        /// </summary>
        public Co2Reading Read()
        {
            if (!_isOpen)
                throw new InvalidOperationException("The sensor device has not been opened.");

            var volts = _device.GetMeasuredValue();
            if (volts == 0.0f)
                throw new Exception("Value read from device was zero. This indicates the device may not be working.");

            var ppm = (int)(volts * PpmPerVolt);
            return new Co2Reading(DateTime.UtcNow, volts, ppm);
        }
    }

    /// <summary>
    /// A single CO2 sensor measurement.
    /// </summary>
    public readonly record struct Co2Reading(DateTime Timestamp, float Volts, int Ppm);
}
