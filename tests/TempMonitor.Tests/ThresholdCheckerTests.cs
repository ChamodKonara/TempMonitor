using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class ThresholdCheckerTests
{
    private readonly ThresholdChecker _t = new();
    private static SensorConfig Config() => new() { WarningThreshold = 27, CriticalThreshold = 32 };

    [Theory]
    [InlineData(23, ThresholdStatus.Normal)]
    [InlineData(26.99, ThresholdStatus.Normal)]
    [InlineData(27, ThresholdStatus.Warning)]    // exactly at warning
    [InlineData(31.99, ThresholdStatus.Warning)]
    [InlineData(32, ThresholdStatus.Critical)]   // exactly at critical
    [InlineData(45, ThresholdStatus.Critical)]
    public void Check_ReturnsExpectedStatus(double temp, ThresholdStatus expected) =>
        Assert.Equal(expected, _t.Check(temp, Config()));

    [Fact]
    public void Check_UsesEachSensorsOwnThresholds()
    {
        var strict = new SensorConfig { WarningThreshold = 24, CriticalThreshold = 26 };
        Assert.Equal(ThresholdStatus.Critical, _t.Check(27, strict));
        Assert.Equal(ThresholdStatus.Warning, _t.Check(27, Config()));
    }
}