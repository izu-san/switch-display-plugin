using System.IO;

namespace SwitchDisplayPlugin;

internal static class PluginLog
{
    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FlowLauncher",
                "Logs",
                "SwitchDisplayPlugin");

            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "plugin.log");
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}";

            lock (Lock)
            {
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Logging must never break plugin execution.
        }
    }
}
