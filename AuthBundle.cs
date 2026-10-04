namespace GrokUsageWidget;

internal sealed class AuthBundle
{
    public string Access { get; set; } = "";
    public string? Refresh { get; set; }
    public string? ClientId { get; set; }
    public string? Issuer { get; set; }
    public string? PrincipalType { get; set; }
    public string? PrincipalId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? ScopeKey { get; set; }
}
