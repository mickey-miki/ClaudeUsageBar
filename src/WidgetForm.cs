using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ClaudeUsageBar;

/// <summary>配色。50％・80％以上の黄・赤は警告なので、どの配色でも変えない。</summary>
public sealed record Theme(string Name, Color Back, Color Accent)
{
    public static readonly Theme[] All =
    [
        new("ダーク", Color.FromArgb(32, 33, 36), Color.FromArgb(79, 172, 254)),
        new("ブラック", Color.FromArgb(12, 12, 12), Color.FromArgb(225, 225, 225)),
        new("ネイビー", Color.FromArgb(18, 30, 58), Color.FromArgb(110, 190, 255)),
        new("パープル", Color.FromArgb(42, 26, 62), Color.FromArgb(196, 150, 255)),
        new("グリーン", Color.FromArgb(16, 44, 34), Color.FromArgb(90, 220, 150)),
        new("ピンク", Color.FromArgb(58, 24, 44), Color.FromArgb(255, 150, 205)),
    ];

    public static Theme Find(string? name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];
}

public sealed class WidgetForm : Form
{
    static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    static readonly Color Fore = Color.FromArgb(240, 240, 240), Muted = Color.FromArgb(185, 188, 193),
        Failed = Color.FromArgb(255, 138, 128);
    const int BaseWidth = 350, BaseHeight = 40;

    readonly AppState state;
    readonly ToolTip tip = new();
    readonly System.Windows.Forms.Timer topTimer = new() { Interval = 1000 };
    Point? dragOffset;
    Point dragStart;
    string? error;
    bool hasError;

    public WidgetForm(AppState state)
    {
        this.state = state;
        Text = "ClaudeUsageBar";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        _ = Handle;
        ApplyTheme();
        Restore();

        // タスクバーも最前面なので、クリックなどで隠されたら前に戻す。
        // 自動配置のときは、通知領域の幅の変化にも追従する。
        topTimer.Tick += (_, _) =>
        {
            if (!Visible || dragOffset is not null) return;
            if (state.X is null) Restore();
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        };
        topTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            // TOPMOST | TOOLWINDOW | NOACTIVATE（ShowWithoutActivation では TopMost プロパティが効かない）
            p.ExStyle |= 0x8 | 0x80 | 0x08000000;
            return p;
        }
    }

    public static int Rounded(double percent) => (int)Math.Round(percent, MidpointRounding.AwayFromZero);

    public static Color LevelColor(double percent, Color normal) => Rounded(percent) switch
    {
        >= 80 => Color.FromArgb(239, 83, 80),
        >= 50 => Color.FromArgb(255, 202, 40),
        _ => normal
    };

    public void ApplyTheme()
    {
        BackColor = Theme.Find(state.Theme).Back;
        Invalidate();
    }

    public void SetStatus(bool failed, string? reason)
    {
        hasError = failed;
        error = reason;
        tip.SetToolTip(this, failed ? $"更新失敗: {reason}" : "");
        Invalidate();
    }

    public void Restore()
    {
        ClientSize = new Size(S(BaseWidth), S(BaseHeight));
        if (state is { X: { } x, Y: { } y } &&
            Screen.AllScreens.Any(s => s.Bounds.Contains(new Point(x + Width / 2, y + Height / 2))))
        {
            Location = new Point(x, y);
            return;
        }
        // 位置の指定がない、または画面外のときは、タスクバーの通知領域の左隣に置く。
        var bar = FindWindow("Shell_TrayWnd", null);
        var notify = bar == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && GetWindowRect(bar, out var b) && GetWindowRect(notify, out var n)
            && b.Right - b.Left > b.Bottom - b.Top)
        {
            Location = new Point(n.Left - Width - S(6), b.Top + (b.Bottom - b.Top - Height) / 2);
            return;
        }
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - S(12), area.Bottom - Height - S(12));
    }

    public void DockToTaskbar()
    {
        (state.X, state.Y) = (null, null);
        Restore();
    }

    int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ClientSize = new Size(S(BaseWidth), S(BaseHeight));
        Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            state.Visible = false;
            state.Save();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            topTimer.Dispose();
            tip.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) (dragOffset, dragStart) = (e.Location, Location);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragOffset is { } o)
            Location = new Point(Cursor.Position.X - o.X, Cursor.Position.Y - o.Y);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && dragOffset is not null)
        {
            dragOffset = null;
            if (Location != dragStart)
            {
                SnapToTaskbar();
                (state.X, state.Y) = (Left, Top);
                state.Save();
            }
        }
        base.OnMouseUp(e);
    }

    /// <summary>横向きのタスクバーに落としたら、上下の中央にそろえる。</summary>
    void SnapToTaskbar()
    {
        var screen = Screen.FromControl(this);
        var center = new Point(Left + Width / 2, Top + Height / 2);
        var (all, work) = (screen.Bounds, screen.WorkingArea);
        if (center.Y >= work.Bottom && work.Bottom < all.Bottom)
            Top = work.Bottom + (all.Bottom - work.Bottom - Height) / 2;
        else if (center.Y < work.Top && work.Top > all.Top)
            Top = all.Top + (work.Top - all.Top - Height) / 2;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var normal = new Font("Yu Gothic UI", S(12), FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Yu Gothic UI", S(10), GraphicsUnit.Pixel);
        DrawCell(g, normal, small, 10, "5時間", state.Last?.FiveHour);
        DrawCell(g, normal, small, 148, "今週", state.Last?.SevenDay);

        var time = state.Last is { } l ? TimeZoneInfo.ConvertTime(l.FetchedAt, Jst).ToString("M/d HH:mm") : "";
        var color = hasError ? Failed : Muted;
        Draw(g, hasError ? "更新失敗" : time == "" ? "取得中…" : "更新", small, color, 284, 4, 60, TextFormatFlags.Right);
        Draw(g, time, small, color, 284, 21, 60, TextFormatFlags.Right);
    }

    void DrawCell(Graphics g, Font normal, Font small, int x, string label, UsageWindow? w)
    {
        const int width = 126;
        var level = w is null ? Muted : LevelColor(w.Percent, Theme.Find(state.Theme).Accent);
        Draw(g, label, normal, Fore, x, 1, width, TextFormatFlags.Left);
        Draw(g, w is null ? "--%" : $"{Rounded(w.Percent)}%", normal, level, x, 1, width, TextFormatFlags.Right);

        var bar = new Rectangle(S(x), S(18), S(width), S(5));
        using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillRectangle(b, bar);
        if (w is not null)
        {
            using var b = new SolidBrush(level);
            g.FillRectangle(b, bar.X, bar.Y, (float)(bar.Width * Math.Clamp(w.Percent, 0, 100) / 100), bar.Height);
        }
        var reset = w?.ResetsAt is not { } r ? "リセット --"
            : r <= DateTimeOffset.UtcNow ? "リセット時刻を経過"
            : $"リセット {TimeZoneInfo.ConvertTime(r, Jst):M/d HH:mm}";
        Draw(g, reset, small, Muted, x, 23, width, TextFormatFlags.Left);
    }

    void Draw(Graphics g, string text, Font font, Color color, int x, int y, int width, TextFormatFlags align) =>
        TextRenderer.DrawText(g, text, font, new Rectangle(S(x), S(y), S(width), S(16)), color,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
