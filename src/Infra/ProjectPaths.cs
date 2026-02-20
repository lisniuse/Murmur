namespace VoiceAssistant;

/// <summary>
/// 统一管理项目各目录路径。
/// 项目根目录通过向上查找 src/ 或 models/ 目录来定位，
/// 与编译配置（Debug/Release）和 exe 位置无关。
/// </summary>
internal static class ProjectPaths
{
    public static readonly string Root     = FindRoot();
    // 可由外部（AppSettings）覆盖，支持用户自定义模型目录
    public static string Models { get; set; } = Path.Combine(Root, "models");
    // 配置和日志与 Root 同级：开发时 = jws-test/，发布后 = EXE 同目录
    public static readonly string Settings = Path.Combine(Root, "settings.json");
    public static readonly string Log      = Path.Combine(Root, "app.log");

    private static string FindRoot()
    {
#if PRODUCTION
        // 生产环境（dotnet publish -c Release）：直接使用 EXE 所在目录
        // 不能用 BaseDirectory（单文件解压时指向临时目录）
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
        if (exeDir.Length > 0)
            return exeDir;
        return AppDomain.CurrentDomain.BaseDirectory;
#else
        // 开发模式（Debug）：向上查找含 src/ 或 models/ 的目录
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) ||
                Directory.Exists(Path.Combine(dir, "models")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null || parent == dir) break;
            dir = parent;
        }

        // 兜底：使用 EXE 所在目录
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
        return exeDir.Length > 0 ? exeDir : AppDomain.CurrentDomain.BaseDirectory;
#endif
    }
}
