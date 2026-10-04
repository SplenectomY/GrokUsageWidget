namespace GrokUsageWidget;

internal static class AppIcon
{
    public static Icon Grok { get; } = Load();

    private static Icon Load()
    {
        var nextToExe = Path.Combine(AppContext.BaseDirectory, "grok.ico");
        if (File.Exists(nextToExe))
            return new Icon(nextToExe);

        try
        {
            var fromExe = Icon.ExtractAssociatedIcon(
                Environment.ProcessPath ?? Application.ExecutablePath);
            if (fromExe != null)
                return fromExe;
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }
}
