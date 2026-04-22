namespace SwitchDisplayPlugin;

public sealed record DisplayInfo(
    int DisplayNumber,
    string DeviceName,
    string DisplayName,
    string MonitorName,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary);
