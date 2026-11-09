using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class ThresholdChecker
{
    public ThresholdStatus Check(double temperature, SensorConfig config)
    {
        if (temperature >= config.CriticalThreshold) return ThresholdStatus.Critical;
        if (temperature >= config.WarningThreshold) return ThresholdStatus.Warning;
        return ThresholdStatus.Normal;
    }
}