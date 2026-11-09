namespace TempMonitor.App.Models;

public class SensorReading
{
    public string SensorId { get; set; } = "";
    public string SensorName { get; set; } = "";
    public string Location { get; set; } = "";
    public double Temperature { get; set; }
    public DateTime Timestamp { get; set; }
    public ThresholdStatus Status { get; set; }
    public AnomalyStatus Anomaly { get; set; }
}