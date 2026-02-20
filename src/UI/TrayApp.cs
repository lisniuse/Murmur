namespace VoiceAssistant;

public class TrayApp : ApplicationContext
{
    private const string AppName    = "Murmur";
    private const string AppVersion = "v0.1.0";
    private readonly NotifyIcon _tray;
    private readonly VoiceListener _listener;
    private AppSettings _settings;
    private ToolStripMenuItem _statusItem = null!;
    private ToolStripMenuItem _deviceItem = null!;
    // 用隐藏 Control 做 UI 线程调度，比 SynchronizationContext 更可靠
    private readonly Control _uiInvoker = new();

    public TrayApp()
    {
        // 强制在主线程创建句柄，后续 BeginInvoke 就能安全跨线程调用
        _uiInvoker.CreateControl();

        _settings = AppSettings.Load();
        ApplyModelsPath(_settings);

        _listener = new VoiceListener();
        _listener.OnWakeWord += OnWakeWord;
        _listener.OnCommand += OnCommand;
        _listener.OnInputWakeWord += OnInputWakeWord;
        _listener.OnInputCommand += OnInputCommand;
        _listener.OnError += OnError;
        _listener.OnStatus += OnStatus;

        var menu = new ContextMenuStrip();

        _statusItem = (ToolStripMenuItem)menu.Items.Add("● 初始化中...");
        _statusItem.Enabled = false;
        _statusItem.ForeColor = Color.Gray;

        _deviceItem = new ToolStripMenuItem();
        _deviceItem.Enabled = false;
        _deviceItem.Visible = false;
        _deviceItem.Font = new Font(SystemFonts.MenuFont!.FontFamily, SystemFonts.MenuFont.Size - 1);
        menu.Items.Add(_deviceItem);

        var pauseItem = new ToolStripMenuItem("暂停监听");
        pauseItem.Click += (s, e) =>
        {
            if (_listener.IsPaused)
            {
                _listener.Resume();
                pauseItem.Text = "暂停监听";
                _statusItem.Text = $"● 监听中（唤醒词：{_settings.WakeWord}）";
                _statusItem.ForeColor = Color.Green;
                _tray!.Text = $"{AppName} - 监听中（{_settings.WakeWord}）";
            }
            else
            {
                _listener.Pause();
                pauseItem.Text = "开始监听";
                _statusItem.Text = $"● 已暂停（唤醒词：{_settings.WakeWord}）";
                _statusItem.ForeColor = Color.Gray;
                _tray!.Text = $"{AppName} - 已暂停";
            }
        };
        menu.Items.Add(pauseItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置", null, (s, e) => OpenSettings());

        var startupItem = new ToolStripMenuItem("开机自启") { Checked = StartupManager.IsEnabled() };
        startupItem.Click += (s, e) =>
        {
            if (StartupManager.IsEnabled()) { StartupManager.Disable(); startupItem.Checked = false; }
            else { StartupManager.Enable(); startupItem.Checked = true; }
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关于", null, (s, e) =>
        {
            MessageBox.Show(
                $"{AppName}  {AppVersion}\n\n唤醒词驱动的桌面语音助手\n支持 System.Speech / Whisper / Qwen3-ASR",
                $"关于 {AppName}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
        menu.Items.Add("退出", null, (s, e) => Quit());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"{AppName} - 初始化中...",
            Visible = true,
            ContextMenuStrip = menu
        };

        Logger.Log("INFO", "启动", $"{AppName} {AppVersion} 已启动");
        _ = InitAsync();
    }

    // 将操作转发到 UI 主线程执行
    private void RunOnUI(Action action) => _uiInvoker.BeginInvoke(action);

    // 颜色定义
    private static readonly Color ColorInfo    = Color.FromArgb(0, 120, 212);  // 蓝
    private static readonly Color ColorSuccess = Color.FromArgb(16, 196,  80);  // 绿
    private static readonly Color ColorError   = Color.FromArgb(232,  17,  35); // 红
    private static readonly Color ColorPaste   = Color.FromArgb(128,   0, 212); // 紫

    // 统一气泡提示 + 日志（必须在 UI 线程调用）
    private void ShowTip(string title, string message, Color accent, int duration = 3000, string logLevel = "INFO")
    {
        Logger.Log(logLevel, title, message);
        ToastForm.Show(title, message, accent, duration);
    }

    private async Task InitAsync()
    {
        try
        {
            await _listener.StartAsync(_settings);
            RunOnUI(() =>
            {
                _tray.Text = $"{AppName} - 监听中（{_settings.WakeWord}）";
                _statusItem.Text = $"● 监听中（唤醒词：{_settings.WakeWord}）";
                _statusItem.ForeColor = Color.Green;
                ShowTip($"{AppName} 就绪",
                    $"说「{_settings.WakeWord}」唤醒\n说「{_settings.InputWakeWord}」直接输入",
                    ColorInfo);
            });
        }
        catch (Exception ex)
        {
            RunOnUI(() =>
            {
                _statusItem.Text = "● 初始化失败";
                _statusItem.ForeColor = Color.Red;
                ShowTip("初始化失败", ex.Message, ColorError, 5000, "ERROR");
            });
        }
    }

    private static void ApplyModelsPath(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.ModelsPath))
            ProjectPaths.Models = s.ModelsPath;
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            ApplyModelsPath(_settings);
            _statusItem.Text = $"● 监听中（唤醒词：{_settings.WakeWord}）";
            // 切换离开 Qwen 模式时隐藏设备信息
            if (_settings.RecognitionMode != "Qwen")
                _deviceItem.Visible = false;
            _ = _listener.ReloadAsync(_settings);
            ShowTip("设置已保存", $"唤醒词：{_settings.WakeWord}", ColorSuccess);
        }
    }

