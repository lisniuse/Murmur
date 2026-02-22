using System.Speech.Recognition;
using NAudio.Wave;

namespace VoiceAssistant;

/// <summary>
/// 混合架构：
///   System.Speech → 轻量唤醒词检测（一直运行）
///   NAudio        → 唤醒后录音
///   Whisper.net 或 System.Speech DictationGrammar → 语音转文字
/// </summary>
public class VoiceListener : IDisposable
{
    private SpeechRecognitionEngine? _wakeEngine;
    private readonly AudioRecorder      _recorder    = new();
    private readonly WhisperTranscriber _transcriber = new();
    private readonly QwenTranscriber    _qwen        = new();

    private string[] _wakeWords = ["小助手"];
    private string[] _inputWakeWords = ["小助手帮我输入"];
    private AppSettings _settings = new();

    private bool _processing = false;
    private bool _paused     = false;
    private bool _ready      = false;

    public bool IsPaused => _paused;
    public bool IsReady  => _ready;

    public void Pause()
    {
        _paused = true;
        StopWakeEngine();
        OnStatus?.Invoke("已暂停监听");
    }

    public void Resume()
    {
        _paused = false;
        StartWakeEngine();
    }

    // 事件
    public event Action? OnWakeWord;
    public event Action<string>? OnCommand;
    public event Action? OnInputWakeWord;
    public event Action<string>? OnInputCommand;
    public event Action<string>? OnError;
    public event Action<string>? OnStatus;

    public VoiceListener()
    {
        // 订阅子组件状态事件（只订阅一次，避免 Reload 时重复叠加）
        _transcriber.OnStatus += msg => OnStatus?.Invoke(msg);
        _qwen.OnStatus        += msg => OnStatus?.Invoke(msg);
    }

    public async Task StartAsync(AppSettings settings)
    {
        _settings = settings;
        _wakeWords = Parse(settings.WakeWord);
        _inputWakeWords = Parse(settings.InputWakeWord);

        if (settings.RecognitionMode == "Whisper")
        {
            try
            {
                await _transcriber.InitAsync(settings.WhisperModel);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Whisper 初始化失败: {ex.Message}");
                return;
            }
        }
        else if (settings.RecognitionMode == "Qwen")
        {
            _qwen.PythonExe = settings.PythonPath;
            try
            {
                await _qwen.InitAsync();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Qwen3-ASR 初始化失败: {ex.Message}");
                return;
            }
        }

        _ready = true;
        StartWakeEngine();
    }

    public async Task ReloadAsync(AppSettings settings)
    {
        var oldMode  = _settings.RecognitionMode;
        var oldModel = _settings.WhisperModel;

        StopWakeEngine();
        _settings = settings;
        _wakeWords = Parse(settings.WakeWord);
        _inputWakeWords = Parse(settings.InputWakeWord);
        await Task.Delay(200);

        // 如果切换到 Whisper 或更换了模型，重新初始化
        if (settings.RecognitionMode == "Whisper" &&
            (oldMode != "Whisper" || oldModel != settings.WhisperModel))
        {
            try
            {
                await _transcriber.InitAsync(settings.WhisperModel);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Whisper 初始化失败: {ex.Message}");
                return;
            }
        }
        else if (settings.RecognitionMode == "Qwen")
        {
            // 仅在以下情况才重启 Python 进程（避免每次保存设置都重载模型）：
            // 1. 从其他模式切换过来  2. Python 路径变了  3. 进程已意外退出
            bool needReinit = oldMode != "Qwen"
                           || _qwen.PythonExe != settings.PythonPath
                           || !_qwen.IsRunning;
            _qwen.PythonExe = settings.PythonPath;
            if (needReinit)
            {
                try
                {
                    await _qwen.InitAsync();
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Qwen3-ASR 初始化失败: {ex.Message}");
                    return;
                }
            }
        }

        StartWakeEngine();
    }

