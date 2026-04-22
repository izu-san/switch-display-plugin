using Flow.Launcher.Plugin;

namespace SwitchDisplayPlugin;

public sealed class Main : IPlugin
{
    private PluginInitContext? _context;

    public void Init(PluginInitContext context)
    {
        _context = context;
        PluginLog.Write("Plugin initialized.");
    }

    public List<Result> Query(Query query)
    {
        try
        {
            var displays = DisplayManager.GetDisplays();
            var search = query.Search.Trim();
            var results = new List<Result>();

            if (StreamingSetup.Matches(search))
            {
                results.Add(ToStreamingSetupResult(displays));
            }

            var matches = string.IsNullOrWhiteSpace(search)
                ? displays
                : displays.Where(display => Matches(display, search)).ToList();

            if (matches.Count == 0 && results.Count == 0)
            {
                return
                [
                    new Result
                    {
                        Title = "No displays found",
                        SubTitle = "Try a display number, device name, or monitor name."
                    }
                ];
            }

            results.AddRange(matches
                .OrderByDescending(display => display.IsPrimary)
                .ThenBy(display => display.DisplayNumber)
                .Select(ToResult));

            return results;
        }
        catch (Exception exception)
        {
            return
            [
                new Result
                {
                    Title = "Could not read displays",
                    SubTitle = exception.Message
                }
            ];
        }
    }

    private Result ToResult(DisplayInfo display)
    {
        var title = display.IsPrimary
            ? $"{display.DisplayName} (current main display)"
            : $"Set {display.DisplayName} as main display";

        return new Result
        {
            Title = title,
            SubTitle = $"{display.MonitorName} - {display.Width}x{display.Height} at {display.X},{display.Y}",
            AsyncAction = async _ =>
            {
                try
                {
                    PluginLog.Write($"Action started: {display.DisplayName} {display.DeviceName}, primary={display.IsPrimary}.");
                    var result = DisplayManager.SetPrimaryDisplay(display.DeviceName);
                    PluginLog.Write($"Action completed: {result.Message} Primary={result.PrimaryDisplay.DisplayName} {result.PrimaryDisplay.DeviceName}.");
                    await Task.Delay(500);
                    _context?.API.ReQuery(true);
                    _context?.API.ShowMsg("Main display changed", $"{result.PrimaryDisplay.DisplayName} is now the main display.");
                    return true;
                }
                catch (Exception exception)
                {
                    PluginLog.Write($"Action failed: {exception}");
                    _context?.API.ShowMsg("Could not change main display", exception.Message);
                    return false;
                }
            }
        };
    }

    private Result ToStreamingSetupResult(IReadOnlyList<DisplayInfo> displays)
    {
        var targetDisplay = StreamingSetup.FindTargetDisplay(displays);
        var subtitle = targetDisplay is null
            ? "Launch OBS Studio, OneComme, and INFINITAS. No 2560x1440 display was found."
            : $"Launch OBS Studio, OneComme, set {targetDisplay.DisplayName} ({targetDisplay.MonitorName}) as main, then open INFINITAS.";

        return new Result
        {
            Title = "Start INFINITAS stream setup",
            SubTitle = subtitle,
            AsyncAction = async _ =>
            {
                try
                {
                    PluginLog.Write("Streaming setup started.");
                    var setupResult = await Task.Run(() => StreamingSetup.Run());
                    PluginLog.Write($"Streaming setup completed: {setupResult}");
                    await Task.Delay(500);
                    _context?.API.ReQuery(true);
                    _context?.API.ShowMsg("Stream setup started", setupResult);
                    return true;
                }
                catch (Exception exception)
                {
                    PluginLog.Write($"Streaming setup failed: {exception}");
                    _context?.API.ShowMsg("Could not start stream setup", exception.Message);
                    return false;
                }
            }
        };
    }

    private static bool Matches(DisplayInfo display, string search)
    {
        return display.DisplayNumber.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
            || display.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || display.MonitorName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || display.DeviceName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
