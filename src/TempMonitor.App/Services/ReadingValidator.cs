using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class ReadingValidator
{
    public ValidationResult Validate(SensorReading r, SensorConfig config)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(r.SensorId)) result.Errors.Add("SensorId is required.");
        if (string.IsNullOrWhiteSpace(r.SensorName)) result.Errors.Add("SensorName is required.");
        if (string.IsNullOrWhiteSpace(r.Location)) result.Errors.Add("Location is required.");
        if (r.Timestamp == default) result.Errors.Add("Timestamp is required.");

        if (double.IsNaN(r.Temperature) || double.IsInfinity(r.Temperature))
            result.Errors.Add("Temperature is not a valid number.");
        else if (r.Temperature < config.MinTemp || r.Temperature > config.MaxTemp)
            result.Errors.Add($"Temperature {r.Temperature} is outside limits {config.MinTemp} to {config.MaxTemp}.");

        return result;
    }
}