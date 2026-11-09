using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class ConfigValidator
{
    public ValidationResult Validate(SensorConfig c)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(c.SensorId)) result.Errors.Add("SensorId is required.");
        if (string.IsNullOrWhiteSpace(c.Name)) result.Errors.Add("Name is required.");
        if (string.IsNullOrWhiteSpace(c.Location)) result.Errors.Add("Location is required.");
        if (c.MinTemp >= c.MaxTemp) result.Errors.Add("MinTemp must be less than MaxTemp.");
        if (c.WarningThreshold >= c.CriticalThreshold)
            result.Errors.Add("WarningThreshold must be less than CriticalThreshold.");
        if (c.WarningThreshold < c.MinTemp || c.CriticalThreshold > c.MaxTemp)
            result.Errors.Add("Thresholds must sit within MinTemp and MaxTemp.");

        return result;
    }
}