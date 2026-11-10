using System.Globalization;
using Microsoft.Data.Sqlite;
using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class ReadingRepository
{
    private readonly string _connectionString;
    private readonly object _lock = new();   // sensors run on separate threads

    public ReadingRepository(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureCreated();
    }

    private void EnsureCreated()
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Readings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SensorId TEXT NOT NULL,
                    SensorName TEXT NOT NULL,
                    Location TEXT NOT NULL,
                    Temperature REAL NOT NULL,
                    Timestamp TEXT NOT NULL,
                    ThresholdStatus TEXT NOT NULL,
                    AnomalyStatus TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Readings_SensorId ON Readings (SensorId);";
            cmd.ExecuteNonQuery();
        }
    }

    public void StoreData(SensorReading r)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Readings
                    (SensorId, SensorName, Location, Temperature, Timestamp, ThresholdStatus, AnomalyStatus)
                VALUES (@id, @name, @loc, @temp, @ts, @status, @anomaly);";
            cmd.Parameters.AddWithValue("@id", r.SensorId);
            cmd.Parameters.AddWithValue("@name", r.SensorName);
            cmd.Parameters.AddWithValue("@loc", r.Location);
            cmd.Parameters.AddWithValue("@temp", r.Temperature);
            cmd.Parameters.AddWithValue("@ts", r.Timestamp.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@status", r.Status.ToString());
            cmd.Parameters.AddWithValue("@anomaly", r.Anomaly.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    // sensorId = null returns every sensor's readings
    public List<SensorReading> RetrieveData(string? sensorId = null)
    {
        lock (_lock)
        {
            var list = new List<SensorReading>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT SensorId, SensorName, Location, Temperature, Timestamp, ThresholdStatus, AnomalyStatus
                FROM Readings"
                + (sensorId == null ? "" : " WHERE SensorId = @id")
                + " ORDER BY Id;";
            if (sensorId != null) cmd.Parameters.AddWithValue("@id", sensorId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SensorReading
                {
                    SensorId = reader.GetString(0),
                    SensorName = reader.GetString(1),
                    Location = reader.GetString(2),
                    Temperature = reader.GetDouble(3),
                    Timestamp = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture,
                                               DateTimeStyles.RoundtripKind),
                    Status = Enum.Parse<ThresholdStatus>(reader.GetString(5)),
                    Anomaly = Enum.Parse<AnomalyStatus>(reader.GetString(6))
                });
            }
            return list;
        }
    }
}