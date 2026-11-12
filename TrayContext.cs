using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AdapterToggle;

internal sealed class TrayContext : ApplicationContext
{
    private static readonly int[] AutoOffChoices = [0, 5, 10, 15, 30, 60];

    private readonly AdapterControl _adapter = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _statusItem, _onItem, _offItem, _autoOffMenu, _autoOnItem, _startupItem;
    private readonly Icon _iconActive, _iconIdle, _iconOff, _iconMissing;

    private Settings _settings;
    private AdapterState _state = AdapterState.NotFound;
    private bool _controllerConnected;
    private bool _gameRunning;
    private bool _busy;
    private bool _suppressAutoOn;
    private DateTime _lastActive = DateTime.UtcNow;

    public TrayContext()
    {
        _settings = Settings.Load();
        _adapter.NameMatch = _settings.DeviceNameMatch;

        _iconActive = MakeIcon(Color.LimeGreen);
        _iconIdle = MakeIcon(Color.Gold);
        _iconOff = MakeIcon(Color.DimGray);
        _iconMissing = MakeIcon(Color.IndianRed);

        _statusItem = new ToolStripMenuItem("…") { Enabled = false };
        _onItem = new ToolStripMenuItem("Turn adapter ON", null, (_, _) => _ = SetAdapterAsync(true, manual: true));
        _offItem = new ToolStripMenuItem("Turn adapter OFF", null, (_, _) => _ = SetAdapterAsync(false, manual: true));

        _autoOffMenu = new ToolStripMenuItem("Auto-off when no controller");
        foreach (int minutes in AutoOffChoices)
        {
            var item = new ToolStripMenuItem(minutes == 0 ? "Never" : $"After {minutes} min") { Tag = minutes };
            item.Click += (_, _) =>
            {
                _settings.AutoOffMinutes = minutes;
                _settings.Save();
                _lastActive = DateTime.UtcNow;
                UpdateUi();
            };
            _autoOffMenu.DropDownItems.Add(item);
        }

        _autoOnItem = new ToolStripMenuItem("Auto-on while a game is running", null, (_, _) =>
        {
            _settings.AutoOnEnabled = !_settings.AutoOnEnabled;
            _settings.Save();
            UpdateUi();
        });

        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = StartupTask.IsRegistered(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            _onItem,
            _offItem,
            new ToolStripSeparator(),
            _autoOffMenu,
            _autoOnItem,
            new ToolStripMenuItem("Edit settings file…", null, (_, _) => OpenSettingsFile()),
            new ToolStripMenuItem("Reload settings", null, (_, _) => ReloadSettings()),
            _startupItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        ]);

