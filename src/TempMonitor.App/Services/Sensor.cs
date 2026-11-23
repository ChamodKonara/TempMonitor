using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class Sensor
{
    public SensorConfig Config { get; }
    public List<double> History { get; } = new();   // this sensor's valid readings only
    public object Gate { get; } = new();
    internal CancellationTokenSource? Cts { get; set; }
    internal Task? Loop { get; set; }
    public bool IsRunning => Cts != null;

    public Sensor(SensorConfig config) => Config = config;
}