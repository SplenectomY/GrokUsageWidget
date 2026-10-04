namespace GrokUsageWidget;

internal static class AuthToken
{
    public static AuthBundle? Load()
    {
        if (!File.Exists(Paths.AuthJson))
            return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(Paths.AuthJson));
        return FindBundle(doc.RootElement, null);
    }

    public static string? Read() => Load()?.Access;

    public static bool LooksExpired(AuthBundle b)
    {
        if (b.ExpiresAt is null)
            return false;
        return b.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(2);
    }

    public static async Task<string?> RefreshIfNeededAsync(HttpClient http, CancellationToken ct)
    {
        var bundle = Load();
        if (bundle is null)
            return null;
        if (!LooksExpired(bundle) && !string.IsNullOrEmpty(bundle.Access))
            return bundle.Access;
        if (string.IsNullOrEmpty(bundle.Refresh))
            return bundle.Access;
        return await RefreshAsync(http, bundle, ct).ConfigureAwait(false) ?? bundle.Access;
    }

    public static async Task<string?> RefreshAsync(HttpClient http, AuthBundle? bundle, CancellationToken ct)
    {
        bundle ??= Load();
        if (bundle is null || string.IsNullOrEmpty(bundle.Refresh))
            return null;

        var clientId = bundle.ClientId ?? "b1a00492-073a-47ea-816f-4c329264a828";
        var tokenUrl = "https://auth.x.ai/oauth2/token";
        if (!string.IsNullOrEmpty(bundle.Issuer))
        {
            try
            {
                using var disc = await http.GetAsync(
                    bundle.Issuer.TrimEnd('/') + "/.well-known/openid-configuration", ct).ConfigureAwait(false);
                if (disc.IsSuccessStatusCode)
                {
                    using var ddoc = JsonDocument.Parse(await disc.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                    if (ddoc.RootElement.TryGetProperty("token_endpoint", out var te))
                        tokenUrl = te.GetString() ?? tokenUrl;
                }
            }
            catch { /* hardcoded fallback */ }
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = bundle.Refresh!,
            ["client_id"] = clientId
        };
        if (!string.IsNullOrEmpty(bundle.PrincipalType))
            form["principal_type"] = bundle.PrincipalType!;
        if (!string.IsNullOrEmpty(bundle.PrincipalId))
            form["principal_id"] = bundle.PrincipalId!;

        using var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (!root.TryGetProperty("access_token", out var at))
            return null;
        var access = at.GetString();
        if (string.IsNullOrEmpty(access))
            return null;

        string? newRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        int expiresIn = 0;
        if (root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var secs))
            expiresIn = secs;

        TryWriteTokens(bundle.ScopeKey, access, newRefresh ?? bundle.Refresh, expiresIn);
        return access;
    }

    private static void TryWriteTokens(string? scopeKey, string access, string? refresh, int expiresIn)
    {
        try
        {
            var path = Paths.AuthJson;
            var raw = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(raw);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                PatchObject(writer, doc.RootElement, scopeKey, access, refresh, expiresIn, depth: 0);
            }

            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, stream.ToArray());
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
        }
        catch { /* leave file alone */ }
    }

    private static void PatchObject(
        Utf8JsonWriter w, JsonElement el, string? scopeKey,
        string access, string? refresh, int expiresIn, int depth)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            el.WriteTo(w);
            return;
        }

        w.WriteStartObject();
        var isCred = el.TryGetProperty("key", out _) || el.TryGetProperty("refresh_token", out _);
        foreach (var p in el.EnumerateObject())
        {
            w.WritePropertyName(p.Name);
            if (isCred && p.Name is "key" or "access_token")
                w.WriteStringValue(access);
            else if (isCred && p.Name == "refresh_token" && !string.IsNullOrEmpty(refresh))
                w.WriteStringValue(refresh);
            else if (isCred && p.Name == "expires_at" && expiresIn > 0)
                w.WriteStringValue(DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToString("o"));
            else if (p.Value.ValueKind == JsonValueKind.Object)
                PatchObject(w, p.Value, scopeKey, access, refresh, expiresIn, depth + 1);
            else
                p.Value.WriteTo(w);
        }
        w.WriteEndObject();
    }

    private static AuthBundle? FindBundle(JsonElement el, string? scopeKey)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        string? key = Str(el, "key") ?? Str(el, "access_token") ?? Str(el, "accessToken");
        string? refresh = Str(el, "refresh_token") ?? Str(el, "refreshToken");
        if (!string.IsNullOrEmpty(key) && key.Length > 20)
        {
            DateTimeOffset? exp = null;
            var expS = Str(el, "expires_at") ?? Str(el, "expiresAt");
            if (expS != null && DateTimeOffset.TryParse(expS, out var parsed))
                exp = parsed;
            return new AuthBundle
            {
                Access = key,
                Refresh = refresh,
                ClientId = Str(el, "oidc_client_id") ?? Str(el, "oidcClientId"),
                Issuer = Str(el, "oidc_issuer") ?? Str(el, "oidcIssuer") ?? "https://auth.x.ai",
                PrincipalType = Str(el, "principal_type") ?? Str(el, "principalType"),
                PrincipalId = Str(el, "principal_id") ?? Str(el, "principalId"),
                ExpiresAt = exp,
                ScopeKey = scopeKey
            };
        }

        foreach (var p in el.EnumerateObject())
        {
            var found = FindBundle(p.Value, p.Name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static string? Str(JsonElement el, string name)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        }
        return null;
    }
}
