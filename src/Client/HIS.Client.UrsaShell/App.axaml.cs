using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HIS.Client.UrsaShell.ViewModels;
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
                    if (window.DataContext is WorkbenchModel vm)
                    {
                        var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
                        if (size.Width <= 0 || size.Height <= 0) size = new PixelSize(1440, 900);
                        window.Width = size.Width;
                        window.Height = size.Height;

                        void Capture(string path)
                        {
                            window.Measure(new Size(size.Width, size.Height));
                            window.Arrange(new Rect(0, 0, size.Width, size.Height));
                            var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
                            rtb.Render(window);
                            rtb.Save(path);
                        }

                        // Capture Default (View 0: OPD)
                        Capture(@"d:\GHIS\current_window.png");
                        Capture(@"d:\GHIS\view_0_opd.png");

                        // Capture View 1: Inpatient
                        vm.SelectedNavIndex = 1;
                        await System.Threading.Tasks.Task.Delay(250);
                        Capture(@"d:\GHIS\view_1_ipd.png");

                        // Capture View 2: Ward Bed Matrix
                        vm.SelectedNavIndex = 2;
                        await System.Threading.Tasks.Task.Delay(250);
                        Capture(@"d:\GHIS\view_2_ward.png");

                        // Capture View 3: MedTech
                        vm.SelectedNavIndex = 3;
                        await System.Threading.Tasks.Task.Delay(250);
                        Capture(@"d:\GHIS\view_3_medtech.png");

                        // Capture View 4: Pharmacy
                        vm.SelectedNavIndex = 4;
                        await System.Threading.Tasks.Task.Delay(250);
                        Capture(@"d:\GHIS\view_4_pharmacy.png");

                        // Capture View 5: EMR & OFD
                        vm.SelectedNavIndex = 5;
                        await System.Threading.Tasks.Task.Delay(250);
                        Capture(@"d:\GHIS\view_5_ofd.png");

                        // Capture View 6: Dynamic Form Config
                        vm.SelectedNavIndex = 6;
                        await System.Threading.Tasks.Task.Delay(250);
                        Capture(@"d:\GHIS\view_6_config.png");

                        // Reset back to View 0
                        vm.SelectedNavIndex = 0;
                        await System.Threading.Tasks.Task.Delay(100);
                    }
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
