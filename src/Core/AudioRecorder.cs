using NAudio.Wave;

namespace VoiceAssistant;

/// <summary>
/// 唤醒词触发后，用 NAudio 录音直到检测到静音
/// </summary>
public class AudioRecorder
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int    NoiseFloorMs      = 400;   // 录音开头 400ms 用于测量底噪
    private const double MinSpeechThreshold = 0.008; // 有声音阈值的最小值（适配低音量麦克风）
    private const double SpeechMultiplier  = 4.0;  // 说话音量须是底噪的 4 倍
    private const double SilenceRatio      = 0.25; // 静音 = 低于说话峰值的 25%
    private const int    SilenceDurationMs = 800;  // 静音超过 0.8s 停止
    private const int    MaxDurationMs     = 20000; // 最长录制 20s
    private const int    MinSpeechMs       = 300;  // 至少 300ms 说话才开始检测静音

    /// <summary>
    /// 根据设备名称解析 NAudio 设备号（找不到则返回 0 = 系统默认）
    /// </summary>
    public static int ResolveDeviceNumber(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return 0;
        for (int i = 0; i < WaveIn.DeviceCount; i++)
            if (WaveIn.GetCapabilities(i).ProductName == deviceName)
                return i;
        return 0;
    }

    public async Task<MemoryStream> RecordUntilSilenceAsync(int deviceNumber = 0, CancellationToken ct = default)
    {
        var outputMs = new MemoryStream();
        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, Channels),
            BufferMilliseconds = 100,
            DeviceNumber = deviceNumber
        };

        var writer = new WaveFileWriter(outputMs, waveIn.WaveFormat);
        var tcs = new TaskCompletionSource();

        // 阶段一：测量底噪（前 400ms，用户还未开口）
        bool noisePhase     = true;
        double noiseRmsSum  = 0;
        int    noiseCount   = 0;
        double noiseFloor   = MinSpeechThreshold; // 底噪 RMS 均值
        double speechThreshold = MinSpeechThreshold; // 动态有声阈值

        // 阶段二：说话 / 静音检测
        bool hasSpeech = false;
        DateTime? silenceStart      = null;
        DateTime? speechStart       = null;
        DateTime? belowThresholdAt  = null; // 掉到阈值以下的时刻（用于宽限期）
        const int SpeechGraceMs     = 1000; // 阈值以下持续超过 1s 才重置说话计时
        double peakRms = 0;

        var recordingStart = DateTime.Now;

        waveIn.DataAvailable += (s, e) =>
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);

            var rms     = CalculateRms(e.Buffer, e.BytesRecorded);
            var elapsed = (DateTime.Now - recordingStart).TotalMilliseconds;

            // ── 阶段一：采集底噪样本 ──────────────────────────────────────
            if (noisePhase)
            {
                if (elapsed < NoiseFloorMs)
                {
                    noiseRmsSum += rms;
                    noiseCount++;
                    return;
                }
                // 底噪测量完毕，计算动态阈值
                noisePhase = false;
                noiseFloor = noiseCount > 0 ? noiseRmsSum / noiseCount : MinSpeechThreshold;
                speechThreshold = Math.Max(MinSpeechThreshold, noiseFloor * SpeechMultiplier);
                Logger.Log("PERF", "底噪测量", $"noiseFloor={noiseFloor:F4}  speechThreshold={speechThreshold:F4}");
            }

            // ── 阶段二：说话 / 静音检测 ──────────────────────────────────
            if (rms > speechThreshold)
            {
                peakRms = Math.Max(peakRms, rms);
                belowThresholdAt = null; // 有声音，清除宽限期计时
                if (speechStart == null)
                {
                    speechStart = DateTime.Now;
                    Logger.Log("PERF", "说话开始", $"elapsed={elapsed:F0}ms  rms={rms:F4}");
                }
                silenceStart = null;
            }
            else if (!hasSpeech && speechStart != null)
            {
                // 声音掉到阈值以下：等宽限期结束再重置，允许说话时的自然波动
                belowThresholdAt ??= DateTime.Now;
                if ((DateTime.Now - belowThresholdAt.Value).TotalMilliseconds > SpeechGraceMs)
                {
                    Logger.Log("PERF", "说话计时重置", $"elapsed={elapsed:F0}ms（宽限期内无持续说话）");
                    speechStart      = null;
                    belowThresholdAt = null;
                }
            }

            // hasSpeech 判断移到 if/else 外：用挂钟时间累积，不要求每帧都高于阈值
            if (!hasSpeech && speechStart != null &&
                (DateTime.Now - speechStart.Value).TotalMilliseconds > MinSpeechMs)
            {
                hasSpeech = true;
                Logger.Log("PERF", "确认有效说话", $"elapsed={elapsed:F0}ms  peakRms={peakRms:F4}");
            }

            if (hasSpeech)
            {
                // 静音阈值：说话峰值的 25% 与 speechThreshold 一半中取较大值
                var silenceThreshold = Math.Max(speechThreshold * 0.5, peakRms * SilenceRatio);
                if (rms > silenceThreshold)
                    silenceStart = null;
                else
                {
                    if (silenceStart == null)
                        Logger.Log("PERF", "静音开始", $"elapsed={elapsed:F0}ms  rms={rms:F4}  silenceThreshold={silenceThreshold:F4}");
                    silenceStart ??= DateTime.Now;
                    if ((DateTime.Now - silenceStart.Value).TotalMilliseconds > SilenceDurationMs)
                        tcs.TrySetResult();
                }
            }

            // 超时保护
            if (elapsed > MaxDurationMs)
                tcs.TrySetResult();
        };

        waveIn.StartRecording();

        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(MaxDurationMs + 2000), ct);
        }
        catch (TimeoutException) { }
        catch (OperationCanceledException) { }
        finally
        {
            waveIn.StopRecording();
            waveIn.Dispose();
            writer.Dispose(); // 写入完整 WAV 文件头（同时会关闭 outputMs）
        }

        // WaveFileWriter.Dispose() 会关闭底层 MemoryStream，
        // 但 MemoryStream 关闭后 ToArray() 仍然有效，重新包装成可读的新流
        return new MemoryStream(outputMs.ToArray());
    }

    private static double CalculateRms(byte[] buffer, int byteCount)
    {
        double sum = 0;
        int samples = byteCount / 2;
        for (int i = 0; i < byteCount - 1; i += 2)
        {
            double sample = BitConverter.ToInt16(buffer, i) / 32768.0;
            sum += sample * sample;
        }
        return samples > 0 ? Math.Sqrt(sum / samples) : 0;
    }
}
