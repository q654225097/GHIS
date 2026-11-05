using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HIS.Client.UrsaShell.Views;

namespace HIS.Client.UrsaShell;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            window.Loaded += async (s, e) =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(800);
                    var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
                    if (size.Width <= 0 || size.Height <= 0) size = new PixelSize(1440, 900);
                    var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
                    window.Width = size.Width;
                    window.Height = size.Height;
                    window.Measure(new Size(size.Width, size.Height));
                    window.Arrange(new Rect(0, 0, size.Width, size.Height));
                    rtb.Render(window);
                    rtb.Save(@"d:\GHIS\current_window.png");
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(@"d:\GHIS\render_err.log", ex.ToString());
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

