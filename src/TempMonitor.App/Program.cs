using TempMonitor.App.Services;

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var logger = new FileLogger(Path.Combine(dataDir, "monitor.log"));
var repo = new ReadingRepository(Path.Combine(dataDir, "readings.db"));
var store = new ConfigStore(Path.Combine(dataDir, "sensors.json"));
var manager = new SensorManager(repo, logger);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    manager.ExitApplication();
    Environment.Exit(0);
};

manager.InitialiseSensor(store.Load());
new MenuRunner(manager, store, Console.In, Console.Out).Run();