    private void OnStatus(string msg) => RunOnUI(() =>
    {
        _tray.Text = $"{AppName} - {msg}"[..Math.Min(63, $"{AppName} - {msg}".Length)];
        // 初始化阶段同步更新菜单状态项（监听中/暂停状态由专门路径维护，不覆盖）
        if (!_listener.IsReady)
        {
            _statusItem.Text = $"● {msg}";
            _statusItem.ForeColor = Color.Gray;
        }
        // 检测 GPU/CPU 设备信息，更新副文案
        if (msg.StartsWith("Qwen3-ASR 使用 "))
        {
            bool isGpu = msg.Contains("GPU");
            _deviceItem.Text      = isGpu ? $"  {msg[12..]}" : "  CPU 运行（建议配置 GPU）";
            _deviceItem.ForeColor = isGpu ? Color.FromArgb(16, 150, 24) : Color.OrangeRed;
            _deviceItem.Visible   = true;
        }
        if (msg.Contains("下载") || msg.Contains("失败") || msg.Contains("就绪"))
            ShowTip(AppName, msg, ColorInfo);
    });

    private void OnWakeWord() => RunOnUI(() =>
    {
        System.Media.SystemSounds.Asterisk.Play();
        ShowTip(AppName, "我在听，请说...", ColorSuccess);
    });

    private void OnInputWakeWord() => RunOnUI(() =>
    {
        System.Media.SystemSounds.Asterisk.Play();
        ShowTip("输入模式", "请说出要输入的内容...", ColorPaste);
    });

    private void OnInputCommand(string text) => RunOnUI(() =>
    {
        Clipboard.SetText(text);
        Thread.Sleep(80);
        InputHelper.SimulateCtrlV();
        ShowTip("已粘贴", text, ColorPaste, 2000);
    });

    private void OnCommand(string text) => RunOnUI(() =>
    {
        ShowTip("识别结果", text, ColorSuccess);

        if (!string.IsNullOrWhiteSpace(_settings.Command))
            ExecuteCommand(_settings.Command.Replace("{content}", text));
    });

    private void ExecuteCommand(string fullCmd)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Logger.Log("CMD", "执行命令", fullCmd);
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {fullCmd}")
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding  = System.Text.Encoding.UTF8
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var output = await proc.StandardOutput.ReadToEndAsync();
                var error  = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(output))
                    Logger.Log("CMD", "命令输出", output.Trim());
                if (!string.IsNullOrWhiteSpace(error))
                    Logger.Log("ERROR", "命令错误", error.Trim());
            }
            catch (Exception ex)
            {
                Logger.Log("ERROR", "命令执行失败", ex.Message);
                RunOnUI(() => ShowTip("命令执行失败", ex.Message, ColorError, 5000, "ERROR"));
            }
        });
    }

    private void OnError(string msg) => RunOnUI(() =>
        ShowTip("错误", msg, ColorError, 5000, "ERROR"));

    private void Quit()
    {
        Logger.Log("INFO", "退出", "程序正常退出");
        _listener.Stop();
        _tray.Visible = false;
        Application.Exit();
    }
}
