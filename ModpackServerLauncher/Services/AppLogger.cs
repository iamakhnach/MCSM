namespace ModpackServerLauncher.Services;

public static class AppLogger
{
    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModpackServerLauncher",
        "Logs",
        "app.log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(Exception exception, string? context = null)
    {
        Write("ERROR", string.IsNullOrWhiteSpace(context)
            ? exception.ToString()
            : context + Environment.NewLine + exception);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {level}: {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the launcher.
        }
    }
}