        _tray = new NotifyIcon { ContextMenuStrip = menu, Icon = _iconMissing, Text = "AdapterToggle", Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            if (_state == AdapterState.Enabled)
                _ = SetAdapterAsync(false, manual: true);
            else if (_state == AdapterState.Disabled)
                _ = SetAdapterAsync(true, manual: true);
        };

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, _settings.PollSeconds) * 1000 };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();

        Poll();
    }

    private void Poll()
    {
        if (_busy)
            return;

        try
        {
            _state = _adapter.GetState();
        }
        catch (Exception ex)
        {
            _state = AdapterState.NotFound;
            Debug.WriteLine(ex);
        }

        _controllerConnected = _state == AdapterState.Enabled && ControllerMonitor.AnyConnected();
        _gameRunning = _settings.AutoOnEnabled && ControllerMonitor.AnyProcessRunning(_settings.AutoOnProcesses);
        if (!_gameRunning)
            _suppressAutoOn = false;

        var now = DateTime.UtcNow;
        if (_state != AdapterState.Enabled || _controllerConnected || _gameRunning)
            _lastActive = now;

        if (_state == AdapterState.Disabled && _gameRunning && !_suppressAutoOn)
        {
            _ = SetAdapterAsync(true, manual: false, reason: "A game started");
        }
        else if (_state == AdapterState.Enabled && _settings.AutoOffMinutes > 0
                 && now - _lastActive >= TimeSpan.FromMinutes(_settings.AutoOffMinutes))
        {
            _ = SetAdapterAsync(false, manual: false, reason: $"No controller for {_settings.AutoOffMinutes} min");
        }

        UpdateUi();
    }

    private async Task SetAdapterAsync(bool enable, bool manual, string? reason = null)
    {
        if (_busy)
            return;

        // don't let auto-on immediately undo a manual off while a game is still running.
        if (manual)
            _suppressAutoOn = !enable && _gameRunning;

        _busy = true;
        UpdateUi();
        try
        {
            await Task.Run(() => _adapter.SetEnabled(enable));
            _lastActive = DateTime.UtcNow;
            if (!manual && reason is not null)
                _tray.ShowBalloonTip(3000, "AdapterToggle", $"{reason} – adapter turned {(enable ? "ON" : "OFF")}.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            // backoff, so a failing automatic action doesn't retry on every poll.
            _lastActive = DateTime.UtcNow;
            if (!manual && enable)
                _suppressAutoOn = true;
            _tray.ShowBalloonTip(5000, "AdapterToggle – failed", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            Poll();
        }
    }

    private void UpdateUi()
    {
        string status = _busy ? "Switching…" : _state switch
        {
            AdapterState.Enabled => _controllerConnected ? "ON – controller connected" : "ON – idle",
            AdapterState.Disabled => "OFF",
            AdapterState.Problem => "Device has a problem",
            _ => $"Not found (\"{_settings.DeviceNameMatch}\")",
        };

        _statusItem.Text = status;
        _onItem.Enabled = !_busy && _state is AdapterState.Disabled or AdapterState.Problem;
        _offItem.Enabled = !_busy && _state is AdapterState.Enabled or AdapterState.Problem;
        _autoOnItem.Checked = _settings.AutoOnEnabled;
        _autoOnItem.ToolTipText = _settings.AutoOnProcesses.Length == 0
            ? "Add process names to AutoOnProcesses in the settings file."
            : string.Join(", ", _settings.AutoOnProcesses);
        foreach (ToolStripMenuItem item in _autoOffMenu.DropDownItems)
            item.Checked = (int)item.Tag! == _settings.AutoOffMinutes;

        _tray.Icon = _state switch
        {
            AdapterState.Enabled => _controllerConnected ? _iconActive : _iconIdle,
            AdapterState.Disabled => _iconOff,
            _ => _iconMissing,
        };

        string tip = $"Xbox adapter: {status}";
        if (_state == AdapterState.Enabled && !_controllerConnected && _settings.AutoOffMinutes > 0 && !_gameRunning)
        {
            var left = TimeSpan.FromMinutes(_settings.AutoOffMinutes) - (DateTime.UtcNow - _lastActive);
            tip += $"\nAuto-off in {Math.Max(0, (int)Math.Ceiling(left.TotalMinutes))} min";
        }
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    private void ToggleStartup()
    {
        try
        {
            if (StartupTask.IsRegistered())
                StartupTask.Unregister();
            else
                StartupTask.Register();
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(5000, "AdapterToggle – failed", ex.Message, ToolTipIcon.Error);
        }
        _startupItem.Checked = StartupTask.IsRegistered();
    }

    private static void OpenSettingsFile()
    {
        if (!File.Exists(Settings.FilePath))
            new Settings().Save();
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Settings.FilePath}\"") { UseShellExecute = true });
    }

    private void ReloadSettings()
    {
        _settings = Settings.Load();
        _adapter.NameMatch = _settings.DeviceNameMatch;
        _timer.Interval = Math.Max(1, _settings.PollSeconds) * 1000;
        _lastActive = DateTime.UtcNow;
        Poll();
    }

    private static Icon MakeIcon(Color fill)
    {
        var size = SystemInformation.SmallIconSize;
        using var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(1, 1, size.Width - 3, size.Height - 3);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(Color.Black, Math.Max(1f, size.Width / 16f));
            g.FillEllipse(brush, rect);
            g.DrawEllipse(pen, rect);
        }
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _iconActive.Dispose();
            _iconIdle.Dispose();
            _iconOff.Dispose();
            _iconMissing.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        base.ExitThreadCore();
    }
}
