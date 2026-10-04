namespace GrokUsageWidget;

internal static class CrashLog
{
    public static void Write(string msg)
    {
        try
        {
            Directory.CreateDirectory(Paths.GrokHome);
            File.AppendAllText(Paths.WidgetLog, DateTime.Now.ToString("s") + " " + msg + Environment.NewLine);
        }
        catch { /* nowhere to write */ }
    }
}
