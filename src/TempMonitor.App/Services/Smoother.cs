namespace TempMonitor.App.Services;

public class Smoother
{
    // history = this sensor's valid readings, oldest first. Returns null when there is no data.
    public double? Smooth(IReadOnlyList<double> history)
    {
        if (history.Count == 0) return null;
        return history.TakeLast(5).Average();
    }
}