    private void StartWakeEngine()
    {
        if (_paused) return;   // 暂停状态下不重启
        try
        {
            _wakeEngine = new SpeechRecognitionEngine();

            // 唤醒词语法（只需要固定词，不用 DictationGrammar）
            var allWords = _wakeWords.Concat(_inputWakeWords).ToArray();
            var grammar = new GrammarBuilder();
            grammar.Append(new Choices(allWords));
            _wakeEngine.LoadGrammar(new Grammar(grammar));

            _wakeEngine.SpeechRecognized += OnWakeWordDetected;
            _wakeEngine.SetInputToDefaultAudioDevice();
            _wakeEngine.RecognizeAsync(RecognizeMode.Multiple);

            OnStatus?.Invoke("麦克风就绪，正在监听唤醒词...");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"唤醒词引擎启动失败: {ex.Message}");
        }
    }

    private async void OnWakeWordDetected(object? sender, SpeechRecognizedEventArgs e)
    {
        if (_processing) return;

        // 置信度低于阈值时忽略，避免误触发
        const float ConfidenceThreshold = 0.85f;
        Logger.Log("WAKE", "置信度", $"{e.Result.Confidence:F2} / 词: {e.Result.Text}");
        if (e.Result.Confidence < ConfidenceThreshold) return;

        _processing = true;

        var word = e.Result.Text.Trim();
        bool isInputMode = _inputWakeWords.Contains(word);

        if (isInputMode) OnInputWakeWord?.Invoke();
        else OnWakeWord?.Invoke();

        // 停止唤醒引擎，释放麦克风给 NAudio
        StopWakeEngine();
        await Task.Delay(150); // 等待麦克风释放

        try
        {
            string text;
            if (_settings.RecognitionMode == "SystemSpeech")
            {
                OnStatus?.Invoke("识别中（System.Speech）...");
                text = await RecognizeWithSystemSpeechAsync();
            }
            else if (_settings.RecognitionMode == "Qwen")
            {
                OnStatus?.Invoke("录音中，请说话...");
                var devNum = AudioRecorder.ResolveDeviceNumber(_settings.MicrophoneDeviceName);
                var swRec = System.Diagnostics.Stopwatch.StartNew();
                using var wavStream = await _recorder.RecordUntilSilenceAsync(devNum);
                swRec.Stop();
                Logger.Log("PERF", "录音时长", $"{swRec.ElapsedMilliseconds}ms");
                OnStatus?.Invoke("识别中（Qwen3-ASR）...");
                text = await _qwen.TranscribeAsync(wavStream);
            }
            else
            {
                // 默认 Whisper
                OnStatus?.Invoke("录音中，请说话...");
                var devNum = AudioRecorder.ResolveDeviceNumber(_settings.MicrophoneDeviceName);
                using var wavStream = await _recorder.RecordUntilSilenceAsync(devNum);
                OnStatus?.Invoke("识别中（Whisper）...");
                text = await _transcriber.TranscribeAsync(wavStream);
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (isInputMode) OnInputCommand?.Invoke(text);
                else OnCommand?.Invoke(text);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"识别出错: {ex.Message}");
        }
        finally
        {
            _processing = false;
            // 重新启动唤醒监听
            StartWakeEngine();
        }
    }

    /// <summary>
    /// 使用 System.Speech DictationGrammar 识别一句话（有静音自动停止）
    /// </summary>
    private Task<string> RecognizeWithSystemSpeechAsync()
    {
        var tcs = new TaskCompletionSource<string>();
        SpeechRecognitionEngine? dictEngine = null;
        try
        {
            dictEngine = new SpeechRecognitionEngine();
            dictEngine.LoadGrammar(new DictationGrammar());
            dictEngine.SetInputToDefaultAudioDevice();

            // EndSilenceTimeout：静音多久停止识别
            dictEngine.EndSilenceTimeout = TimeSpan.FromSeconds(1.5);
            dictEngine.BabbleTimeout     = TimeSpan.FromSeconds(0);
            dictEngine.InitialSilenceTimeout = TimeSpan.FromSeconds(8);

            dictEngine.SpeechRecognized += (_, e) =>
            {
                var result = e.Result?.Text ?? string.Empty;
                dictEngine.RecognizeAsyncStop();
                tcs.TrySetResult(result);
            };
            dictEngine.RecognizeCompleted += (_, e) =>
            {
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(e.Result?.Text ?? string.Empty);
                dictEngine.Dispose();
            };

            dictEngine.RecognizeAsync(RecognizeMode.Single);
        }
        catch (Exception ex)
        {
            dictEngine?.Dispose();
            tcs.TrySetException(ex);
        }
        return tcs.Task;
    }

    private void StopWakeEngine()
    {
        try
        {
            _wakeEngine?.RecognizeAsyncCancel();
            _wakeEngine?.Dispose();
            _wakeEngine = null;
        }
        catch { }
    }

    private static string[] Parse(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void Stop() => StopWakeEngine();

    public void Dispose()
    {
        StopWakeEngine();
        _transcriber.Dispose();
        _qwen.Dispose();
    }
}
