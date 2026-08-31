using Avalonia;
using Avalonia.Fonts.Inter;

namespace WhatsAppChatViewer;

internal static class Program
{
    internal static IReadOnlyList<string> Arguments { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
