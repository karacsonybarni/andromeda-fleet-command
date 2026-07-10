using System.Text;

namespace AndromedaFleetCommand.Game.Infrastructure;

public sealed class CrashReportService : IDisposable
{
    private readonly string _directory;
    private bool _disposed;

    public CrashReportService(string directory)
    {
        _directory = directory;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public string? LastReportPath { get; private set; }

    public string WriteReport(Exception error, string context)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log");
        var report = new StringBuilder()
            .AppendLine("Andromeda Fleet Command crash report")
            .AppendLine($"UTC: {DateTime.UtcNow:O}")
            .AppendLine($"Context: {context}")
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($"Runtime: {Environment.Version}")
            .AppendLine()
            .AppendLine(error.ToString())
            .ToString();
        File.WriteAllText(path, report);
        LastReportPath = path;
        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception error) WriteReport(error, "Unhandled exception");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        WriteReport(args.Exception, "Unobserved task exception");
        args.SetObserved();
    }
}
