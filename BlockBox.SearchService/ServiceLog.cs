namespace BlockBox.SearchService;

/// <summary>
/// Minimal append-only service log. The service runs without a console, so without this a
/// crashed or stuck index leaves nothing to diagnose.
/// </summary>
internal static class ServiceLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlockBox", "Search", "service.log");

    internal static bool EchoToConsole { get; set; }

    internal static void Write(string message) => WriteCore(message, null);

    internal static void Write(string message, Exception exception) => WriteCore(message, exception);

    private static void WriteCore(string message, Exception? exception)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}";
        if (exception is not null) line += Environment.NewLine + exception;
        System.Diagnostics.Trace.WriteLine(line);
        if (EchoToConsole) Console.Error.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxBytes) File.Move(LogPath, LogPath + ".1", overwrite: true);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the service down.
        }
    }
}
