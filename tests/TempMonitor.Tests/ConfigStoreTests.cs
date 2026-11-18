using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class ConfigStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tm_{Guid.NewGuid()}");
    private string FilePath => Path.Combine(_folder, "sensors.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Load_MissingFile_CreatesDefaultSensors()
    {
        var list = new ConfigStore(FilePath).Load();
        Assert.Equal(3, list.Count);
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new ConfigStore(FilePath);
        store.Save(new[] { new SensorConfig
        {
            SensorId = "X1", Name = "N", Location = "L",
            MinTemp = 5, MaxTemp = 45, WarningThreshold = 25, CriticalThreshold = 30
        }});

        var c = Assert.Single(store.Load());
        Assert.Equal("X1", c.SensorId);
        Assert.Equal(25, c.WarningThreshold);
        Assert.Equal(45, c.MaxTemp);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyList()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Empty(new ConfigStore(FilePath).Load());
    }

    [Fact]
    public void Defaults_AreAllValid()
    {
        var validator = new ConfigValidator();
        Assert.All(ConfigStore.Defaults(), c => Assert.True(validator.Validate(c).IsValid));
    }
}