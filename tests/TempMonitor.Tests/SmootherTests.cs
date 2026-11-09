using TempMonitor.App.Services;

public class SmootherTests
{
    private readonly Smoother _s = new();

    [Fact]
    public void NoReadings_ReturnsNull() =>
        Assert.Null(_s.Smooth(new List<double>()));

    [Fact]
    public void OneReading_ReturnsThatReading() =>
        Assert.Equal(23.0, _s.Smooth(new List<double> { 23.0 }));

    [Fact]
    public void FewerThanFive_AveragesAll() =>
        Assert.Equal(24.0, _s.Smooth(new List<double> { 22, 24, 26 }));

    [Fact]
    public void ExactlyFive_AveragesAll() =>
        Assert.Equal(24.0, _s.Smooth(new List<double> { 22, 23, 24, 25, 26 }));

    [Fact]
    public void MoreThanFive_UsesLatestFiveOnly() =>
        // the old 100 must be ignored
        Assert.Equal(24.0, _s.Smooth(new List<double> { 100, 22, 23, 24, 25, 26 }));
}