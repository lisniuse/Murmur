using NAudio.Wave;

namespace VoiceAssistant;

/// <summary>
/// 麦克风测试对话框：录音 3 秒 → 实时音量显示 → 自动回放
/// </summary>
public class MicTestForm : Form
{
    private readonly int _deviceNumber;
    private WaveInEvent? _waveIn;
    private MemoryStream _recordedMs = new();
    private WaveFileWriter? _writer;

    private readonly ProgressBar _volumeBar;
    private readonly Label _statusLabel;
    private readonly Button _closeBtn;

    private int _countdown = 3;
    private System.Windows.Forms.Timer? _countdownTimer;

    public MicTestForm(int deviceNumber)
    {
        _deviceNumber = deviceNumber;

        Text = "麦克风测试";
        Size = new Size(360, 185);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        Controls.Add(new Label
        {
            Text      = "音量：",
            Location  = new Point(20, 22),
            AutoSize  = true
        });

        _volumeBar = new ProgressBar
        {
            Location = new Point(68, 18),
            Size     = new Size(252, 24),
            Maximum  = 100,
            Value    = 0,
            Style    = ProgressBarStyle.Continuous
        };
        Controls.Add(_volumeBar);

        _statusLabel = new Label
        {
            Text     = "准备录音...",
            Location = new Point(20, 56),
            Size     = new Size(300, 20)
        };
        Controls.Add(_statusLabel);

        _closeBtn = new Button
        {
            Text     = "关闭",
            Location = new Point(130, 100),
            Width    = 100,
            Enabled  = false
        };
        _closeBtn.Click += (_, _) => Close();
        Controls.Add(_closeBtn);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        StartRecording();
    }

    private void StartRecording()
    {
        _recordedMs = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat         = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50,
            DeviceNumber       = _deviceNumber
        };
        _writer = new WaveFileWriter(_recordedMs, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, ev) =>
        {
            _writer?.Write(ev.Buffer, 0, ev.BytesRecorded);

            // 计算 RMS 用于音量条
            double sum     = 0;
            int    samples = ev.BytesRecorded / 2;
            for (int i = 0; i < ev.BytesRecorded - 1; i += 2)
            {
                double s = BitConverter.ToInt16(ev.Buffer, i) / 32768.0;
                sum += s * s;
            }
            double rms      = samples > 0 ? Math.Sqrt(sum / samples) : 0;
            int    barValue = Math.Min(100, (int)(rms * 500));

            if (!IsDisposed && IsHandleCreated)
                Invoke(() => _volumeBar.Value = barValue);
        };

        _waveIn.StartRecording();

        _countdown = 3;
        UpdateStatus($"录音中... 剩余 {_countdown} 秒");

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (_, _) =>
        {
            _countdown--;
            if (_countdown <= 0)
            {
                _countdownTimer!.Stop();
                StopAndPlay();
            }
            else
            {
                UpdateStatus($"录音中... 剩余 {_countdown} 秒");
            }
        };
        _countdownTimer.Start();
    }

    private void StopAndPlay()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;

        _volumeBar.Value = 0;

        byte[] audioData = _recordedMs.ToArray();

        // WAV 文件头 44 字节，内容不足说明麦克风无输出
        if (audioData.Length <= 44)
        {
            UpdateStatus("未检测到音频，请检查麦克风连接");
            _closeBtn.Enabled = true;
            return;
        }

        UpdateStatus("正在播放...");

        Task.Run(() =>
        {
            try
            {
                using var playMs  = new MemoryStream(audioData);
                using var reader  = new WaveFileReader(playMs);
                using var waveOut = new WaveOutEvent();
                waveOut.Init(reader);
                waveOut.Play();
                while (waveOut.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(50);
            }
            catch { /* 忽略播放错误 */ }
            finally
            {
                if (!IsDisposed && IsHandleCreated)
                    Invoke(() =>
                    {
                        UpdateStatus("测试完成，已播放录音");
                        _closeBtn.Enabled = true;
                    });
            }
        });
    }

    private void UpdateStatus(string msg) => _statusLabel.Text = msg;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _writer?.Dispose();
        base.OnFormClosing(e);
    }
}
