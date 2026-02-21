using NAudio.Wave;

namespace VoiceAssistant;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _wakeWordBox;
    private readonly TextBox _inputWakeWordBox;
    private readonly TextBox _commandBox;
    private readonly RadioButton _rbWhisper;
    private readonly RadioButton _rbSystemSpeech;
    private readonly RadioButton _rbQwen;
    private readonly ComboBox _modelBox;
    private readonly TextBox _pythonPathBox;
    private readonly TextBox _modelsPathBox;
    private readonly ComboBox _micDeviceBox;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "语音助手 - 设置";
        Size = new Size(420, 648);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        int x = 20, w = 365;

        // 唤醒词
        Add(new Label { Text = "唤醒词：", Location = new Point(x, 16), AutoSize = true });
        _wakeWordBox = new TextBox { Text = _settings.WakeWord, Location = new Point(x, 34), Width = w };
        Add(_wakeWordBox);
        AddHint("多个唤醒词用逗号分隔，例如：贾维斯,小助手", x, 57);

        // 输入唤醒词
        Add(new Label { Text = "输入唤醒词：", Location = new Point(x, 82), AutoSize = true });
        _inputWakeWordBox = new TextBox { Text = _settings.InputWakeWord, Location = new Point(x, 100), Width = w };
        Add(_inputWakeWordBox);
        AddHint("说这句话后，接着说的内容会粘贴到光标处", x, 123);

        // 执行命令
        Add(new Label { Text = "唤醒后执行命令：", Location = new Point(x, 148), AutoSize = true });
        _commandBox = new TextBox
        {
            Text      = _settings.Command,
            Location  = new Point(x, 166),
            Width     = w,
            Multiline = true,
            Height    = 46,
            ScrollBars = ScrollBars.Vertical
        };
        Add(_commandBox);
        AddHint("{content} 会被替换为识别到的说话内容，留空则不执行命令", x, 217);

        // 模型存放目录
        Add(new Label { Text = "模型存放目录：", Location = new Point(x, 232), AutoSize = true });
        _modelsPathBox = new TextBox
        {
            Text     = _settings.ModelsPath,
            Location = new Point(x, 250),
            Width    = w - 86
        };
        var modelsPathBtn = new Button { Text = "浏览...", Location = new Point(x + w - 80, 249), Width = 75 };
        modelsPathBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                SelectedPath = string.IsNullOrWhiteSpace(_modelsPathBox.Text)
                    ? ProjectPaths.Models : _modelsPathBox.Text
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                _modelsPathBox.Text = dlg.SelectedPath;
        };
        Add(_modelsPathBox);
        Add(modelsPathBtn);
        AddHint("留空则自动使用 EXE 同目录下的 models/ 文件夹", x, 273);

        // 麦克风设备
        Add(new Label { Text = "麦克风设备：", Location = new Point(x, 290), AutoSize = true });
        _micDeviceBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(x, 308),
            Width         = w - 86
        };
        _micDeviceBox.Items.Add("默认（系统）");
        for (int i = 0; i < WaveIn.DeviceCount; i++)
            _micDeviceBox.Items.Add(WaveIn.GetCapabilities(i).ProductName);
        // 还原已保存的设备选择
        {
            int idx = 0;
            if (!string.IsNullOrEmpty(_settings.MicrophoneDeviceName))
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                    if (WaveIn.GetCapabilities(i).ProductName == _settings.MicrophoneDeviceName)
                    { idx = i + 1; break; }
            _micDeviceBox.SelectedIndex = idx;
        }
        Add(_micDeviceBox);
        var testMicBtn = new Button { Text = "测试", Location = new Point(x + w - 80, 307), Width = 75 };
        testMicBtn.Click += (_, _) =>
        {
            int devNum = _micDeviceBox.SelectedIndex <= 0 ? 0 : _micDeviceBox.SelectedIndex - 1;
            using var dlg = new MicTestForm(devNum);
            dlg.ShowDialog(this);
        };
        Add(testMicBtn);

        // 识别引擎选择
        var engineGroup = new GroupBox
        {
            Text     = "语音识别引擎",
            Location = new Point(x, 360),
            Size     = new Size(w, 192)
        };

        _rbWhisper = new RadioButton
        {
            Text     = "Whisper 模型（准确，离线，需下载）",
            Location = new Point(10, 20),
            AutoSize = true,
            Checked  = _settings.RecognitionMode == "Whisper"
        };
        // Whisper 模型大小（缩进在 Whisper 下方）
        var modelLabel = new Label { Text = "Whisper 模型大小：", Location = new Point(28, 46), AutoSize = true };
        _modelBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location      = new Point(148, 43),
            Width         = 110,
            Enabled       = _settings.RecognitionMode == "Whisper"
        };
        _modelBox.Items.AddRange(["tiny (75MB)", "base (150MB)", "small (480MB)"]);
        _modelBox.SelectedIndex = _settings.WhisperModel switch
        {
            "tiny"  => 0,
            "small" => 2,
            _       => 1
        };

        _rbSystemSpeech = new RadioButton
        {
            Text     = "System.Speech（快速，无需下载）",
            Location = new Point(10, 72),
            AutoSize = true,
            Checked  = _settings.RecognitionMode == "SystemSpeech"
        };
        _rbQwen = new RadioButton
        {
            Text     = "Qwen3-ASR-1.7B（最准确，需 GPU + Python）",
            Location = new Point(10, 98),
            AutoSize = true,
            Checked  = _settings.RecognitionMode == "Qwen"
        };

        void UpdateModelBox() => _modelBox.Enabled = _rbWhisper.Checked;
        _rbWhisper.CheckedChanged      += (_, _) => UpdateModelBox();
        _rbSystemSpeech.CheckedChanged += (_, _) => UpdateModelBox();
        _rbQwen.CheckedChanged         += (_, _) => UpdateModelBox();

        engineGroup.Controls.Add(_rbWhisper);
        engineGroup.Controls.Add(modelLabel);
        engineGroup.Controls.Add(_modelBox);
        engineGroup.Controls.Add(_rbSystemSpeech);
        engineGroup.Controls.Add(_rbQwen);
        // 提示 label 加到 engineGroup
        var qwenHint = new Label
        {
            Text      = "首次使用需运行 asr\\install.bat 并自动下载模型（约 3GB）",
            Location  = new Point(28, 122),
            AutoSize  = true,
            ForeColor = Color.Gray,
            Font      = new Font(Font.FontFamily, 8)
        };
        engineGroup.Controls.Add(qwenHint);

        // Python 路径（Qwen 专用）
        var pyLabel = new Label { Text = "Python 路径：", Location = new Point(28, 140), AutoSize = true };
        _pythonPathBox = new TextBox
        {
            Text     = _settings.PythonPath,
            Location = new Point(28, 158),
            Width    = w - 38,
        };
        engineGroup.Controls.Add(pyLabel);
        engineGroup.Controls.Add(_pythonPathBox);
        Add(engineGroup);

        // 按钮
        var saveBtn   = new Button { Text = "保存", Location = new Point(200, 576), Width = 85, DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = "取消", Location = new Point(300, 576), Width = 85, DialogResult = DialogResult.Cancel };
        saveBtn.Click += OnSave;
        Add(saveBtn);
        Add(cancelBtn);
        AcceptButton = saveBtn;
        CancelButton = cancelBtn;
    }

    private void Add(Control c) => Controls.Add(c);

    private void AddHint(string text, int x, int y) =>
        Controls.Add(new Label
        {
            Text      = text,
            Location  = new Point(x, y),
            AutoSize  = true,
            ForeColor = Color.Gray,
            Font      = new Font(Font.FontFamily, 8)
        });

    private void OnSave(object? sender, EventArgs e)
    {
        var wakeWord = _wakeWordBox.Text.Trim();
        var inputWakeWord = _inputWakeWordBox.Text.Trim();

        if (string.IsNullOrEmpty(wakeWord) || string.IsNullOrEmpty(inputWakeWord))
        {
            MessageBox.Show("唤醒词不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _settings.WakeWord      = wakeWord;
        _settings.InputWakeWord = inputWakeWord;
        _settings.Command       = _commandBox.Text.Trim();
        _settings.RecognitionMode = _rbSystemSpeech.Checked ? "SystemSpeech"
                                  : _rbQwen.Checked        ? "Qwen"
                                                           : "Whisper";
        _settings.WhisperModel = _modelBox.SelectedIndex switch
        {
            0 => "tiny",
            2 => "small",
            _ => "base"
        };
        _settings.PythonPath  = _pythonPathBox.Text.Trim();
        _settings.ModelsPath  = _modelsPathBox.Text.Trim();
        _settings.MicrophoneDeviceName = _micDeviceBox.SelectedIndex <= 0
            ? ""
            : _micDeviceBox.Items[_micDeviceBox.SelectedIndex]?.ToString() ?? "";
        _settings.Save();
    }
}
