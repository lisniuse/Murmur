using System.Text.Json;

namespace VoiceAssistant;

public class AppSettings
{
    public string WakeWord { get; set; } = "小助手";
    public string InputWakeWord { get; set; } = "小助手帮我输入";
    public string Command { get; set; } = ""; // 唤醒后执行的命令，{content} 会被替换为识别内容
    // "Whisper" / "SystemSpeech" / "Qwen"
    public string RecognitionMode { get; set; } = "SystemSpeech";
    // Whisper 模型大小："tiny" / "base" / "small"
    public string WhisperModel { get; set; } = "base";
    // Qwen3-ASR Python 可执行文件路径（支持 .exe 或 .bat）
    public string PythonPath { get; set; } = @"D:\dev\tools\python\bin\python.bat";
    // 模型存放目录，留空则自动使用 EXE 同目录下的 models/
    public string ModelsPath { get; set; } = "";

    private static readonly string SettingsPath = ProjectPaths.Settings;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }

        // 首次运行：生成默认配置文件
        var defaults = new AppSettings();
        try { defaults.Save(); } catch { }
        return defaults;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
