using System.Globalization;
using TempMonitor.App.Models;

namespace TempMonitor.App.Services;

public class MenuRunner
{
    private readonly SensorManager _mgr;
    private readonly ConfigStore _store;
    private readonly TextReader _in;
    private readonly TextWriter _out;

    public MenuRunner(SensorManager mgr, ConfigStore store, TextReader input, TextWriter output)
    {
        _mgr = mgr; _store = store; _in = input; _out = output;
    }

    public void Run()
    {
        while (true)
        {
            ShowMenu();
            var line = _in.ReadLine();
            if (line == null) break;   // input closed

            if (!int.TryParse(line.Trim(), out var choice) || choice < 0 || choice > 10)
            {
                _out.WriteLine("Please enter a number from the menu.");
                continue;
            }
            if (choice == 0) break;

            try { Handle(choice); }
            catch (Exception ex) { _out.WriteLine($"Something went wrong: {ex.Message}"); }
        }
        _mgr.ExitApplication();
        _out.WriteLine("All sensors stopped. Goodbye.");
    }

    private void ShowMenu()
    {
        _out.WriteLine();
        _out.WriteLine("=== Data Centre Temperature Monitor ===");
        _out.WriteLine(" 1. Start a sensor");
        _out.WriteLine(" 2. Start all sensors");
        _out.WriteLine(" 3. Stop a sensor");
        _out.WriteLine(" 4. Stop all sensors");
        _out.WriteLine(" 5. View stored readings");
        _out.WriteLine(" 6. Analyse temperatures");
        _out.WriteLine(" 7. Configure alert thresholds");
        _out.WriteLine(" 8. Reset a sensor");
        _out.WriteLine(" 9. Delete a sensor");
        _out.WriteLine("10. Generate abnormal reading (testing)");
        _out.WriteLine(" 0. Exit");
        _out.Write("Choose an option: ");
    }

    private void Handle(int choice)
    {
        switch (choice)
        {
            case 1: WithSensor(id => _out.WriteLine(_mgr.StartSensor(id) ? $"Sensor {id} started." : $"Sensor {id} is already running.")); break;
            case 2: _out.WriteLine($"Started {_mgr.StartAllSensors()} sensor(s)."); break;
            case 3: WithSensor(id => _out.WriteLine(_mgr.StopSensor(id) ? $"Sensor {id} stopped." : $"Sensor {id} is not running.")); break;
            case 4: _out.WriteLine($"Stopped {_mgr.StopAllSensors()} sensor(s)."); break;
            case 5: ViewReadings(); break;
            case 6: WithSensor(Analyse); break;
            case 7: WithSensor(ConfigureThresholds); break;
            case 8: WithSensor(id => { _mgr.ResetSensor(id); _out.WriteLine($"Sensor {id} reset. Stored readings kept."); }); break;
            case 9: WithSensor(DeleteSensor); break;
            case 10: WithSensor(GenerateAbnormal); break;
        }
    }

    private void WithSensor(Action<string> action)
    {
        var id = PickSensor();
        if (id != null) action(id);
    }

    // Returns the sensor ID, "" for "all sensors" (only when allowAll), or null if cancelled/invalid
    private string? PickSensor(bool allowAll = false)
    {
        var ids = _mgr.SensorIds.OrderBy(x => x).ToList();
        if (ids.Count == 0 && !allowAll)
        {
            _out.WriteLine("No sensors loaded.");
            return null;
        }
        for (int i = 0; i < ids.Count; i++)
        {
            var c = _mgr.GetConfig(ids[i]);
            if (c == null) continue;
            _out.WriteLine($"  {i + 1}. {c.SensorId} - {c.Name} ({c.Location}) {(_mgr.IsRunning(ids[i]) ? "[running]" : "[stopped]")}");
        }
        if (allowAll) _out.WriteLine("  0. All sensors (includes deleted ones)");
        _out.Write("Choose a sensor: ");

        var line = _in.ReadLine()?.Trim();
        if (allowAll && line == "0") return "";
        if (int.TryParse(line, out var n) && n >= 1 && n <= ids.Count) return ids[n - 1];

        _out.WriteLine("Invalid choice.");
        return null;
    }

