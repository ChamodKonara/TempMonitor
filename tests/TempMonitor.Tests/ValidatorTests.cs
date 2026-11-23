using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class ValidatorTests
{
    private static SensorConfig Config() => new()
    {
        SensorId = "S1",
        Name = "Rack 1",
        Location = "Room A",
        MinTemp = 10,
        MaxTemp = 50,
        WarningThreshold = 27,
        CriticalThreshold = 32
    };

    private static SensorReading Reading(double temp) => new()
    {
        SensorId = "S1",
        SensorName = "Rack 1",
        Location = "Room A",
        Temperature = temp,
        Timestamp = DateTime.UtcNow
    };

    private readonly ReadingValidator _rv = new();
    private readonly ConfigValidator _cv = new();

    [Theory]
    [InlineData(10)]   // exactly the minimum
    [InlineData(50)]   // exactly the maximum
    [InlineData(23)]
    public void Reading_WithinLimits_IsValid(double t) =>
        Assert.True(_rv.Validate(Reading(t), Config()).IsValid);

    [Theory]
    [InlineData(9.99)]
    [InlineData(50.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Reading_OutsideLimits_IsRejected(double t) =>
        Assert.False(_rv.Validate(Reading(t), Config()).IsValid);

    [Fact]
    public void Reading_MissingFields_ReportsEachError()
    {
        var r = new SensorReading { Temperature = 23 };
        var result = _rv.Validate(r, Config());
        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count); // id, name, location, timestamp
    }

    [Fact]
    public void Config_Valid_Passes() =>
        Assert.True(_cv.Validate(Config()).IsValid);

    [Fact]
    public void Config_MissingId_Fails()
    {
        var c = Config(); c.SensorId = "";
        Assert.False(_cv.Validate(c).IsValid);
    }

    [Fact]
    public void Config_WarningAboveCritical_Fails()
    {
        var c = Config(); c.WarningThreshold = 35;
        Assert.False(_cv.Validate(c).IsValid);
    }

    [Fact]
    public void Config_MinAboveMax_Fails()
    {
        var c = Config(); c.MinTemp = 60;
        Assert.False(_cv.Validate(c).IsValid);
    }
}