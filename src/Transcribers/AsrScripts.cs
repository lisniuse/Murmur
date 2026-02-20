namespace VoiceAssistant;

/// <summary>
/// ASR Python 脚本的嵌入式源码。
/// 检测到文件缺失时自动写出，使程序无需依赖 asr/ 目录预先存在。
/// </summary>
internal static class AsrScripts
{
    private static readonly string AsrDir =
        Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
            "asr");

    public static string ScriptPath => Path.Combine(AsrDir, "asr_server.py");

    /// <summary>
    /// 确保 asr/ 目录和所有脚本文件存在，缺失则自动创建。
    /// pythonPath 用于生成 install.bat 中的 Python 路径。
    /// </summary>
    public static void EnsureFiles(string pythonPath)
    {
        Directory.CreateDirectory(AsrDir);
        // 脚本和依赖文件始终覆盖（保持与嵌入版本同步）
        Write("asr_server.py",    Asr_Server_Py);
        Write("requirements.txt", Requirements_Txt);
        // install.bat 只在缺失时写（用户可能已自定义 Python 路径）
        WriteIfMissing("install.bat", Install_Bat.Replace("{PYTHON_PATH}", pythonPath));
    }

    private static void Write(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(AsrDir, fileName), content, System.Text.Encoding.UTF8);
    }

    private static void WriteIfMissing(string fileName, string content)
    {
        var path = Path.Combine(AsrDir, fileName);
        if (!File.Exists(path))
            File.WriteAllText(path, content, System.Text.Encoding.UTF8);
    }

    private const string Requirements_Txt = "qwen-asr\n";

    private const string Install_Bat =
"""
@echo off
chcp 65001 >nul
echo ============================================
echo  Qwen3-ASR 依赖安装
echo ============================================
echo.

set PYTHON={PYTHON_PATH}

if not exist "%PYTHON%" (
    echo [错误] 找不到 Python: %PYTHON%
    echo 请修改 install.bat 中的 PYTHON 路径
    pause
    exit /b 1
)

echo Python 路径: %PYTHON%
echo.
echo 正在安装 qwen-asr ...
"%PYTHON%" -m pip install -U qwen-asr

echo.
echo ============================================
echo  安装完成！首次运行时会自动下载模型（约 3GB）
echo ============================================
pause
""";

    private const string Asr_Server_Py =
""""
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Qwen3-ASR 语音识别服务 —— 与 C# 通过 stdin/stdout 通信

用法: python asr_server.py [hf_cache_dir]
  hf_cache_dir: HuggingFace 模型缓存目录（可选，默认 ~/.cache/huggingface）

协议：
  启动后输出：
    LOADING          → 模型加载中
    MODEL_READY      → 就绪，可接受请求
    ERROR:<msg>      → 初始化失败

  接受输入（每行一条）：
    <WAV 文件路径>   → 识别该文件
    EXIT             → 退出进程

  每次识别后输出：
    OK:<识别文本>
    ERROR:<错误信息>
"""

import sys
import os
import time

# 必须在任何 HuggingFace/transformers import 之前设置缓存目录
if len(sys.argv) > 1:
    _hf_home = sys.argv[1]
    os.environ["HF_HOME"]               = _hf_home
    os.environ["TRANSFORMERS_CACHE"]    = os.path.join(_hf_home, "hub")
    os.environ["HUGGINGFACE_HUB_CACHE"] = os.path.join(_hf_home, "hub")


def main():
    print("LOADING", flush=True)

    try:
        import torch
        from qwen_asr import Qwen3ASRModel
    except ImportError as e:
        print(f"ERROR:缺少依赖，请先运行 install.bat: {e}", flush=True)
        sys.exit(1)

    device = "cuda:0" if torch.cuda.is_available() else "cpu"
    dtype  = torch.bfloat16 if torch.cuda.is_available() else torch.float32
    print(f"DEVICE:{device}", flush=True)

    try:
        model = Qwen3ASRModel.from_pretrained(
            "Qwen/Qwen3-ASR-1.7B",
            dtype=dtype,
            device_map=device,
            max_inference_batch_size=1,
            max_new_tokens=256,
        )
    except Exception as e:
        print(f"ERROR:模型加载失败: {e}", flush=True)
        sys.exit(1)

    print("MODEL_READY", flush=True)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        if line == "EXIT":
            break

        try:
            t0 = time.time()
            results = model.transcribe(audio=line, language="Chinese")
            elapsed_ms = int((time.time() - t0) * 1000)
            text = results[0].text.strip() if results else ""
            print(f"TIMING:{elapsed_ms}", flush=True)
            print(f"OK:{text}", flush=True)
        except Exception as e:
            print(f"ERROR:{e}", flush=True)

        # 删除临时文件（C# 侧创建的）
        try:
            if os.path.exists(line) and "qwen_tmp_" in os.path.basename(line):
                os.remove(line)
        except Exception:
            pass


if __name__ == "__main__":
    main()
"""";
}
