using System.Text.Json;
using Microsoft.Win32;

namespace GrokUsageWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\GrokUsageWidget.SingleInstance", out var created);
        if (!created)
            return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MeterForm());
        GC.KeepAlive(mutex);
    }
}

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
}

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

internal sealed class UsageSnapshot
{
    public bool Ok { get; init; }
    public string Status { get; init; } = "";
    public double? UsedPercent { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
    public string? BuildShare { get; init; }
}

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

            return new UsageSnapshot
            {
                Ok = true,
                Status = "ok",
                UsedPercent = pct,
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

internal sealed class MeterForm : Form
{
    private readonly WidgetSettings _settings = WidgetSettings.Load();
    private readonly System.Windows.Forms.Timer _poll = new();
    private readonly System.Windows.Forms.Timer _clock = new();
    private readonly Label _pct = new();
    private readonly Label _sub = new();
    private readonly Panel _barFill = new();
    private readonly Panel _barTrack = new();
    private readonly NotifyIcon _tray = new();
    private UsageSnapshot? _last;
    private bool _dragging;
    private Point _dragOffset;

    public MeterForm()
    {
        Text = "Grok usage";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(220, 72);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.White;
        Padding = new Padding(10);
        DoubleBuffered = true;
        Icon = AppIcon.Grok;

        var title = new Label
        {
            AutoSize = true,
            Text = "SUPERGROK WEEK",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(160, 160, 168),
            Location = new Point(12, 8)
        };

        _pct.AutoSize = true;
        _pct.Font = new Font("Segoe UI Semibold", 18f);
        _pct.Location = new Point(10, 22);
        _pct.Text = "—";

        _sub.AutoSize = true;
        _sub.Font = new Font("Segoe UI", 8f);
        _sub.ForeColor = Color.FromArgb(170, 170, 178);
        _sub.Location = new Point(88, 32);
        _sub.MaximumSize = new Size(124, 32);
        _sub.Text = "starting…";

        _barTrack.Location = new Point(12, 58);
        _barTrack.Size = new Size(196, 6);
        _barTrack.BackColor = Color.FromArgb(40, 40, 46);

        _barFill.Location = new Point(0, 0);
        _barFill.Size = new Size(0, 6);
        _barFill.BackColor = Color.FromArgb(80, 200, 120);
        _barTrack.Controls.Add(_barFill);

        Controls.Add(title);
        Controls.Add(_pct);
        Controls.Add(_sub);
        Controls.Add(_barTrack);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add("Open Usage page", null, (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://grok.com/",
                UseShellExecute = true
            }));
        var startItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        startItem.Checked = Startup.IsEnabled();
        startItem.Click += (_, _) =>
        {
            try
            {
                Startup.SetEnabled(startItem.Checked);
            }
            catch (Exception ex)
            {
                startItem.Checked = false;
                MessageBox.Show(ex.Message, "Start with Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        menu.Items.Add(startItem);
        menu.Items.Add("Hide", null, (_, _) => Hide());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            SavePosition();
            Application.Exit();
        });
        menu.Opening += (_, _) => startItem.Checked = Startup.IsEnabled();

        _tray.Text = "Grok usage";
        _tray.Icon = AppIcon.Grok;
        _tray.Visible = true;
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) =>
        {
            Visible = !Visible;
            if (Visible) Activate();
        };

        MouseDown += BeginDrag;
        title.MouseDown += BeginDrag;
        _pct.MouseDown += BeginDrag;
        _sub.MouseDown += BeginDrag;
        MouseMove += DragMove;
        MouseUp += EndDrag;
        title.MouseMove += DragMove;
        title.MouseUp += EndDrag;
        _pct.MouseMove += DragMove;
        _pct.MouseUp += EndDrag;

        LocationChanged += (_, _) =>
        {
            if (!_dragging && Visible)
                SavePosition();
        };

        PlaceOnScreen();

        _poll.Interval = _settings.PollSeconds * 1000;
        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();

        _clock.Interval = 30_000;
        _clock.Tick += (_, _) => Render(_last);
        _clock.Start();

        Shown += async (_, _) =>
        {
            PlaceOnScreen();
            await RefreshAsync();
        };
        FormClosing += (_, _) =>
        {
            SavePosition();
            _tray.Visible = false;
        };
    }

    private void SavePosition()
    {
        var screen = Screen.FromControl(this) ?? Screen.FromPoint(Location) ?? Screen.PrimaryScreen;
        _settings.X = Left;
        _settings.Y = Top;
        if (screen != null)
        {
            _settings.ScreenDevice = screen.DeviceName;
            _settings.OffsetX = Left - screen.WorkingArea.Left;
            _settings.OffsetY = Top - screen.WorkingArea.Top;
        }
        _settings.Save();
    }

    private void PlaceOnScreen()
    {
        var screen = ResolveScreen();
        var wa = screen.WorkingArea;

        int x, y;
        if (!string.IsNullOrEmpty(_settings.ScreenDevice) || HasSavedPoint())
        {
            x = wa.Left + _settings.OffsetX;
            y = wa.Top + _settings.OffsetY;
            if (!HasSavedOffsets() && HasSavedPoint())
            {
                x = _settings.X;
                y = _settings.Y;
            }
        }
        else
        {
            x = wa.Right - Width - 16;
            y = wa.Top + 16;
        }

        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        Location = new Point(x, y);
    }

    private bool HasSavedPoint() =>
        _settings.X != int.MinValue && _settings.Y != int.MinValue
        && !(_settings.X == -1 && _settings.Y == -1 && string.IsNullOrEmpty(_settings.ScreenDevice));

    private bool HasSavedOffsets() =>
        !string.IsNullOrEmpty(_settings.ScreenDevice);

    private Screen ResolveScreen()
    {
        if (!string.IsNullOrEmpty(_settings.ScreenDevice))
        {
            var named = Screen.AllScreens.FirstOrDefault(s =>
                string.Equals(s.DeviceName, _settings.ScreenDevice, StringComparison.OrdinalIgnoreCase));
            if (named != null)
                return named;
        }

        if (HasSavedPoint())
        {
            var pt = new Point(_settings.X, _settings.Y);
            var hit = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains(pt))
                      ?? Screen.FromPoint(pt);
            if (hit != null)
                return hit;
        }

        return Screen.PrimaryScreen ?? Screen.AllScreens.First();
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragOffset = e.Location;
        if (sender is Control c && c != this)
            _dragOffset = new Point(e.X + c.Left, e.Y + c.Top);
    }

