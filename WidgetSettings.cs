namespace GrokUsageWidget;

internal sealed class WidgetSettings
{
    public int PollSeconds { get; set; } = 60;
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public string? ScreenDevice { get; set; }

    public static WidgetSettings Load()
    {
        try
        {
            if (File.Exists(Paths.WidgetConfig))
            {
                var s = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(Paths.WidgetConfig));
                if (s != null)
                {
                    s.PollSeconds = Math.Clamp(s.PollSeconds, 15, 600);
                    return s;
                }
            }
        }
        catch { /* first run */ }

        return new WidgetSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.WidgetConfig)!);
            File.WriteAllText(Paths.WidgetConfig, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }
}
