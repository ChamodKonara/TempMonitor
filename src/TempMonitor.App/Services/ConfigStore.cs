using System.Text.Json;
using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public ConfigStore(string path) => _path = path;

    // Creates the file with default sensors on first run. A corrupt file loads as empty.
    public List<SensorConfig> Load()
    {
        if (!File.Exists(_path))
        {
            var defaults = Defaults();
            Save(defaults);
            return defaults;
        }
        try
        {
            return JsonSerializer.Deserialize<List<SensorConfig>>(File.ReadAllText(_path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save(IEnumerable<SensorConfig> configs)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(configs, Options));
    }

    public static List<SensorConfig> Defaults() => new()
    {
        new() { SensorId = "S1", Name = "Rack A1", Location = "Hall A", MinTemp = 10, MaxTemp = 50, WarningThreshold = 27, CriticalThreshold = 32 },
        new() { SensorId = "S2", Name = "Rack A2", Location = "Hall A", MinTemp = 10, MaxTemp = 50, WarningThreshold = 27, CriticalThreshold = 32 },
        new() { SensorId = "S3", Name = "Rack B1", Location = "Hall B", MinTemp = 10, MaxTemp = 50, WarningThreshold = 27, CriticalThreshold = 32 },
    };
}