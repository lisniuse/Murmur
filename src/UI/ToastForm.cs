using System.Runtime.InteropServices;

namespace VoiceAssistant;

public class ToastForm : Form
{
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    private static readonly List<ToastForm> _active = [];
    private static readonly object _lock = new();

    private readonly System.Windows.Forms.Timer _fadeInTimer  = new() { Interval = 12 };
    private readonly System.Windows.Forms.Timer _displayTimer;
    private readonly System.Windows.Forms.Timer _fadeOutTimer = new() { Interval = 12 };
    private readonly System.Windows.Forms.Timer _slideTimer   = new() { Interval = 12 };

    private int _targetY;

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    public static void Show(string title, string message, Color accent, int duration = 3000)
    {
        var toast = new ToastForm(title, message, accent, duration);
        toast.Show();
    }

    private ToastForm(string title, string message, Color accent, int duration)
    {
        SuspendLayout();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = true;
        StartPosition   = FormStartPosition.Manual;
        BackColor       = Color.FromArgb(28, 28, 28);
        Opacity         = 0;

        const int W      = 300;
        const int Pad    = 14;
        const int AccentW = 4;

        Controls.Add(new Panel
        {
            BackColor = accent,
            Dock      = DockStyle.Left,
            Width     = AccentW
        });

        var lblTitle = new Label
        {
            Text        = title,
            Font        = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor   = Color.White,
            Location    = new Point(AccentW + Pad, Pad),
            AutoSize    = true,
            MaximumSize = new Size(W - AccentW - Pad * 2, 0)
        };
        Controls.Add(lblTitle);

        var lblMsg = new Label
        {
            Text        = message,
            Font        = new Font("Segoe UI", 9f),
            ForeColor   = Color.FromArgb(195, 195, 195),
            Location    = new Point(AccentW + Pad, Pad + lblTitle.PreferredHeight + 4),
            AutoSize    = true,
            MaximumSize = new Size(W - AccentW - Pad * 2, 0)
        };
        Controls.Add(lblMsg);

        int h = Pad + lblTitle.PreferredHeight + 4 + lblMsg.PreferredHeight + Pad;
        Size = new Size(W, Math.Max(h, 56));

        Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 12, 12));

        ResumeLayout(false);

        // 点击关闭
        Click += (_, _) => BeginFadeOut();
        foreach (Control c in Controls)
            c.Click += (_, _) => BeginFadeOut();

        // 淡入
        _fadeInTimer.Tick += (_, _) =>
        {
            Opacity += 0.1;
            if (Opacity >= 1) { Opacity = 1; _fadeInTimer.Stop(); }
        };

        // 停留后淡出
        _displayTimer = new System.Windows.Forms.Timer { Interval = duration };
        _displayTimer.Tick += (_, _) => { _displayTimer.Stop(); BeginFadeOut(); };

        // 淡出
        _fadeOutTimer.Tick += (_, _) =>
        {
            Opacity -= 0.07;
            if (Opacity <= 0) { _fadeOutTimer.Stop(); Close(); }
        };

        // 滑动：平滑移向 _targetY
        _slideTimer.Tick += (_, _) =>
        {
            int diff = _targetY - Top;
            if (Math.Abs(diff) <= 1)
            {
                Top = _targetY;
                _slideTimer.Stop();
            }
            else
            {
                Top += diff / 3 + (diff > 0 ? 1 : -1);
            }
        };
    }

    // 新气泡出现时把自己的目标 Y 向上移动 amount 像素
    private void PushUp(int amount)
    {
        _targetY -= amount;
        if (!_slideTimer.Enabled)
            _slideTimer.Start();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // 新气泡总是从右下角底部出现，同时把已有气泡向上推
        var area = Screen.PrimaryScreen!.WorkingArea;
        _targetY = area.Bottom - Height - 12;
        Location = new Point(area.Right - Width - 12, _targetY);

        lock (_lock)
        {
            foreach (var t in _active)
                if (!t.IsDisposed)
                    t.PushUp(Height + 8);

            _active.Add(this);
        }

        _fadeInTimer.Start();
        _displayTimer.Start();
    }

    private void BeginFadeOut()
    {
        _displayTimer.Stop();
        if (!_fadeOutTimer.Enabled)
            _fadeOutTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        lock (_lock) _active.Remove(this);
        _fadeInTimer.Dispose();
        _displayTimer.Dispose();
        _fadeOutTimer.Dispose();
        _slideTimer.Dispose();
    }
}
