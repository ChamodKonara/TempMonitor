using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class SensorManager
{
    private readonly Dictionary<string, Sensor> _sensors = new();
    private readonly object _lock = new();
    private readonly ReadingRepository _repo;
    private readonly ISensorLogger _logger;
    private readonly ReadingLogger _readingLog;
    private readonly TimeSpan _interval;

    private readonly ConfigValidator _configValidator = new();
    private readonly ReadingValidator _validator = new();
    private readonly ThresholdChecker _threshold = new();
    private readonly AnomalyDetector _anomaly = new();
    private readonly Smoother _smoother = new();

    public SensorManager(ReadingRepository repo, ISensorLogger logger, TimeSpan? interval = null)
    {
        _repo = repo;
        _logger = logger;
        _readingLog = new ReadingLogger(logger);
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public IReadOnlyList<string> SensorIds
    {
        get { lock (_lock) return _sensors.Keys.ToList(); }
    }

    private Sensor? Find(string? id)
    {
        if (id == null) return null;
        lock (_lock) return _sensors.TryGetValue(id, out var s) ? s : null;
    }

    public bool IsRunning(string id) => Find(id)?.IsRunning ?? false;

    public IReadOnlyList<double> GetHistory(string id)
    {
        var s = Find(id);
        if (s == null) return Array.Empty<double>();
        lock (s.Gate) return s.History.ToList();
    }

    // 1. InitialiseSensor: invalid sensors are logged and skipped, valid ones still load
    public int InitialiseSensor(IEnumerable<SensorConfig> configs)
    {
        int loaded = 0;
        foreach (var c in configs)
        {
            if (c == null) { _logger.Log("ERROR", "Skipped a null sensor configuration."); continue; }

            var result = _configValidator.Validate(c);
            if (!result.IsValid)
            {
                _logger.Log("ERROR", $"Sensor '{c.SensorId}' not loaded: {string.Join("; ", result.Errors)}");
                continue;
            }

            lock (_lock)
            {
                if (_sensors.ContainsKey(c.SensorId))
                {
                    _logger.Log("ERROR", $"Sensor '{c.SensorId}' not loaded: duplicate ID.");
                    continue;
                }
                _sensors[c.SensorId] = new Sensor(c);
            }
            loaded++;
        }
        return loaded;
    }

    // 2. StartSensor: returns false if unknown or already running (no duplicate loops)
    public bool StartSensor(string id)
    {
        var s = Find(id);
        if (s == null) return false;

        lock (s.Gate)
        {
            if (s.IsRunning) return false;
            s.Cts = new CancellationTokenSource();
            var token = s.Cts.Token;
            s.Loop = Task.Run(() => SimulateData(id, token));
        }
        _logger.Log("INFO", $"Sensor {id} started.");
        return true;
    }

    // 3. StartAllSensors: returns how many were started
    public int StartAllSensors() => SensorIds.Count(StartSensor);

    // 4. StopSensor: keeps data and history, allows restart. Returns false if not running.
    public bool StopSensor(string id)
    {
        var s = Find(id);
        if (s == null) return false;

        CancellationTokenSource? cts;
        Task? loop;
        lock (s.Gate)
        {
            cts = s.Cts; loop = s.Loop;
            s.Cts = null; s.Loop = null;
        }
        if (cts == null) return false;

        cts.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        cts.Dispose();
        _logger.Log("INFO", $"Sensor {id} stopped.");
        return true;
    }

    // 5. StopAllSensors: returns how many were stopped
    public int StopAllSensors() => SensorIds.Count(StopSensor);

    // 6. SimulateData: repeated readings around 22-24C while the sensor runs
    public async Task SimulateData(string sensorId, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    double temp = Math.Round(23 + (Random.Shared.NextDouble() * 2 - 1), 1);
                    ProcessReading(sensorId, temp);
                }
                catch (Exception ex)
                {
                    _logger.Log("ERROR", $"Sensor {sensorId} failed to process a reading: {ex.Message}");
                }
                await Task.Delay(_interval, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    // The pipeline: ValidateData -> CheckThreshold -> DetectAnomaly -> LogData -> StoreData.
    // Also used by the menu to inject controlled abnormal readings.
    // Returns the accepted reading, or null if rejected or the sensor is unknown.
    public SensorReading? ProcessReading(string sensorId, double temperature)
    {
        var s = Find(sensorId);
        if (s == null) return null;

        var reading = new SensorReading
        {
            SensorId = s.Config.SensorId,
            SensorName = s.Config.Name,
            Location = s.Config.Location,
            Temperature = temperature,
            Timestamp = DateTime.UtcNow
        };

        lock (s.Gate)
        {
            var check = _validator.Validate(reading, s.Config);
            if (!check.IsValid)
            {
                _readingLog.LogRejected(reading, check.Errors);
                return null;
            }

            reading.Status = _threshold.Check(temperature, s.Config);
            reading.Anomaly = _anomaly.Detect(temperature, s.History);

            _readingLog.LogAccepted(reading);
            _readingLog.LogAlert(reading);
            _readingLog.LogAnomaly(reading);

            _repo.StoreData(reading);
            s.History.Add(temperature);
            return reading;
        }
    }

    // 10. SmoothData: average of this sensor's latest five valid readings (null if none)
    public double? SmoothData(string sensorId)
    {
        var s = Find(sensorId);
        if (s == null) return null;
        lock (s.Gate) return _smoother.Smooth(s.History);
    }

    // 13. RetrieveData: one sensor, or all sensors when id is null (includes deleted sensors)
    public List<SensorReading> RetrieveData(string? sensorId = null) => _repo.RetrieveData(sensorId);

    // 14. ResetSensor: stop, clear in-memory history, keep config and stored readings
    public bool ResetSensor(string id)
    {
        var s = Find(id);
        if (s == null) return false;

        StopSensor(id);
        lock (s.Gate) s.History.Clear();
        _logger.Log("INFO", $"Sensor {id} reset.");
        return true;
    }

    // Reset every registered sensor. Returns how many were reset.
    public int ResetAllSensors() => SensorIds.Count(ResetSensor);

    // 15. DeleteSensor: stop and unregister, keep stored readings
    public bool DeleteSensor(string id)
    {
        var s = Find(id);
        if (s == null) return false;

        StopSensor(id);
        lock (_lock) _sensors.Remove(id);
        _logger.Log("INFO", $"Sensor {id} deleted.");
        return true;
    }

    public SensorConfig? GetConfig(string id) => Find(id)?.Config;

    public List<SensorConfig> GetConfigs()
    {
        lock (_lock) return _sensors.Values.Select(s => s.Config).ToList();
    }

    // Changes one sensor's thresholds only if the new values are valid
    public bool UpdateThresholds(string id, double warning, double critical, out List<string> errors)
    {
        errors = new List<string>();
        var s = Find(id);
        if (s == null) { errors.Add("Unknown sensor."); return false; }

        lock (s.Gate)
        {
            var candidate = new SensorConfig
            {
                SensorId = s.Config.SensorId,
                Name = s.Config.Name,
                Location = s.Config.Location,
                MinTemp = s.Config.MinTemp,
                MaxTemp = s.Config.MaxTemp,
                WarningThreshold = warning,
                CriticalThreshold = critical
            };
            var result = _configValidator.Validate(candidate);
            if (!result.IsValid) { errors.AddRange(result.Errors); return false; }

            s.Config.WarningThreshold = warning;
            s.Config.CriticalThreshold = critical;
        }
        _logger.Log("INFO", $"Sensor {id} thresholds changed to warning {warning}, critical {critical}.");
        return true;
    }

    // 16. ExitApplication: stop everything and close the shared log. Safe to call twice.
    public void ExitApplication()
    {
        StopAllSensors();
        _logger.Log("INFO", "Application shutting down.");
        _logger.Dispose();
    }
}