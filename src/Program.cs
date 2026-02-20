namespace VoiceAssistant;

static class Program
{
    [STAThread]
    static void Main()
    {
        // 防止多实例运行
        var mutex = new System.Threading.Mutex(true, "VoiceAssistantSingleInstance", out bool isNew);
        if (!isNew) return;

        // .NET 5+ 默认只含 UTF-8，注册后 GBK/GB2312 等系统编码才可用
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
