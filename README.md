# TempMonitor - Data Centre Temperature Monitor

D202 Individual Development Project. A C# (.NET 9) console application that simulates several
data-centre temperature sensors, validates their readings, raises threshold alerts, detects sudden
temperature changes, smooths readings and stores accepted readings in SQLite.

![CI](https://github.com/ChamodKonara/TempMonitor/actions/workflows/ci.yml/badge.svg)

## 1. Project overview

Each sensor runs independently and produces a reading about once a second (around 22-24 C with
small random variation). Every reading goes through the same pipeline:

1. Validate (required fields, sensor temperature limits). Invalid readings are logged and rejected.
2. Check thresholds (Normal, Warning or Critical) using that sensor's own thresholds.
3. Detect anomalies against that sensor's previous five valid readings.
4. Log the outcome (accepted, alert, anomaly).
5. Store the accepted reading and its statuses in SQLite and add it to the sensor's in-memory history.

A numeric console menu lets the user start and stop sensors, view and analyse readings, change
thresholds, reset or delete sensors and generate controlled abnormal readings for testing.

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download) (tested with 9.0.x)
- Git
- Optional, for coverage reports: `dotnet tool install -g dotnet-reportgenerator-globaltool`

NuGet packages are restored automatically: `Microsoft.Data.Sqlite` (app), `xunit` and
`coverlet.collector` (tests).

## 2. Installation, configuration, build and run

```bat
git clone https://github.com/ChamodKonara/TempMonitor.git
cd TempMonitor
dotnet build
dotnet run --project src/TempMonitor.App
```

### Configuration

Sensors are configured in `sensors.json`. On the first run the file is created with three default
sensors (S1, S2, S3). It is stored in a `data` folder next to the compiled program
(`src/TempMonitor.App/bin/Debug/net9.0/data/`). Each sensor has:

| Field | Meaning |
|---|---|
| `SensorId`, `Name`, `Location` | Required identity fields |
| `MinTemp`, `MaxTemp` | Valid temperature limits. Readings outside these are rejected |
| `WarningThreshold`, `CriticalThreshold` | Alert thresholds. Warning must be below Critical and both must sit inside the limits |

Invalid or duplicate sensor configurations are logged and skipped. They never stop the valid
sensors from loading. If the file is corrupt, the application starts with no sensors and reports
that in the menu.

### Menu

```
 1. Start a sensor            6. Analyse temperatures (one or all)
 2. Start all sensors         7. Configure alert thresholds (saved)
 3. Stop a sensor             8. Reset a sensor (one or all)
 4. Stop all sensors          9. Delete a sensor (with confirmation)
 5. View stored readings     10. Generate abnormal reading (testing)
 0. Exit
```

Invalid menu input (letters, out-of-range numbers, empty lines, end of input) is handled with a
message and never crashes the program. Option 10 injects a controlled reading at the sensor's
warning threshold, at its critical threshold, as a sudden 3.5 C jump (anomaly), or outside the
valid limits (rejected).

## 3. Application structure

```
src/TempMonitor.App/
  Program.cs                  Start-up wiring only
  Models/                     SensorConfig, SensorReading, ThresholdStatus, AnomalyStatus
  Services/
    SensorManager.cs          Sensor lifecycle and the reading pipeline
    Sensor.cs                 One sensor: config, own history, own running loop
    ConfigValidator.cs        Sensor configuration rules
    ReadingValidator.cs       Reading rules and temperature limits
    ThresholdChecker.cs       Normal / Warning / Critical
    AnomalyDetector.cs        Sudden-change detection
    Smoother.cs               Average of the latest five valid readings
    ReadingRepository.cs      SQLite storage and retrieval
    ReadingLogger.cs          What gets logged (accepted, rejected, alerts, anomalies)
    FileLogger.cs             Thread-safe log file writer
    ISensorLogger.cs          Logging interface (lets tests inspect log output)
    ConfigStore.cs            Load and save sensors.json
    MenuRunner.cs             The numeric console menu
tests/TempMonitor.Tests/      xUnit test project
evidence/                     Code coverage report and screenshot
.github/workflows/ci.yml      GitHub Actions build and test
```

The menu is deliberately thin. All behaviour lives in the services, which keeps it easy to test.

### Required methods and where they live

Equivalent names are used where noted.

| # | Required method | Implementation |
|---|---|---|
| 1 | InitialiseSensor | `SensorManager.InitialiseSensor` (uses `ConfigValidator`) |
| 2 | StartSensor | `SensorManager.StartSensor` |
| 3 | StartAllSensors | `SensorManager.StartAllSensors` |
| 4 | StopSensor | `SensorManager.StopSensor` |
| 5 | StopAllSensors | `SensorManager.StopAllSensors` |
| 6 | SimulateData | `SensorManager.SimulateData` (per-sensor loop) |
| 7 | ValidateData | `ReadingValidator.Validate` |
| 8 | CheckThreshold | `ThresholdChecker.Check` |
| 9 | DetectAnomaly | `AnomalyDetector.Detect` |
| 10 | SmoothData | `SensorManager.SmoothData` (uses `Smoother.Smooth`) |
| 11 | LogData | `ReadingLogger` (`LogAccepted`, `LogRejected`, `LogAlert`, `LogAnomaly`) |
| 12 | StoreData | `ReadingRepository.StoreData` |
| 13 | RetrieveData | `ReadingRepository.RetrieveData`, exposed by `SensorManager.RetrieveData` |
| 14 | ResetSensor | `SensorManager.ResetSensor` (and `ResetAllSensors`) |
| 15 | DeleteSensor | `SensorManager.DeleteSensor` |
| 16 | ExitApplication | `SensorManager.ExitApplication` |

