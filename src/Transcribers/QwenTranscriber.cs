using System.Diagnostics;

namespace VoiceAssistant;

/// <summary>
/// 通过常驻 Python 子进程调用 Qwen3-ASR-1.7B 进行语音识别。
/// 协议：stdin 发 WAV 路径 → stdout 读 OK:<文本> 或 ERROR:<msg>
/// </summary>
public class QwenTranscriber : IDisposable
{
    private Process?          _process;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static string ScriptPath => AsrScripts.ScriptPath;

    private static readonly string HfCacheDir =
        Path.Combine(ProjectPaths.Models, "qwen");

    public event Action<string>? OnStatus;

    public string PythonExe { get; set; } = @"D:\dev\tools\python\python.exe";
    public bool IsRunning => _process != null && !_process.HasExited;

    public async Task InitAsync()
    {
        // 释放旧进程
        DisposeProcess();

        // 确保 asr/ 脚本文件存在（首次运行自动从嵌入源码写出）
        AsrScripts.EnsureFiles(PythonExe);

        if (!File.Exists(PythonExe))
            throw new FileNotFoundException($"找不到 Python: {PythonExe}");
        if (!File.Exists(ScriptPath))
            throw new FileNotFoundException($"找不到 ASR 脚本: {ScriptPath}");

        // .bat 文件需要通过 cmd.exe 启动，否则无法重定向 stdin/stdout
        // cmd.exe /c 的引号规则：整个命令需要再套一层外引号
        //   正确: cmd /c ""bat路径" -u "脚本路径""
        //   错误: cmd /c "bat路径" -u "脚本路径"  ← -u 会被 cmd 当成自身参数
        var isBat = PythonExe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        string exe, args;
        if (isBat)
        {
            exe  = "cmd.exe";
            args = $"/c \"\"{PythonExe}\" -u \"{ScriptPath}\" \"{HfCacheDir}\"\"";
        }
        else
        {
            exe  = PythonExe;
            args = $"-u \"{ScriptPath}\" \"{HfCacheDir}\"";
        }

        // cmd.exe 的错误信息使用系统 OEM 编码（中文 Windows 为 GBK）
        var stderrEnc = isBat
            ? System.Text.Encoding.GetEncoding(
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            : System.Text.Encoding.UTF8;

        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding  = stderrEnc,
        };
        // 强制 Python 以 UTF-8 输出（通过 bat 启动时也生效）
        psi.Environment["PYTHONUTF8"]       = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        // 将 HuggingFace 模型缓存重定向到项目目录
        Directory.CreateDirectory(HfCacheDir);
        psi.Environment["HF_HOME"] = HfCacheDir;

        _process = Process.Start(psi) ?? throw new Exception("无法启动 Python 进程");

        // 等待 Python 输出 LOADING / MODEL_READY / ERROR
        // 用流结束（null）而非 HasExited 判断，避免竞态
        OnStatus?.Invoke("正在启动 Qwen3-ASR...");
        string? line;
        while ((line = await _process.StandardOutput.ReadLineAsync()) != null)
        {
            if (line == "LOADING")
            {
                OnStatus?.Invoke("正在加载 Qwen3-ASR 模型...");
            }
            else if (line.StartsWith("DEVICE:"))
            {
                var device = line[7..];
                OnStatus?.Invoke(device.StartsWith("cuda") ? $"Qwen3-ASR 使用 GPU（{device}）" : "Qwen3-ASR 使用 CPU（未检测到 GPU）");
            }
            else if (line == "MODEL_READY")
            {
                OnStatus?.Invoke("Qwen3-ASR 已就绪");
                return;
            }
            else if (line.StartsWith("ERROR:"))
            {
                throw new Exception(line[6..]);
            }
        }

        // 流结束但未收到 MODEL_READY → 进程异常退出，读 stderr
        var stderr = await _process.StandardError.ReadToEndAsync();
        throw new Exception(string.IsNullOrWhiteSpace(stderr)
            ? "Qwen3-ASR 进程意外退出（请先运行 install.bat 安装依赖）"
            : $"Qwen3-ASR 启动失败: {stderr.Trim()}");
    }

    public async Task<string> TranscribeAsync(MemoryStream wavStream)
    {
        if (_process == null || _process.HasExited)
            throw new InvalidOperationException("Qwen3-ASR 进程未运行");

        await _lock.WaitAsync();
        try
        {
            // 写入临时 WAV 文件
            var tmpPath = Path.Combine(
                Path.GetTempPath(),
                $"qwen_tmp_{Guid.NewGuid():N}.wav");

            var audioBytes = wavStream.ToArray();
            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            await File.WriteAllBytesAsync(tmpPath, audioBytes);

            try
            {
                // 发送路径给 Python
                await _process.StandardInput.WriteLineAsync(tmpPath);
                await _process.StandardInput.FlushAsync();

                // 读取识别结果
                int pythonMs = -1;
                while (true)
                {
                    var line = await _process.StandardOutput.ReadLineAsync();
                    if (line == null)
                        throw new Exception("Qwen3-ASR 进程意外退出");

                    if (line.StartsWith("TIMING:") && int.TryParse(line[7..], out var ms))
                    {
                        pythonMs = ms;
                    }
                    else if (line.StartsWith("OK:"))
                    {
                        swTotal.Stop();
                        var audioSec = audioBytes.Length / (16000.0 * 2);
                        Logger.Log("PERF", "Qwen 识别耗时",
                            $"音频时长 {audioSec:F1}s | Python 推理 {pythonMs}ms | 总耗时 {swTotal.ElapsedMilliseconds}ms");
                        return line[3..].Trim();
                    }
                    else if (line.StartsWith("ERROR:"))
                    {
                        throw new Exception(line[6..]);
                    }
                }
            }
            finally
            {
                // Python 侧会尝试删除，这里兜底
                try { File.Delete(tmpPath); } catch { }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void DisposeProcess()
    {
        if (_process == null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("EXIT");
                _process.WaitForExit(2000);
            }
        }
        catch { }
        try { _process.Kill(); } catch { }
        _process.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        DisposeProcess();
        _lock.Dispose();
    }
}
