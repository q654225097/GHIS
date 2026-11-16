using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Client.UrsaShell.Views;

namespace HIS.Client.UrsaShell;

public partial class App : Application
{
    private const string ArtifactDir = @"C:\Users\admin\.gemini\antigravity\brain\96198005-d71e-489b-8e84-fc6907f3e95c";

    private static string ResolveCaptureOutputDir()
    {
        var envDir = Environment.GetEnvironmentVariable("GHIS_CAPTURE_DIR");
        var dir = !string.IsNullOrWhiteSpace(envDir)
            ? envDir
            : @"d:\GHIS";
        Directory.CreateDirectory(dir);
        return dir;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var isCaptureMode = Environment.GetEnvironmentVariable("CAPTURE_SCREENSHOTS") == "1";
            if (isCaptureMode)
            {
                RunCaptureAutomation(desktop);
            }
            else
            {
                var loginWindow = new LoginWindow();
                desktop.MainWindow = loginWindow;
                loginWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RunCaptureAutomation(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var workbench = new WorkbenchModel();
        var main = new MainShellWindow(workbench);
        var shell = main.ViewModel;
        desktop.MainWindow = main;
        main.Show();

        main.Loaded += async (s, e) =>
        {
            var outputDir = ResolveCaptureOutputDir();
            try
            {
                await Task.Delay(1000);
                if (SessionContext.Current.IsLoggedIn || main.IsLoaded)
                {
                    var size = new PixelSize(1440, 900);
                    main.Width = size.Width;
                    main.Height = size.Height;

                    void Capture(string fileName)
                    {
                        main.Measure(new Size(size.Width, size.Height));
                        main.Arrange(new Rect(0, 0, size.Width, size.Height));
                        var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
                        rtb.Render(main);
                        var localPath = Path.Combine(outputDir, fileName);
                        rtb.Save(localPath);
                        try
                        {
                            if (Directory.Exists(ArtifactDir))
                            {
                                File.Copy(localPath, Path.Combine(ArtifactDir, fileName), true);
                            }
                        }
                        catch { }
                    }

                    // 1. Outpatient prescription station.
                    shell.NavigateByKey("opd");
                    workbench.SelectedOpdTabIndex = 1;
                    await Task.Delay(500);
                    Capture("view_opd_prescription.png");
                    Capture("current_window.png");

                    // 1b. Prescription search demonstration.
                    workbench.PrescriptionSearchQuery = "阿司匹林";
                    await Task.Delay(300);
                    Capture("view_opd_search.png");
                    workbench.PrescriptionSearchQuery = string.Empty;

                    // 2. Inpatient doctor.
                    shell.NavigateByKey("ipd_doctor");
                    if (workbench.SelectedInpatient != null)
                    {
                        workbench.SelectedInpatient.ApplyOrderFilter();
                    }
                    await Task.Delay(500);
                    Capture("view_ipd_doctor.png");

                    // 2b. Long-term orders only.
                    if (workbench.SelectedInpatient != null)
                    {
                        workbench.SelectedInpatient.SelectedOrderFilter = "LONG";
                    }
                    await Task.Delay(300);
                    Capture("view_ipd_long_orders.png");

                    // 2c. Temporary orders only.
                    if (workbench.SelectedInpatient != null)
                    {
                        workbench.SelectedInpatient.SelectedOrderFilter = "TEMP";
                    }
                    await Task.Delay(300);
                    Capture("view_ipd_temp_orders.png");

                    if (workbench.SelectedInpatient != null)
                    {
                        workbench.SelectedInpatient.SelectedOrderFilter = "ALL";
                    }

                    // 3. Inpatient nurse.
                    shell.NavigateByKey("ipd_nurse");
                    workbench.SelectedNurseTabIndex = 0;
                    await Task.Delay(400);
                    Capture("view_ipd_nurse.png");

                    // 3b. Nursing execution.
                    workbench.SelectedNurseTabIndex = 1;
                    await Task.Delay(400);
                    Capture("view_ipd_nurse_tasks.png");

                    // 4. Admin stations.
                    SessionContext.Current.IsLoggedIn = true;
                    SessionContext.Current.RoleCode = "ROLE-ADMIN";
                    SessionContext.Current.RoleName = "超级管理员";
                    workbench.SyncWithSession();
                    shell.RebuildNavigation();

                    shell.NavigateByKey("dynamic_form");
                    await Task.Delay(300);
                    Capture("view_dynamic_form.png");

                    shell.NavigateByKey("user_admin");
                    await Task.Delay(300);
                    Capture("view_user_admin.png");

                    shell.NavigateByKey("role_admin");
                    await Task.Delay(300);
                    Capture("view_role_admin.png");

                    // Done capturing, write success flag and shutdown
                    File.WriteAllText(Path.Combine(outputDir, "capture_done.txt"), DateTime.Now.ToString("o"));
                    await Task.Delay(500);
                    desktop.Shutdown();
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(outputDir, "capture_err.log"), ex.ToString());
                desktop.Shutdown();
            }
        };
    }
}
