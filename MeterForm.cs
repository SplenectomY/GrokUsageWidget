namespace GrokUsageWidget;

internal sealed class MeterForm : Form
{
    private readonly WidgetSettings _settings = WidgetSettings.Load();
    private readonly System.Windows.Forms.Timer _poll = new();
    private readonly System.Windows.Forms.Timer _clock = new();
    private readonly Label _pct = new();
    private readonly Label _sub = new();
    private readonly Label _credits = new();
    private readonly Panel _barFill = new();
    private readonly Panel _barTrack = new();
    private readonly NotifyIcon _tray = new();
    private readonly EventWaitHandle _revealPulse;
    private readonly CancellationTokenSource _revealCts = new();
    private UsageSnapshot? _last;
    private bool _dragging;
    private bool _suppressSave;
    private Point _dragOffset;

    public MeterForm(EventWaitHandle revealPulse)
    {
        _revealPulse = revealPulse;
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

        _credits.AutoSize = true;
        _credits.Font = new Font("Segoe UI Semibold", 8f);
        _credits.ForeColor = Color.FromArgb(230, 190, 70);
        _credits.Location = new Point(118, 8);
        _credits.Text = "";
        _credits.Visible = false;

        _barTrack.Location = new Point(12, 58);
        _barTrack.Size = new Size(196, 6);
        _barTrack.BackColor = Color.FromArgb(40, 40, 46);

        _barFill.Location = new Point(0, 0);
        _barFill.Size = new Size(0, 6);
        _barFill.BackColor = Color.FromArgb(80, 200, 120);
        _barTrack.Controls.Add(_barFill);

        Controls.Add(title);
        Controls.Add(_credits);
        Controls.Add(_pct);
        Controls.Add(_sub);
        Controls.Add(_barTrack);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Reveal", null, (_, _) => Reveal());
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add("Open Settings → Usage", null, (_, _) =>
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
            if (!_dragging && !_suppressSave && Visible)
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
            StartRevealListener();
            await RefreshAsync();
        };
        FormClosing += (_, _) =>
        {
            _revealCts.Cancel();
            try { _revealPulse.Set(); } catch { /* wake listener */ }
            SavePosition();
            _tray.Visible = false;
        };
    }

    private void StartRevealListener()
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (!_revealCts.IsCancellationRequested)
                {
                    if (_revealPulse.WaitOne(500) && !_revealCts.IsCancellationRequested)
                    {
                        try
                        {
                            if (IsHandleCreated)
                                BeginInvoke(Reveal);
                        }
                        catch { /* shutting down */ }
                    }
                }
            }
            catch (ObjectDisposedException) { /* exit */ }
        })
        {
            IsBackground = true,
            Name = "GrokUsageReveal"
        };
        thread.Start();
    }

    private void Reveal()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea
                 ?? new Rectangle(0, 0, 1280, 720);
        _suppressSave = true;
        try
        {
            Visible = true;
            WindowState = FormWindowState.Normal;
            Location = new Point(
                wa.Left + Math.Max(0, (wa.Width - Width) / 2),
                wa.Top + Math.Max(0, (wa.Height - Height) / 2));
            TopMost = true;
            Show();
            BringToFront();
            Activate();
        }
        finally
        {
            _suppressSave = false;
        }
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
            _credits.Visible = false;
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

        if (snap.ExtraCreditsUsd is > 0)
        {
            _credits.Text = $"${snap.ExtraCreditsUsd.Value:0.00} left";
            _credits.Visible = true;
            _tray.Text = $"Grok {used:0}%  ${snap.ExtraCreditsUsd.Value:0.00} left  {reset}";
        }
        else
        {
            _credits.Visible = false;
            _tray.Text = $"Grok {used:0}%  {reset}";
        }
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
