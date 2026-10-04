namespace GrokUsageWidget;

internal sealed class UsageSnapshot
{
    public bool Ok { get; init; }
    public string Status { get; init; } = "";
    public double? UsedPercent { get; init; }
    public decimal? ExtraCreditsUsd { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
    public string? BuildShare { get; init; }
}
