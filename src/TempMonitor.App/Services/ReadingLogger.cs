using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class ReadingLogger
{
    private readonly ISensorLogger _log;
    public ReadingLogger(ISensorLogger log) => _log = log;

    public void LogAccepted(SensorReading r) =>
        _log.Log("INFO", $"ACCEPTED {r.SensorId} {r.Location} {r.Temperature:F1}C");

    public void LogRejected(SensorReading r, IEnumerable<string> errors) =>
        _log.Log("ERROR", $"REJECTED {r.SensorId}: {string.Join("; ", errors)}");

    public void LogAlert(SensorReading r)
    {
        if (r.Status == ThresholdStatus.Normal) return;
        var level = r.Status == ThresholdStatus.Critical ? "CRITICAL" : "WARNING";
        _log.Log(level, $"ALERT {r.SensorId} {r.Temperature:F1}C is {r.Status}");
    }

    public void LogAnomaly(SensorReading r) =>
        _log.Log(r.Anomaly == AnomalyStatus.Anomaly ? "WARNING" : "INFO",
                 $"ANOMALY {r.SensorId}: {r.Anomaly}");
}