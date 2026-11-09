namespace TempMonitor.App.Services;

public interface ISensorLogger : IDisposable
{
    void Log(string level, string message);
}