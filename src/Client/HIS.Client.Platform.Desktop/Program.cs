using Avalonia;
using HIS.Client.UrsaShell;

namespace HIS.Client.Platform.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                HIS.Client.UrsaShell.Services.DesktopPrintServiceLocator.Current = new WindowsPrintService();
            }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Desktop startup error: " + ex);
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "desktop_error.log"), ex.ToString());
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
