using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
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
            var loginWindow = new LoginWindow();
            desktop.MainWindow = loginWindow;
            loginWindow.Show();

            loginWindow.Loaded += async (s, e) =>
            {
                try
                {
                    await Task.Delay(500);

                    // 1. Capture Login Window
                    var lSize = new PixelSize((int)loginWindow.Bounds.Width, (int)loginWindow.Bounds.Height);
                    if (lSize.Width <= 0 || lSize.Height <= 0) lSize = new PixelSize(900, 600);
                    loginWindow.Width = lSize.Width;
                    loginWindow.Height = lSize.Height;
                    loginWindow.Measure(new Size(lSize.Width, lSize.Height));
                    loginWindow.Arrange(new Rect(0, 0, lSize.Width, lSize.Height));

                    var loginRtb = new RenderTargetBitmap(lSize, new Vector(96, 96));
                    loginRtb.Render(loginWindow);
                    loginRtb.Save(@"d:\GHIS\login_window.png");

                    // 2. Open MainWindow and capture View 7, 8 & Modals
                    var mainWindow = new MainWindow();
                    mainWindow.Show();

                    mainWindow.Loaded += async (ms, me) =>
                    {
                        try
                        {
                            await Task.Delay(500);

                            var mSize = new PixelSize((int)mainWindow.Bounds.Width, (int)mainWindow.Bounds.Height);
                            if (mSize.Width <= 0 || mSize.Height <= 0) mSize = new PixelSize(1440, 900);
                            mainWindow.Width = mSize.Width;
                            mainWindow.Height = mSize.Height;

                            void CaptureMain(string path)
                            {
                                mainWindow.Measure(new Size(mSize.Width, mSize.Height));
                                mainWindow.Arrange(new Rect(0, 0, mSize.Width, mSize.Height));
                                var rtb = new RenderTargetBitmap(mSize, new Vector(96, 96));
                                rtb.Render(mainWindow);
                                rtb.Save(path);
                            }

                            if (mainWindow.DataContext is WorkbenchModel vm)
                            {
                                // View 7: User Administration
                                vm.SelectedNavIndex = 7;
                                await Task.Delay(300);
                                CaptureMain(@"d:\GHIS\view_7_user_admin.png");

                                // View 8: Role & Permission Matrix
                                vm.SelectedNavIndex = 8;
                                await Task.Delay(300);
                                CaptureMain(@"d:\GHIS\view_8_role_matrix.png");

                                // Overlay 8: Create User Modal
                                vm.IsCreateUserModalOpen = true;
                                await Task.Delay(250);
                                CaptureMain(@"d:\GHIS\modal_create_user.png");
                                vm.IsCreateUserModalOpen = false;
                                await Task.Delay(100);

                                // Overlay 9: Create Role Modal
                                vm.IsCreateRoleModalOpen = true;
                                await Task.Delay(250);
                                CaptureMain(@"d:\GHIS\modal_create_role.png");
                                vm.IsCreateRoleModalOpen = false;
                                await Task.Delay(100);

                                // Overlay 10: Switch User Modal
                                vm.IsSwitchUserModalOpen = true;
                                await Task.Delay(250);
                                CaptureMain(@"d:\GHIS\modal_switch_user.png");
                                vm.IsSwitchUserModalOpen = false;
                                await Task.Delay(100);

                                // Reset to View 7
                                vm.SelectedNavIndex = 7;
                                await Task.Delay(150);
                                CaptureMain(@"d:\GHIS\current_window.png");
                            }

                            // Copy all to artifact directory
                            var artifactDir = @"C:\Users\admin\.gemini\antigravity\brain\96198005-d71e-489b-8e84-fc6907f3e95c";
                            if (Directory.Exists(artifactDir))
                            {
                                var files = new[]
                                {
                                    "login_window.png",
                                    "view_7_user_admin.png",
                                    "view_8_role_matrix.png",
                                    "modal_create_user.png",
                                    "modal_create_role.png",
                                    "modal_switch_user.png",
                                    "current_window.png"
                                };
                                foreach (var f in files)
                                {
                                    var src = Path.Combine(@"d:\GHIS", f);
                                    if (File.Exists(src))
                                    {
                                        File.Copy(src, Path.Combine(artifactDir, f), true);
                                    }
                                }
                            }
                        }
                        catch (Exception mex)
                        {
                            File.WriteAllText(@"d:\GHIS\main_capture_err.log", mex.ToString());
                        }
                    };
                }
                catch (Exception ex)
                {
                    File.WriteAllText(@"d:\GHIS\login_capture_err.log", ex.ToString());
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
