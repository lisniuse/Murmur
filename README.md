# Murmur

唤醒词驱动的 Windows 桌面语音助手，常驻系统托盘，说一句话即可触发识别或自动输入文字。

## 功能

- **唤醒词监听** — 使用 System.Speech 轻量引擎持续监听，不占用 Whisper / Qwen 资源
- **语音识别** — 支持三种引擎，按需选择
- **输入模式** — 说出唤醒词后，识别结果自动粘贴到当前光标处
- **自定义命令** — 识别结果可传入任意 shell 命令（`{content}` 占位符）
- **开机自启** — 一键写入注册表

## 识别引擎

| 引擎 | 说明 | 适合场景 |
|---|---|---|
| **System.Speech** | Windows 内置，无需额外模型，中文识别率较低 | 快速体验 |
| **Whisper** | OpenAI 离线模型，中文效果好，纯本地运行 | 日常使用 |
| **Qwen3-ASR** | 阿里 Qwen3-ASR-1.7B，效果最佳，需 Python 环境 | 高精度场景 |

## 环境要求

- Windows 10 / 11 x64
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)（运行发布版本需要）
- Python 3.10+（仅 Qwen3-ASR 模式需要）

## 快速开始

### 运行开发版

```bash
cd src
dotnet run
```

首次运行会在项目根目录自动生成 `settings.json`。

### 发布单文件 EXE

```bash
cd src
dotnet publish -c Release
```

输出在 `output/prd/VoiceAssistant.exe`，将整个 `output/prd/` 目录复制到目标机器即可运行。

## 目录结构

```
jws-test/
  src/                  源码
    Core/               录音与唤醒逻辑
    Transcribers/       Whisper / Qwen3-ASR 识别器
    UI/                 托盘、设置窗口、Toast
    Infra/              配置、路径、日志、开机自启
  models/               模型文件（不纳入 git）
    whisper/            Whisper 模型（.bin）
    qwen/               Qwen3-ASR 模型缓存
  output/               编译产物（不纳入 git）
    debug/              dotnet run / Debug 构建
    prd/                dotnet publish 发布产物
  settings.json         运行时配置（不纳入 git）
  app.log               运行日志（不纳入 git）
```

## 配置说明

首次启动自动生成 `settings.json`，也可通过托盘菜单 **设置** 修改：

```json
{
  "WakeWord": "小助手",
  "InputWakeWord": "小助手帮我输入",
  "Command": "",
  "RecognitionMode": "SystemSpeech",
  "WhisperModel": "base",
  "PythonPath": "D:\\dev\\tools\\python\\python.exe",
  "ModelsPath": ""
}
```

| 字段 | 说明 |
|---|---|
| `WakeWord` | 唤醒词，多个用逗号分隔 |
| `InputWakeWord` | 输入模式唤醒词，识别结果自动粘贴 |
| `Command` | 识别后执行的命令，`{content}` 替换为识别文字 |
| `RecognitionMode` | `SystemSpeech` / `Whisper` / `Qwen` |
| `WhisperModel` | `tiny` / `base` / `small`（模型越大越准但越慢） |
| `PythonPath` | Python 可执行文件路径，支持 `.exe` 或 `.bat` |
| `ModelsPath` | 模型目录，留空自动使用项目根目录下的 `models/` |

## Whisper 模型

将下载好的 Whisper 模型文件（`.bin`）放入 `models/whisper/` 目录，文件名格式：

```
models/whisper/ggml-base.bin
models/whisper/ggml-small.bin
```

可从 [ggerganov/whisper.cpp](https://github.com/ggerganov/whisper.cpp) 的 Releases 页面下载。

## Qwen3-ASR 模式

首次启动 Qwen 模式时，程序会在 EXE 同目录下自动创建 `asr/` 文件夹并写出 `install.bat`，运行该脚本安装 Python 依赖：

```
asr/install.bat
```

模型（约 3 GB）首次识别时自动从 HuggingFace 下载，缓存到 `models/qwen/`。Python 进程常驻，不会因保存设置而重启。

## 构建

```bash
# Debug（开发调试）
dotnet build -c Debug

# Release 单文件发布
dotnet publish -c Release
```

编译产物统一输出到 `output/`，不污染 `src/` 目录。
