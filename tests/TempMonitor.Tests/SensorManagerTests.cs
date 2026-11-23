using Microsoft.Data.Sqlite;
using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class SpyLogger : ISensorLogger
{
    private readonly object _l = new();
    private readonly List<string> _entries = new();
    public bool Disposed { get; private set; }
    public List<string> Entries { get { lock (_l) return _entries.ToList(); } }
    public void Log(string level, string message) { lock (_l) _entries.Add($"{level}|{message}"); }
    public void Dispose() => Disposed = true;
}

public class SensorManagerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tm_{Guid.NewGuid()}");
    private readonly ReadingRepository _repo;
    private readonly SpyLogger _log = new();
    private readonly SensorManager _mgr;

    public SensorManagerTests()
    {
        _repo = new ReadingRepository(Path.Combine(_folder, "readings.db"));
        _mgr = new SensorManager(_repo, _log, TimeSpan.FromMilliseconds(10));
    }

    public void Dispose()
    {
        _mgr.ExitApplication();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static SensorConfig Cfg(string id) => new()
    {
        SensorId = id,
        Name = $"Name-{id}",
        Location = "Room A",
        MinTemp = 10,
        MaxTemp = 50,
        WarningThreshold = 27,
        CriticalThreshold = 32
    };

    private void Load(params string[] ids) => _mgr.InitialiseSensor(ids.Select(Cfg));
    private int Count(string id) => _repo.RetrieveData(id).Count;

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    // ---- configuration ----

    [Fact]
    public void Initialise_InvalidSensorDoesNotBlockValidOnes()
    {
        var bad = Cfg("BAD"); bad.MinTemp = 60;
        var loaded = _mgr.InitialiseSensor(new[] { Cfg("S1"), bad, Cfg("S2") });

        Assert.Equal(2, loaded);
        Assert.Equal(new[] { "S1", "S2" }, _mgr.SensorIds.OrderBy(x => x));
        Assert.Contains(_log.Entries, e => e.Contains("BAD"));
    }

    [Fact]
    public void Initialise_DuplicateIdIsRejected()
    {
        Assert.Equal(1, _mgr.InitialiseSensor(new[] { Cfg("S1"), Cfg("S1") }));
        Assert.Single(_mgr.SensorIds);
    }

    // ---- start and stop ----

    [Fact]
    public void StartSensor_GeneratesAndStoresReadingsInRange()
    {
        Load("S1");
        Assert.True(_mgr.StartSensor("S1"));
        Assert.True(WaitFor(() => Count("S1") >= 3));
        _mgr.StopSensor("S1");

        Assert.All(_repo.RetrieveData("S1"), r => Assert.InRange(r.Temperature, 22.0, 24.0));
    }

    [Fact]
    public void StartSensor_TwiceDoesNotCreateSecondLoop()
    {
        Load("S1");
        Assert.True(_mgr.StartSensor("S1"));
        Assert.False(_mgr.StartSensor("S1"));
        Assert.True(_mgr.IsRunning("S1"));
    }

    [Fact]
    public void StartSensor_UnknownId_ReturnsFalse() =>
        Assert.False(_mgr.StartSensor("NOPE"));

    [Fact]
    public void StartAll_StartsEverySensor()
    {
        Load("S1", "S2", "S3");
        Assert.Equal(3, _mgr.StartAllSensors());
        Assert.All(new[] { "S1", "S2", "S3" }, id => Assert.True(_mgr.IsRunning(id)));
    }

    [Fact]
    public void StopSensor_NotRunning_ReturnsFalse()
    {
        Load("S1");
        Assert.False(_mgr.StopSensor("S1"));
    }

    [Fact]
    public void StopSensor_DoesNotAffectOtherSensors()
    {
        Load("S1", "S2");
        _mgr.StartAllSensors();
        Assert.True(WaitFor(() => Count("S1") >= 2 && Count("S2") >= 2));

        Assert.True(_mgr.StopSensor("S1"));
        int s1 = Count("S1"), s2 = Count("S2");

        Assert.False(_mgr.IsRunning("S1"));
        Assert.True(_mgr.IsRunning("S2"));
        Assert.True(WaitFor(() => Count("S2") > s2));   // S2 keeps producing
        Assert.Equal(s1, Count("S1"));                  // S1 stopped, data kept
    }

    [Fact]
    public void StoppedSensor_CanBeRestarted()
    {
        Load("S1");
        _mgr.StartSensor("S1");
        Assert.True(WaitFor(() => Count("S1") >= 1));
        _mgr.StopSensor("S1");
        int before = Count("S1");

        Assert.True(_mgr.StartSensor("S1"));
        Assert.True(WaitFor(() => Count("S1") > before));
    }

    [Fact]
    public void StopAll_StopsEverythingAndKeepsData()
    {
        Load("S1", "S2");
        _mgr.StartAllSensors();
        Assert.True(WaitFor(() => Count("S1") >= 1 && Count("S2") >= 1));

        Assert.Equal(2, _mgr.StopAllSensors());
        Assert.False(_mgr.IsRunning("S1"));
        Assert.False(_mgr.IsRunning("S2"));
        Assert.NotEmpty(_repo.RetrieveData());
    }

    // ---- reset and delete ----

    [Fact]
    public void Reset_ClearsHistoryButKeepsConfigAndStoredReadings()
    {
        Load("S1");
        _mgr.StartSensor("S1");
        Assert.True(WaitFor(() => Count("S1") >= 2));

        Assert.True(_mgr.ResetSensor("S1"));
        Assert.False(_mgr.IsRunning("S1"));
        Assert.Empty(_mgr.GetHistory("S1"));
        Assert.Contains("S1", _mgr.SensorIds);
        Assert.True(Count("S1") >= 2);
    }

    [Fact]
    public void Reset_DoesNotAffectOtherSensorHistory()
    {
        Load("S1", "S2");
        _mgr.ProcessReading("S1", 23);
        _mgr.ProcessReading("S2", 24);

        _mgr.ResetSensor("S1");

        Assert.Empty(_mgr.GetHistory("S1"));
        Assert.Single(_mgr.GetHistory("S2"));
    }

    [Fact]
    public void Delete_RemovesSensorButKeepsItsStoredReadings()
    {
        Load("S1", "S2");
        _mgr.ProcessReading("S1", 23);
        _mgr.ProcessReading("S2", 24);
        _mgr.StartSensor("S1");

        Assert.True(_mgr.DeleteSensor("S1"));

        Assert.DoesNotContain("S1", _mgr.SensorIds);
        Assert.False(_mgr.IsRunning("S1"));
        Assert.NotEmpty(_mgr.RetrieveData("S1"));            // history survives deletion
        Assert.Contains("S2", _mgr.SensorIds);               // other sensor untouched
        Assert.Single(_mgr.GetHistory("S2"));
        Assert.Null(_mgr.ProcessReading("S1", 23));          // deleted sensor takes no readings
        Assert.False(_mgr.DeleteSensor("S1"));
    }

    // ---- reading pipeline and controlled abnormal readings ----

    [Fact]
    public void ValidReading_IsStoredWithStatuses()
    {
        Load("S1");
        var r = _mgr.ProcessReading("S1", 23);

        Assert.NotNull(r);
        Assert.Equal(ThresholdStatus.Normal, r!.Status);
        Assert.Equal(AnomalyStatus.NotEvaluated, r.Anomaly);

        var stored = Assert.Single(_repo.RetrieveData("S1"));
        Assert.Equal("Name-S1", stored.SensorName);
        Assert.Equal("Room A", stored.Location);
    }

    [Theory]
    [InlineData(9.9)]
    [InlineData(50.1)]
    public void OutOfRangeReading_IsRejectedAndNotStored(double temp)
    {
        Load("S1");

        Assert.Null(_mgr.ProcessReading("S1", temp));
        Assert.Empty(_repo.RetrieveData("S1"));
        Assert.Empty(_mgr.GetHistory("S1"));
        Assert.Contains(_log.Entries, e => e.StartsWith("ERROR|REJECTED S1"));
    }

    [Fact]
    public void WarningAndCriticalReadings_AreFlaggedAndLogged()
    {
        Load("S1");

        Assert.Equal(ThresholdStatus.Warning, _mgr.ProcessReading("S1", 28)!.Status);
        Assert.Equal(ThresholdStatus.Critical, _mgr.ProcessReading("S1", 33)!.Status);
        Assert.Contains(_log.Entries, e => e.StartsWith("WARNING|ALERT"));
        Assert.Contains(_log.Entries, e => e.StartsWith("CRITICAL|ALERT"));
    }

    [Fact]
    public void AbnormalJump_IsFlaggedAsAnomaly()
    {
        Load("S1");
        _mgr.ProcessReading("S1", 23);
        _mgr.ProcessReading("S1", 23.2);

        var r = _mgr.ProcessReading("S1", 26.5);   // 3.5 above, but below the warning threshold

        Assert.Equal(AnomalyStatus.Anomaly, r!.Anomaly);
        Assert.Equal(ThresholdStatus.Normal, r.Status);
    }

    [Fact]
    public void AnomalyDetection_UsesOnlyTheSensorsOwnHistory()
    {
        Load("S1", "S2");
        _mgr.ProcessReading("S1", 23);

        var first = _mgr.ProcessReading("S2", 30);   // S2's first reading: nothing to compare

        Assert.Equal(AnomalyStatus.NotEvaluated, first!.Anomaly);
    }

    [Fact]
    public void SmoothData_AveragesOnlyThisSensorsReadings()
    {
        Load("S1", "S2");
        _mgr.ProcessReading("S1", 22);
        _mgr.ProcessReading("S1", 24);
        _mgr.ProcessReading("S2", 30);

        Assert.Equal(23.0, _mgr.SmoothData("S1"));
        Assert.Null(_mgr.SmoothData("NOPE"));
    }

    [Fact]
    public void RetrieveData_WorksForOneSensorOrAll()
    {
        Load("S1", "S2");
        _mgr.ProcessReading("S1", 23);
        _mgr.ProcessReading("S2", 24);

        Assert.Single(_mgr.RetrieveData("S1"));
        Assert.Equal(2, _mgr.RetrieveData().Count);
    }

    // ---- shutdown ----

    [Fact]
    public void ExitApplication_StopsAllSensorsAndClosesLogger()
    {
        Load("S1", "S2");
        _mgr.StartAllSensors();

        _mgr.ExitApplication();

        Assert.False(_mgr.IsRunning("S1"));
        Assert.False(_mgr.IsRunning("S2"));
        Assert.True(_log.Disposed);
    }

    [Fact]
    public void ExitApplication_CalledTwice_DoesNotCrash()
    {
        Load("S1");
        _mgr.ExitApplication();
        _mgr.ExitApplication();
    }
}