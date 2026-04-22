namespace SwitchDisplayPlugin;

public sealed record DisplayChangeResult(
    DisplayInfo PrimaryDisplay,
    IReadOnlyList<DisplayInfo> Displays,
    string Message);
