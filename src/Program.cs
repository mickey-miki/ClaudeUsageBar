using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeUsage;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\ClaudeUsageWidget", out var first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(new OAuthUsageSource()));
    }
}

sealed class TrayContext : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ClaudeUsage";
    static readonly TimeSpan Interval = TimeSpan.FromMinutes(5), ManualGap = TimeSpan.FromSeconds(30);

    readonly IUsageSource source;
    readonly AppState state = AppState.Load();
    readonly WidgetForm widget;
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    DateTimeOffset nextDue = DateTimeOffset.MinValue, lastTry = DateTimeOffset.MinValue,
        blockedUntil = DateTimeOffset.MinValue;
    bool busy;

    public TrayContext(IUsageSource source)
    {
        this.source = source;
        widget = new WidgetForm(state);

        var startup = new ToolStripMenuItem("Windows起動時に開始");
        startup.Click += (_, _) => SetStartup(!IsStartup());
        var menu = new ContextMenuStrip();
        menu.Items.Add("表示/非表示", null, (_, _) => Toggle());
        menu.Items.Add("今すぐ更新", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => startup.Checked = IsStartup();

        tray = new NotifyIcon { ContextMenuStrip = menu };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
        UpdateTray(false);
        tray.Visible = true;

        // 経過判定を実時刻で行い、スリープ復帰後もすぐ更新する。
        timer.Tick += async (_, _) =>
        {
            timer.Interval = 15000;
            widget.Invalidate();
            if (DateTimeOffset.UtcNow >= nextDue) await RefreshAsync();
        };
        timer.Start();
        if (state.Visible) widget.Show();
    }

    async Task RefreshAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (busy || now < blockedUntil || now - lastTry < ManualGap) return;
        busy = true;
        lastTry = now;
        nextDue = now + Interval;
        try
        {
            state.Last = await source.FetchAsync();
            state.Save();
            widget.SetStatus(false, null);
            UpdateTray(false);
        }
        catch (Exception e)
        {
            var known = e as UsageException;
            if (known?.RetryAfter is { } wait) nextDue = blockedUntil = DateTimeOffset.UtcNow + wait;
            widget.SetStatus(true, known?.Message ?? "内部エラー");
            UpdateTray(true);
        }
        finally { busy = false; }
    }

    void Toggle()
    {
        if (widget.Visible) widget.Hide();
        else { widget.Restore(); widget.Show(); }
        state.Visible = widget.Visible;
        state.Save();
    }

    static string StartupCommand => $"\"{Environment.ProcessPath}\"";

    static bool IsStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(RunName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
    }

    static void SetStartup(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(RunName, StartupCommand);
            else key.DeleteValue(RunName, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show("自動起動の設定を変更できませんでした。", "Claude 使用量");
        }
    }

    void UpdateTray(bool failed)
    {
        var last = state.Last;
        var max = last is null ? 0 : Math.Max(last.FiveHour.Percent, last.SevenDay.Percent);
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(32, 33, 36));
            using var b = new SolidBrush(failed || last is null ? Color.Gray : WidgetForm.LevelColor(max));
            var h = Math.Max(2, (int)Math.Round(14 * Math.Clamp(max, 0, 100) / 100));
            g.FillRectangle(b, 3, 15 - h, 10, h);
        }
        var handle = bmp.GetHicon();
        var old = tray.Icon;
        using (var temp = Icon.FromHandle(handle)) tray.Icon = new Icon(temp, temp.Size);
        old?.Dispose();
        DestroyIcon(handle);
        tray.Text = last is null ? "Claude 使用量"
            : $"5時間 {WidgetForm.Rounded(last.FiveHour.Percent)}% / 7日 {WidgetForm.Rounded(last.SevenDay.Percent)}%"
              + (failed ? "（更新失敗）" : "");
    }

    protected override void ExitThreadCore()
    {
        timer.Dispose();
        tray.Visible = false;
        tray.Dispose();
        widget.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
