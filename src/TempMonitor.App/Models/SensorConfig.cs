namespace TempMonitor.App.Models;

public class SensorConfig
{
    public string SensorId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public double MinTemp { get; set; }
    public double MaxTemp { get; set; }
    public double WarningThreshold { get; set; }
    public double CriticalThreshold { get; set; }
}