    private void DragMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var screen = PointToScreen(e.Location);
        if (sender is Control c && c != this)
            screen = c.PointToScreen(e.Location);
        Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
    }

    private void EndDrag(object? sender, MouseEventArgs e)
    {
        _dragging = false;
        SavePosition();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snap = await BillingClient.FetchAsync(CancellationToken.None);
            _last = snap;
            if (IsHandleCreated)
                BeginInvoke(() => Render(snap));
        }
        catch
        {
            _last = new UsageSnapshot { Ok = false, Status = "error" };
            if (IsHandleCreated)
                BeginInvoke(() => Render(_last));
        }
    }

    private void Render(UsageSnapshot? snap)
    {
        if (snap is null)
            return;

        if (!snap.Ok || snap.UsedPercent is null)
        {
            _pct.Text = "—";
            _pct.ForeColor = Color.FromArgb(220, 180, 80);
            _sub.Text = snap.Status;
            _barFill.Width = 0;
            _tray.Text = "Grok: " + snap.Status;
            return;
        }

        var used = Math.Clamp(snap.UsedPercent.Value, 0, 100);
        _pct.Text = $"{used:0}%";
        _pct.ForeColor = used >= 90 ? Color.FromArgb(230, 80, 80)
            : used >= 70 ? Color.FromArgb(230, 180, 70)
            : Color.FromArgb(90, 210, 130);

        var reset = snap.ResetsAt is { } t
            ? RelTime(t)
            : "reset unknown";
        _sub.Text = snap.BuildShare is null ? reset : reset + "\n" + snap.BuildShare;

        _barFill.Width = (int)Math.Round(_barTrack.Width * used / 100.0);
        _barFill.BackColor = _pct.ForeColor;
        _tray.Text = $"Grok {used:0}%  {reset}";
    }

    private static string RelTime(DateTimeOffset end)
    {
        var left = end - DateTimeOffset.Now;
        if (left.TotalSeconds <= 0)
            return "reset due";
        if (left.TotalHours >= 24)
            return $"resets in {left.Days}d {left.Hours}h";
        if (left.TotalHours >= 1)
            return $"resets in {(int)left.TotalHours}h {left.Minutes}m";
        return $"resets in {left.Minutes}m";
    }
}
