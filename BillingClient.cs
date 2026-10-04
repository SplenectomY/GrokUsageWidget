namespace GrokUsageWidget;

internal static class BillingClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static async Task<(HttpResponseMessage resp, string body)> BillingGetAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            "https://cli-chat-proxy.grok.com/v1/billing?format=credits");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("X-XAI-Token-Auth", "xai-grok-cli");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("User-Agent", "xai-grok-cli");
        var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (resp, body);
    }

    public static async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        string? token;
        HttpResponseMessage resp;
        string body;
        try
        {
            token = await AuthToken.RefreshIfNeededAsync(Http, ct).ConfigureAwait(false)
                    ?? AuthToken.Read();
            if (token is null)
                return new UsageSnapshot { Ok = false, Status = "no login — run grok login" };

            (resp, body) = await BillingGetAsync(token, ct).ConfigureAwait(false);
            if ((int)resp.StatusCode is 401 or 403)
            {
                var refreshed = await AuthToken.RefreshAsync(Http, AuthToken.Load(), ct).ConfigureAwait(false);
                if (refreshed is not null)
                    (resp, body) = await BillingGetAsync(refreshed, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new UsageSnapshot { Ok = false, Status = "network error" };
        }

        if ((int)resp.StatusCode is 401 or 403)
            return new UsageSnapshot { Ok = false, Status = "token expired — grok login" };
        if (!resp.IsSuccessStatusCode)
            return new UsageSnapshot { Ok = false, Status = $"http {(int)resp.StatusCode}" };

        try
        {
            TryWriteDebug(body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var config = Prop(root, "config") ?? root;

            double? pct =
                ReadDouble(config, "creditUsagePercent", "credit_usage_percent", "usedPercent", "used_percent")
                ?? FirstNamedDouble(config, "creditUsagePercent", "credit_usage_percent", "usedPercent");

            if (pct is null)
            {
                var used = Prop(config, "used", "includedUsed", "included_used");
                var cap = Prop(config, "monthlyLimit", "monthly_limit", "includedLimit", "included_limit");
                if (used is not null && cap is not null)
                {
                    var u = ReadVal(used.Value);
                    var c = ReadVal(cap.Value);
                    if (u is not null && c is > 0)
                        pct = 100.0 * u.Value / c.Value;
                }
            }

            // Unified weekly pool often omits 0.0 after a plan change / reset.
            var period = Prop(config, "currentPeriod", "current_period");
            DateTimeOffset? end = null;
            if (period is not null)
                end = ReadIso(Prop(period.Value, "end"));
            end ??= ReadIso(Prop(config, "billingPeriodEnd", "billing_period_end"));

            if (pct is null && (period is not null || end is not null))
                pct = 0;

            string? build = null;
            var products = Prop(config, "productUsage", "product_usage");
            if (products is { ValueKind: JsonValueKind.Array })
            {
                foreach (var p in products.Value.EnumerateArray())
                {
                    var name = Prop(p, "product")?.GetString() ?? "";
                    if (name.Contains("BUILD", StringComparison.OrdinalIgnoreCase))
                    {
                        var share = ReadDouble(p, "usagePercent", "usage_percent");
                        if (share is not null)
                            build = $"Build {share.Value:0}% of pool";
                    }
                }
            }

            if (pct is null)
                return new UsageSnapshot { Ok = false, Status = "plus payload — no %" };

            var extraCents = ReadDouble(config, "prepaidBalance", "prepaid_balance");
            decimal? extraUsd = extraCents is > 0
                ? Math.Round((decimal)extraCents.Value / 100m, 2, MidpointRounding.AwayFromZero)
                : null;

            return new UsageSnapshot
            {
                Ok = true,
                Status = "ok",
                UsedPercent = pct,
                ExtraCreditsUsd = extraUsd,
                ResetsAt = end,
                BuildShare = build
            };
        }
        catch
        {
            return new UsageSnapshot { Ok = false, Status = "bad json" };
        }
    }

    private static void TryWriteDebug(string body)
    {
        try
        {
            File.WriteAllText(Path.Combine(Paths.GrokHome, "usage-widget-last.json"), body);
        }
        catch { /* ignore */ }
    }

    private static JsonElement? Prop(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p.Value;
            }
        }
        return null;
    }

    private static double? ReadDouble(JsonElement obj, params string[] names)
    {
        var el = Prop(obj, names);
        return el is null ? null : ReadVal(el.Value);
    }

    private static double? FirstNamedDouble(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var p in obj.EnumerateObject())
        {
            if (names.Any(n => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            {
                var v = ReadVal(p.Value);
                if (v is not null)
                    return v;
            }
            if (p.Value.ValueKind == JsonValueKind.Object)
            {
                var nested = FirstNamedDouble(p.Value, names);
                if (nested is not null)
                    return nested;
            }
        }
        return null;
    }

    private static double? ReadVal(JsonElement wrapper)
    {
        if (wrapper.ValueKind == JsonValueKind.Number && wrapper.TryGetDouble(out var d))
            return d;
        if (wrapper.ValueKind == JsonValueKind.String
            && double.TryParse(wrapper.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        var inner = Prop(wrapper, "val", "value");
        if (inner is { ValueKind: JsonValueKind.Number } && inner.Value.TryGetDouble(out var v))
            return v;
        return null;
    }

    private static DateTimeOffset? ReadIso(JsonElement? el)
    {
        if (el is { ValueKind: JsonValueKind.String }
            && DateTimeOffset.TryParse(el.Value.GetString(), out var t))
            return t;
        return null;
    }
}