## 4. Main features and temperature-processing rules

| Rule | Behaviour |
|---|---|
| Simulation | About 22-24 C with small random variation, one reading per second per running sensor |
| Validation | SensorId, name, location and timestamp are required. Temperature must be a real number within the sensor's `MinTemp` to `MaxTemp` (inclusive). Invalid readings are logged and never stored |
| Thresholds | Below warning: **Normal**. At or above warning: **Warning**. At or above critical: **Critical** |
| Anomaly | The latest valid reading is compared with the previous five valid readings from the same sensor. A difference of **3 C or more** from any of them is an **Anomaly**. With no history the result is **NotEvaluated** |
| Smoothing | Average of the latest five valid readings, or all available if fewer. No data gives no value |
| Independence | Each sensor has its own configuration, history and running loop. Stopping, resetting or deleting one never affects another |
| Stop | Keeps stored data and in-memory history, and the sensor can be restarted |
| Reset | Stops the sensor and clears its in-memory history. Configuration and stored readings are kept |
| Delete | Stops and removes the sensor (after confirmation). Its stored readings remain retrievable |

### Assumptions (the specification leaves these open)

- A reading exactly equal to a threshold counts as that level (`>=`).
- "Compared with the previous five valid readings" is implemented as: anomaly if the difference is 3 C or more from **any** of the previous five.
- Stopping a sensor keeps its in-memory history so smoothing and anomaly detection continue after restart. Only Reset clears it.
- Deleting a sensor removes it from `sensors.json`, so it does not come back on the next start. Its stored readings are kept.
- Changed thresholds are validated first (warning below critical, inside the limits) and then saved.

## 5. SQLite database and logging

**Database:** `readings.db` in the `data` folder, created automatically on first run. Table `Readings`:

| Column | Notes |
|---|---|
| Id | Auto-increment primary key |
| SensorId, SensorName, Location | Identity of the sensor at the time of the reading |
| Temperature | Real |
| Timestamp | UTC, ISO 8601 text |
| ThresholdStatus | Normal, Warning or Critical |
| AnomalyStatus | NotEvaluated, Normal or Anomaly |

Only accepted readings are stored. Nothing in the application deletes rows, so data survives
restarts, resets and sensor deletion. Each operation uses its own connection, guarded by a lock
because sensors run on separate threads.

**Logging:** `monitor.log` in the same folder. Format: `yyyy-MM-dd HH:mm:ss [LEVEL] message`.
Levels: INFO (accepted readings, start/stop/reset/delete), WARNING (warning alerts, anomalies),
CRITICAL (critical alerts), ERROR (rejected readings and invalid configuration). The logger is
closed when the application exits and is safe to dispose twice.

## 6. Testing

```bat
dotnet test
```

The separate xUnit project (`tests/TempMonitor.Tests`) contains **99** automated tests covering:

| Area | Test file |
|---|---|
| Configuration and reading validation, temperature boundaries | `ValidatorTests.cs` |
| Warning and critical thresholds, per-sensor thresholds | `ThresholdCheckerTests.cs` |
| Anomaly detection and insufficient history | `AnomalyDetectorTests.cs` |
| Smoothing with 0, 1, 3, 5 and more than 5 readings | `SmootherTests.cs` |
| SQLite storage, retrieval, restart persistence, deleted-sensor history | `RepositoryTests.cs` |
| Logging and clean file-logger shutdown | `LoggingTests.cs` |
| Start, stop, restart, reset, delete, independence, pipeline, controlled abnormal readings, exit | `SensorManagerTests.cs` |
| Menu behaviour, invalid input, thresholds, delete confirmation, analyse/reset all | `MenuTests.cs` |
| Sensor configuration file | `ConfigStoreTests.cs` |

Tests use temporary databases and folders, so they never touch real data.

### Code coverage

Line coverage: **96.9%** (requirement: at least 70%). Branch coverage: **90.2%**.

```bat
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"evidence/coverage" -reporttypes:"Html;TextSummary"
```

Evidence is in the [`evidence`](evidence) folder: `coverage-report.png` (screenshot),
`coverage/Summary.txt` and the full HTML report in `coverage/index.html`. `Program.cs` (start-up
wiring only) is the one file not covered by tests.

### Continuous integration

`.github/workflows/ci.yml` runs on every push. It builds the solution in Release mode and runs all
tests with coverage collection.

## 7. Development process

- **Branching:** `main` for stable code, `develop` for integration, and one `feature/*` branch per task, merged into `develop` by pull request after CI passes.
- **Kanban board:** [GitHub Projects board](https://github.com/users/ChamodKonara/projects/3) with To do, In progress, Testing/Review and Done columns. Every task is a GitHub issue with acceptance criteria, closed by the pull request that completes it.
- **Release:** the finished application is tagged `v1.0`.

## 8. Known limitations

- Console interface only, as the assignment specifies. There is no graphical interface.
- Data files are stored next to the compiled program, so a clean build folder starts with fresh default sensors and an empty database.
- Sensors share one SQLite database file and a lock, which is fine for a few sensors at one reading per second but would not scale to thousands.
- The log file grows without limit (no rotation).
- A corrupt `sensors.json` loads as an empty sensor list and is overwritten the next time settings are saved.
- Readings are simulated. There is no connection to real hardware.
