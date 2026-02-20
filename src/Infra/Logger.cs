namespace VoiceAssistant;

public static class Logger
{
    private static readonly string LogPath = ProjectPaths.Log;
    private static readonly object _lock = new();

    public static void Log(string level, string title, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level,-5}] {title}: {message}";
        lock (_lock)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }
}
