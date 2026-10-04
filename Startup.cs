namespace GrokUsageWidget;

internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GrokUsageWidget";

    public static string ExePath =>
        Environment.ProcessPath
        ?? Application.ExecutablePath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? "";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var val = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var path = ExePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("Cannot find this exe. Publish it, then enable Start with Windows.");
            key.SetValue(ValueName, "\"" + path + "\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
