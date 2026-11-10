using Microsoft.Data.Sqlite;
using TempMonitor.App.Models;
using TempMonitor.App.Services;

public class RepositoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tm_{Guid.NewGuid()}");
    private string DbPath => Path.Combine(_folder, "readings.db");

    private static SensorReading Reading(string id, double temp,
        ThresholdStatus s = ThresholdStatus.Normal, AnomalyStatus a = AnomalyStatus.Normal) => new()
        {
            SensorId = id,
            SensorName = $"Name-{id}",
            Location = "Room A",
            Temperature = temp,
            Timestamp = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc),
            Status = s,
            Anomaly = a
        };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();   // release the file so it can be deleted
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Constructor_CreatesDatabaseAndFolderAutomatically()
    {
        _ = new ReadingRepository(DbPath);
        Assert.True(File.Exists(DbPath));
    }

    [Fact]
    public void NewDatabase_RetrievesNothing() =>
        Assert.Empty(new ReadingRepository(DbPath).RetrieveData());

    [Fact]
    public void Store_ThenRetrieve_KeepsEveryField()
    {
        var repo = new ReadingRepository(DbPath);
        var original = Reading("S1", 33.5, ThresholdStatus.Critical, AnomalyStatus.Anomaly);
        repo.StoreData(original);

        var loaded = Assert.Single(repo.RetrieveData("S1"));
        Assert.Equal("S1", loaded.SensorId);
        Assert.Equal("Name-S1", loaded.SensorName);
        Assert.Equal("Room A", loaded.Location);
        Assert.Equal(33.5, loaded.Temperature);
        Assert.Equal(original.Timestamp, loaded.Timestamp);
        Assert.Equal(ThresholdStatus.Critical, loaded.Status);
        Assert.Equal(AnomalyStatus.Anomaly, loaded.Anomaly);
    }

    [Fact]
    public void RetrieveOneSensor_ReturnsOnlyThatSensor()
    {
        var repo = new ReadingRepository(DbPath);
        repo.StoreData(Reading("S1", 23));
        repo.StoreData(Reading("S2", 24));
        repo.StoreData(Reading("S1", 25));

        var s1 = repo.RetrieveData("S1");
        Assert.Equal(2, s1.Count);
        Assert.All(s1, r => Assert.Equal("S1", r.SensorId));
    }

    [Fact]
    public void RetrieveAll_ReturnsEverySensorInOrder()
    {
        var repo = new ReadingRepository(DbPath);
        repo.StoreData(Reading("S1", 23));
        repo.StoreData(Reading("S2", 24));

        var all = repo.RetrieveData();
        Assert.Equal(new[] { "S1", "S2" }, all.Select(r => r.SensorId));
    }

    [Fact]
    public void Data_SurvivesApplicationRestart()
    {
        new ReadingRepository(DbPath).StoreData(Reading("S1", 23));

        var afterRestart = new ReadingRepository(DbPath);   // a brand-new instance, same file
        Assert.Single(afterRestart.RetrieveData("S1"));
    }

    [Fact]
    public void SensorWithNoRegistration_StillHasItsHistory()
    {
        var repo = new ReadingRepository(DbPath);
        repo.StoreData(Reading("DELETED-1", 23));
        Assert.Single(repo.RetrieveData("DELETED-1"));
    }

    [Fact]
    public void RetrieveUnknownSensor_ReturnsEmptyList() =>
        Assert.Empty(new ReadingRepository(DbPath).RetrieveData("NOPE"));
}