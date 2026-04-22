using System.Diagnostics;
using System.IO;

namespace SwitchDisplayPlugin;

internal static class StreamingSetup
{
    private const int TargetWidth = 2560;
    private const int TargetHeight = 1440;
    private const string InfinitasUrl = "https://p.eagate.573.jp/game/infinitas/2/api/login/login.html";

    public static bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return false;
        }

        return search.Contains("stream", StringComparison.OrdinalIgnoreCase)
            || search.Contains("infinitas", StringComparison.OrdinalIgnoreCase)
            || search.Contains("iidx", StringComparison.OrdinalIgnoreCase)
            || search.Contains("配信", StringComparison.OrdinalIgnoreCase)
            || search.Contains("音ゲー", StringComparison.OrdinalIgnoreCase);
    }

    public static DisplayInfo? FindTargetDisplay(IEnumerable<DisplayInfo> displays)
    {
        return displays
            .Where(display => display.Width == TargetWidth && display.Height == TargetHeight)
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DisplayNumber)
            .FirstOrDefault();
    }

    public static string Run()
    {
        var steps = new List<string>();

        StartApplication("OBS Studio", FindShortcut("OBS Studio.lnk") ?? @"C:\Program Files\obs-studio\bin\64bit\obs64.exe");
        steps.Add("OBS Studio");

        Thread.Sleep(1000);

        var oneComme = FindShortcut("わんコメ - OneComme.lnk")
            ?? FindShortcut("OneComme.lnk")
            ?? FindExecutableInLocalPrograms("OneComme", "OneComme.exe");
        StartApplication("OneComme", oneComme);
        steps.Add("OneComme");

        Thread.Sleep(500);

        var targetDisplay = FindTargetDisplay(DisplayManager.GetDisplays());
        if (targetDisplay is null)
        {
            steps.Add("display unchanged: no 2560x1440 display found");
        }
        else
        {
            DisplayManager.SetPrimaryDisplay(targetDisplay.DeviceName);
            steps.Add($"{targetDisplay.DisplayName} set as main display");
        }

        OpenUrl(InfinitasUrl);
        steps.Add("INFINITAS launcher page");

        return string.Join(" -> ", steps);
    }

    private static void StartApplication(string name, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{name} was not found.");
        }

        StartShellExecute(path);
    }

    private static void OpenUrl(string url)
    {
        StartShellExecute(url);
    }

    private static void StartShellExecute(string fileName)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true,
            WorkingDirectory = GetWorkingDirectory(fileName)
        });
    }

    private static string? GetWorkingDirectory(string fileName)
    {
        if (!Path.IsPathRooted(fileName) || !File.Exists(fileName))
        {
            return null;
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetDirectoryName(fileName);
    }

    private static string? FindShortcut(string fileName)
    {
        foreach (var startMenuPath in GetStartMenuPaths())
        {
            var shortcut = FindFile(startMenuPath, fileName);
            if (shortcut is not null)
            {
                return shortcut;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetStartMenuPaths()
    {
        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        if (!string.IsNullOrWhiteSpace(commonStartMenu))
        {
            yield return Path.Combine(commonStartMenu, "Programs");
        }

        var userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        if (!string.IsNullOrWhiteSpace(userStartMenu))
        {
            yield return Path.Combine(userStartMenu, "Programs");
        }
    }

    private static string? FindExecutableInLocalPrograms(string directoryName, string executableName)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        return FindFile(Path.Combine(localAppData, "Programs", directoryName), executableName);
    }

    private static string? FindFile(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(directory, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (UnauthorizedAccessException exception)
        {
            PluginLog.Write($"Could not search {directory}: {exception.Message}");
            return null;
        }
    }
}
