namespace GrokUsageWidget;

internal static class Paths
{
    public static string GrokHome
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("GROK_HOME");
            if (!string.IsNullOrWhiteSpace(env))
                return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok");
        }
    }

    public static string AuthJson => Path.Combine(GrokHome, "auth.json");
    public static string WidgetConfig => Path.Combine(GrokHome, "usage-widget.json");
    public static string WidgetLog => Path.Combine(GrokHome, "usage-widget.log");
}
