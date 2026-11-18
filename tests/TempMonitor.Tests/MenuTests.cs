using Microsoft.Data.Sqlite;
using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class MenuTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tm_{Guid.NewGuid()}");
    private readonly ReadingRepository _repo;
    private readonly SpyLogger _log = new();
    private readonly SensorManager _mgr;
    private readonly ConfigStore _store;

    public MenuTests()
    {
        _repo = new ReadingRepository(Path.Combine(_folder, "readings.db"));
        _mgr = new SensorManager(_repo, _log, TimeSpan.FromMilliseconds(10));
        _store = new ConfigStore(Path.Combine(_folder, "sensors.json"));
        _mgr.InitialiseSensor(ConfigStore.Defaults());
    }

    public void Dispose()
    {
        _mgr.ExitApplication();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private string Run(string input)
    {
        var output = new StringWriter();
        new MenuRunner(_mgr, _store, new StringReader(input), output).Run();
        return output.ToString();
    }

    // ---- input handling and exit ----

    [Fact]
    public void InvalidMenuInput_DoesNotCrash() =>
        Assert.Contains("Please enter a number", Run("abc\n99\n-1\n\n0\n"));

    [Fact]
    public void Exit_StopsSensorsAndClosesLogger()
    {
        Run("2\n0\n");
        Assert.False(_mgr.IsRunning("S1"));
        Assert.True(_log.Disposed);
    }

    [Fact]
    public void EndOfInput_ExitsCleanly()
    {
        Run("");
        Assert.True(_log.Disposed);
    }

    [Fact]
    public void NoSensorsLoaded_IsReported()
    {
        var empty = new SensorManager(_repo, _log);
        var output = new StringWriter();
        new MenuRunner(empty, _store, new StringReader("1\n0\n"), output).Run();
        Assert.Contains("No sensors loaded.", output.ToString());
    }

    [Theory]
    [InlineData("1\n9\n0\n")]
    [InlineData("1\nabc\n0\n")]
    public void InvalidSensorChoice_IsRejected(string input) =>
        Assert.Contains("Invalid choice.", Run(input));

    // ---- start and stop ----

    [Fact]
    public void StartAndStopAll_ReportCounts()
    {
        var output = Run("2\n4\n0\n");
        Assert.Contains("Started 3 sensor(s).", output);
        Assert.Contains("Stopped 3 sensor(s).", output);
    }

    [Fact]
    public void StartSensorTwice_ReportsAlreadyRunning() =>
        Assert.Contains("already running", Run("1\n1\n1\n1\n0\n"));

    [Fact]
    public void StopSensor_ReportsStoppedOrNotRunning()
    {
        Assert.Contains("is not running", Run("3\n1\n0\n"));
        Assert.Contains("Sensor S1 stopped.", Run("1\n1\n3\n1\n0\n"));
    }

    // ---- controlled abnormal readings ----

    [Fact]
    public void AbnormalWarning_IsAcceptedAndStored()
    {
        Assert.Contains("accepted: Warning", Run("10\n1\n1\n0\n"));
        Assert.Equal(ThresholdStatus.Warning, Assert.Single(_repo.RetrieveData("S1")).Status);
    }

    [Fact]
    public void AbnormalCritical_IsAccepted() =>
        Assert.Contains("accepted: Critical", Run("10\n1\n2\n0\n"));

    [Fact]
    public void AbnormalJump_IsFlaggedAsAnomaly()
    {
        _mgr.ProcessReading("S1", 23);
        Assert.Contains("anomaly: Anomaly", Run("10\n1\n3\n0\n"));
    }

    [Fact]
    public void AbnormalOutOfRange_IsRejectedAndNotStored()
    {
        Assert.Contains("rejected", Run("10\n1\n4\n0\n"));
        Assert.Empty(_repo.RetrieveData("S1"));
    }

    [Fact]
    public void InvalidAbnormalType_IsRejected() =>
        Assert.Contains("Invalid choice.", Run("10\n1\n7\n0\n"));

    // ---- thresholds ----

    [Fact]
    public void ConfigureThresholds_Valid_IsAppliedAndSaved()
    {
        Assert.Contains("updated", Run("7\n1\n28\n33\n0\n"));

        var saved = _store.Load().First(c => c.SensorId == "S1");
        Assert.Equal(28, saved.WarningThreshold);
        Assert.Equal(33, saved.CriticalThreshold);
        Assert.Equal(28, _mgr.GetConfig("S1")!.WarningThreshold);
    }

    [Fact]
    public void ConfigureThresholds_Invalid_LeavesSensorUnchanged()
    {
        Assert.Contains("not changed", Run("7\n1\n40\n30\n0\n"));
        Assert.Equal(27, _mgr.GetConfig("S1")!.WarningThreshold);
    }

    [Fact]
    public void ConfigureThresholds_NotANumber_IsRejected() =>
        Assert.Contains("not a valid number", Run("7\n1\nabc\n0\n"));

    [Fact]
    public void ConfigureThresholds_ChangeAppliesToNextReading()
    {
        Run("7\n1\n20\n25\n0\n");
        Assert.Equal(ThresholdStatus.Warning, _mgr.ProcessReading("S1", 22)!.Status);
    }

    [Fact]
    public void UpdateThresholds_UnknownSensor_Fails()
    {
        Assert.False(_mgr.UpdateThresholds("NOPE", 20, 30, out var errors));
        Assert.NotEmpty(errors);
        Assert.Null(_mgr.GetConfig("NOPE"));
    }

    // ---- reset and delete ----

    [Fact]
    public void Reset_ClearsHistoryButKeepsStoredReadings()
    {
        _mgr.ProcessReading("S1", 23);
        Run("8\n1\n0\n");
        Assert.Empty(_mgr.GetHistory("S1"));
        Assert.Single(_repo.RetrieveData("S1"));
    }

    [Fact]
    public void Delete_Cancelled_KeepsSensor()
    {
        Assert.Contains("cancelled", Run("9\n1\nn\n0\n"));
        Assert.Equal(3, _mgr.SensorIds.Count);
    }

    [Fact]
    public void Delete_Confirmed_RemovesSensorButKeepsReadings()
    {
        _mgr.ProcessReading("S1", 23);
        Run("9\n1\ny\n0\n");

        Assert.DoesNotContain("S1", _mgr.SensorIds);
        Assert.NotEmpty(_repo.RetrieveData("S1"));
        Assert.Equal(2, _store.Load().Count);
    }

    // ---- viewing and analysis ----

    [Fact]
    public void ViewReadings_NoData_SaysSo() =>
        Assert.Contains("No stored readings.", Run("5\n0\n0\n"));

    [Fact]
    public void ViewReadings_AllSensors_ShowsEverything()
    {
        _mgr.ProcessReading("S1", 23);
        _mgr.ProcessReading("S2", 24);
        Assert.Contains("Showing last 2 of 2.", Run("5\n0\n0\n"));
    }

    [Fact]
    public void ViewReadings_OneSensor_ShowsOnlyThatSensor()
    {
        _mgr.ProcessReading("S1", 23);
        _mgr.ProcessReading("S2", 24);
        Assert.Contains("Showing last 1 of 1.", Run("5\n1\n0\n"));
    }

    [Fact]
    public void Analyse_NoData_SaysSo() =>
        Assert.Contains("No readings for S1 yet.", Run("6\n1\n0\n"));

    [Fact]
    public void Analyse_WithData_ShowsStatistics()
    {
        _mgr.ProcessReading("S1", 22);
        _mgr.ProcessReading("S1", 24);

        var output = Run("6\n1\n0\n");
        Assert.Contains("Average 23.0C", output);
        Assert.Contains("Smoothed (latest 5): 23.0C", output);
    }
}