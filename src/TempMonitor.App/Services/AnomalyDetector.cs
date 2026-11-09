using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class AnomalyDetector
{
    public const double Limit = 3.0;

    // history = previous valid readings for THIS sensor, oldest first
    public AnomalyStatus Detect(double latest, IReadOnlyList<double> history)
    {
        if (history.Count == 0) return AnomalyStatus.NotEvaluated;

        var previous = history.TakeLast(5);
        bool anomaly = previous.Any(p => Math.Abs(latest - p) >= Limit);
        return anomaly ? AnomalyStatus.Anomaly : AnomalyStatus.Normal;
    }
}