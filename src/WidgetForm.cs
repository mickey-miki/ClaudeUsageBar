using System.Drawing.Drawing2D;

namespace ClaudeUsage;

public sealed class WidgetForm : Form
{
    static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    static readonly Color Back = Color.FromArgb(32, 33, 36), Fore = Color.FromArgb(240, 240, 240),
        Muted = Color.FromArgb(165, 168, 173), Track = Color.FromArgb(65, 67, 72);

    readonly AppState state;
    Point? dragOffset;
    string? error;
    bool hasError;

    public WidgetForm(AppState state)
    {
        this.state = state;
        Text = "Claude 使用量";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Back;
        _ = Handle;
        Restore();
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

    public static Color LevelColor(double percent) => Rounded(percent) switch
    {
        >= 80 => Color.FromArgb(239, 83, 80),
        >= 50 => Color.FromArgb(255, 202, 40),
        _ => Color.FromArgb(79, 172, 254)
    };

    public void SetStatus(bool failed, string? reason)
    {
        hasError = failed;
        error = reason;
        Invalidate();
    }

    public void Restore()
    {
        ClientSize = new Size(S(230), S(124));
        if (state is { X: { } x, Y: { } y } &&
            Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Point(x + Width / 2, y + Height / 2))))
        {
            Location = new Point(x, y);
            return;
        }
        // 初回、またはモニター構成が変わって画面外になったときは右下に置く。
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - S(12), area.Bottom - Height - S(12));
    }

    int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ClientSize = new Size(S(230), S(124));
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) dragOffset = e.Location;
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
            (state.X, state.Y) = (Left, Top);
            state.Save();
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var normal = new Font("Yu Gothic UI", S(13), FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Yu Gothic UI", S(11), GraphicsUnit.Pixel);
        DrawRow(g, normal, small, S(8), "セッション (5時間)", state.Last?.FiveHour);
        DrawRow(g, normal, small, S(56), "今週 (7日)", state.Last?.SevenDay);

        var time = state.Last is { } l ? TimeZoneInfo.ConvertTime(l.FetchedAt, Jst).ToString("M/d HH:mm") : null;
        var footer = hasError ? $"更新失敗({error})" + (time is null ? "" : $" {time}")
            : time is null ? "取得中…" : $"更新 {time}";
        Draw(g, footer, small, hasError ? Color.FromArgb(255, 138, 128) : Muted, S(103), TextFormatFlags.Left);
    }

    void DrawRow(Graphics g, Font normal, Font small, int y, string label, UsageWindow? w)
    {
        Draw(g, label, normal, Fore, y, TextFormatFlags.Left);
        Draw(g, w is null ? "--%" : $"{Rounded(w.Percent)}%", normal,
            w is null ? Muted : LevelColor(w.Percent), y, TextFormatFlags.Right);
        var bar = new Rectangle(S(10), y + S(19), ClientSize.Width - S(20), S(6));
        using (var b = new SolidBrush(Track)) g.FillRectangle(b, bar);
        if (w is not null)
        {
            using var b = new SolidBrush(LevelColor(w.Percent));
            g.FillRectangle(b, bar.X, bar.Y, (float)(bar.Width * Math.Clamp(w.Percent, 0, 100) / 100), bar.Height);
        }
        var reset = w?.ResetsAt is not { } r ? "リセット --"
            : r <= DateTimeOffset.UtcNow ? "リセット時刻を経過"
            : $"リセット {TimeZoneInfo.ConvertTime(r, Jst):M/d HH:mm}";
        Draw(g, reset, small, Muted, y + S(27), TextFormatFlags.Left);
    }

    void Draw(Graphics g, string text, Font font, Color color, int y, TextFormatFlags align) =>
        TextRenderer.DrawText(g, text, font, new Rectangle(S(10), y, ClientSize.Width - S(20), S(18)), color,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
}