    private double? ReadNumber(string prompt)
    {
        _out.Write(prompt);
        var line = _in.ReadLine()?.Trim();
        if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
            return v;
        _out.WriteLine("That is not a valid number.");
        return null;
    }

    private void ViewReadings()
    {
        var id = PickSensor(allowAll: true);
        if (id == null) return;

        var rows = _mgr.RetrieveData(id == "" ? null : id);
        if (rows.Count == 0) { _out.WriteLine("No stored readings."); return; }

        foreach (var r in rows.TakeLast(10))
            _out.WriteLine($"{r.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss} {r.SensorId} {r.Location} {r.Temperature:F1}C {r.Status} {r.Anomaly}");
        _out.WriteLine($"Showing last {Math.Min(10, rows.Count)} of {rows.Count}.");
    }

    private void Analyse(string id)
    {
        var rows = _mgr.RetrieveData(id);
        if (rows.Count == 0) { _out.WriteLine($"No readings for {id} yet."); return; }

        var temps = rows.Select(r => r.Temperature).ToList();
        _out.WriteLine($"Readings: {rows.Count}");
        _out.WriteLine($"Min {temps.Min():F1}C  Max {temps.Max():F1}C  Average {temps.Average():F1}C");

        var smooth = _mgr.SmoothData(id);
        if (smooth.HasValue) _out.WriteLine($"Smoothed (latest 5): {smooth.Value:F1}C");

        _out.WriteLine($"Warnings: {rows.Count(r => r.Status == ThresholdStatus.Warning)}  " +
                       $"Critical: {rows.Count(r => r.Status == ThresholdStatus.Critical)}  " +
                       $"Anomalies: {rows.Count(r => r.Anomaly == AnomalyStatus.Anomaly)}");
    }

    private void ConfigureThresholds(string id)
    {
        var c = _mgr.GetConfig(id);
        if (c == null) return;
        _out.WriteLine($"Current: warning {c.WarningThreshold}C, critical {c.CriticalThreshold}C (valid range {c.MinTemp} to {c.MaxTemp}C)");

        var warning = ReadNumber("New warning threshold: ");
        if (warning == null) return;
        var critical = ReadNumber("New critical threshold: ");
        if (critical == null) return;

        if (_mgr.UpdateThresholds(id, warning.Value, critical.Value, out var errors))
        {
            _store.Save(_mgr.GetConfigs());
            _out.WriteLine("Thresholds updated and saved.");
        }
        else
        {
            _out.WriteLine("Thresholds not changed: " + string.Join("; ", errors));
        }
    }

    private void DeleteSensor(string id)
    {
        _out.Write($"Delete {id}? Stored readings are kept. Type Y to confirm: ");
        var answer = _in.ReadLine()?.Trim();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
        {
            _out.WriteLine("Delete cancelled.");
            return;
        }
        _mgr.DeleteSensor(id);
        _store.Save(_mgr.GetConfigs());
        _out.WriteLine($"Sensor {id} deleted. Its stored readings are kept.");
    }

    private void GenerateAbnormal(string id)
    {
        var c = _mgr.GetConfig(id);
        if (c == null) return;

        _out.WriteLine("  1. Warning level   2. Critical level   3. Sudden jump (anomaly)   4. Out of range (rejected)");
        _out.Write("Choose type: ");
        if (!int.TryParse(_in.ReadLine()?.Trim(), out var type) || type < 1 || type > 4)
        {
            _out.WriteLine("Invalid choice.");
            return;
        }

        double temp;
        switch (type)
        {
            case 1: temp = c.WarningThreshold; break;
            case 2: temp = c.CriticalThreshold; break;
            case 3:
                {
                    var last = _mgr.GetHistory(id).LastOrDefault(23.0);
                    temp = Math.Round(last + 3.5 > c.MaxTemp ? last - 3.5 : last + 3.5, 1);
                    break;
                }
            default: temp = c.MaxTemp + 5; break;
        }

        var r = _mgr.ProcessReading(id, temp);
        if (r == null) _out.WriteLine($"Reading {temp:F1}C was rejected (see log).");
        else _out.WriteLine($"Reading {r.Temperature:F1}C accepted: {r.Status}, anomaly: {r.Anomaly}.");
    }
}