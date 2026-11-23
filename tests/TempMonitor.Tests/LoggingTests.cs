using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class FakeLogger : ISensorLogger
{
    public List<string> Entries { get; } = new();
    public void Log(string level, string message) => Entries.Add($"{level}|{message}");
    public void Dispose() { }
}

public class LoggingTests
{
    private static SensorReading Reading(ThresholdStatus s = ThresholdStatus.Normal,
                                         AnomalyStatus a = AnomalyStatus.Normal) => new()
                                         {
                                             SensorId = "S1",
                                             Location = "Room A",
                                             Temperature = 23,
                                             Status = s,
                                             Anomaly = a
                                         };

    [Fact]
    public void Accepted_IsLogged()
    {
        var fake = new FakeLogger();
        new ReadingLogger(fake).LogAccepted(Reading());
        Assert.Contains("ACCEPTED S1", fake.Entries.Single());
    }

    [Fact]
    public void Rejected_LogsAllErrors()
    {
        var fake = new FakeLogger();
        new ReadingLogger(fake).LogRejected(Reading(), new[] { "bad temp", "no id" });
        Assert.Contains("bad temp; no id", fake.Entries.Single());
    }

    [Fact]
    public void Warning_IsLoggedAsWarning()
    {
        var fake = new FakeLogger();
        new ReadingLogger(fake).LogAlert(Reading(ThresholdStatus.Warning));
        Assert.StartsWith("WARNING|", fake.Entries.Single());
    }

    [Fact]
    public void Critical_IsLoggedAsCritical()
    {
        var fake = new FakeLogger();
        new ReadingLogger(fake).LogAlert(Reading(ThresholdStatus.Critical));
        Assert.StartsWith("CRITICAL|", fake.Entries.Single());
    }

    [Fact]
    public void Normal_LogsNoAlert()
    {
        var fake = new FakeLogger();
        new ReadingLogger(fake).LogAlert(Reading());
        Assert.Empty(fake.Entries);
    }

    [Fact]
    public void Anomaly_IsLogged()
    {
        var fake = new FakeLogger();
        new ReadingLogger(fake).LogAnomaly(Reading(a: AnomalyStatus.Anomaly));
        Assert.Contains("Anomaly", fake.Entries.Single());
    }

    [Fact]
    public void FileLogger_WritesToFileAndClosesCleanly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"log_{Guid.NewGuid()}.txt");
        var logger = new FileLogger(path);
        logger.Log("INFO", "hello");
        logger.Dispose();
        logger.Dispose();                 // disposing twice must not crash
        logger.Log("INFO", "ignored");    // logging after close must not crash

        var text = File.ReadAllText(path); // works only if the file was released
        Assert.Contains("hello", text);
        Assert.DoesNotContain("ignored", text);
        File.Delete(path);
    }
}