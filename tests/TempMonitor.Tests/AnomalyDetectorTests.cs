using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class AnomalyDetectorTests
{
    private readonly AnomalyDetector _d = new();

    [Fact]
    public void NoHistory_ReturnsNotEvaluated() =>
        Assert.Equal(AnomalyStatus.NotEvaluated, _d.Detect(23, new List<double>()));

    [Fact]
    public void DifferenceOfExactly3_IsAnomaly() =>
        Assert.Equal(AnomalyStatus.Anomaly, _d.Detect(26, new List<double> { 23 }));

    [Fact]
    public void SmallDifference_IsNormal() =>
        Assert.Equal(AnomalyStatus.Normal, _d.Detect(23.5, new List<double> { 23, 23.2 }));

    [Fact]
    public void OnlyLastFiveAreUsed() =>
        Assert.Equal(AnomalyStatus.Normal,
            _d.Detect(23, new List<double> { 30, 23, 23, 23, 23, 23 }));
}