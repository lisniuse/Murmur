using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceAssistant;

public class WhisperTranscriber : IDisposable
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    private static readonly string ModelDir = Path.Combine(ProjectPaths.Models, "whisper");

    public event Action<string>? OnStatus;

    // modelName: "tiny" / "base" / "small"
    public async Task InitAsync(string modelName = "base")
    {
        Directory.CreateDirectory(ModelDir);

        // 释放旧实例（热切换模型时）
        _processor?.Dispose();
        _factory?.Dispose();
        _processor = null;
        _factory = null;

        var (ggmlType, sizeMb, label) = modelName switch
        {
            "tiny"  => (GgmlType.Tiny,  75,  "tiny（约 75MB）"),
            "small" => (GgmlType.Small, 480, "small（约 480MB）"),
            _       => (GgmlType.Base,  150, "base（约 150MB）"),
        };

        var modelPath = Path.Combine(ModelDir, $"ggml-{ggmlType.ToString().ToLower()}.bin");

        if (!File.Exists(modelPath))
        {
            OnStatus?.Invoke($"正在下载 Whisper {label} 模型，请稍候...");
            try
            {
                var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
                using var fileStream = File.Create(modelPath);
                await modelStream.CopyToAsync(fileStream);
                OnStatus?.Invoke("模型下载完成，正在加载...");
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke($"模型下载失败: {ex.Message}");
                throw;
            }
        }

        _factory = WhisperFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("zh")
            .WithPrompt("以下是普通话的句子，请使用简体中文。")
            .WithSingleSegment()
            .Build();

        OnStatus?.Invoke("Whisper 已就绪");
    }

    public async Task<string> TranscribeAsync(MemoryStream wavStream)
    {
        if (_processor == null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        await foreach (var segment in _processor.ProcessAsync(wavStream))
        {
            sb.Append(segment.Text);
        }
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
