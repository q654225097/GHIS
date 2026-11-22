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
        try
        {
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App.Initialize] Fallback loading themes: {ex.Message}");
            Styles.Add(new Semi.Avalonia.SemiTheme());
            Styles.Add(new Ursa.Themes.Semi.UrsaSemiTheme());
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
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
#else
            var loginWindow = new LoginWindow();
            desktop.MainWindow = loginWindow;
            loginWindow.Show();
#endif
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new Views.MainHostView();
        }

        base.OnFrameworkInitializationCompleted();
    }

#if DEBUG
    private void RunCaptureAutomation(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _ = RunCaptureAutomationAsync(desktop);
    }

    private async Task RunCaptureAutomationAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var outputDir = ResolveCaptureOutputDir();
        try
        {
            var loginWin = new LoginWindow();
            loginWin.Width = 960;
            loginWin.Height = 600;
            loginWin.Measure(new Size(960, 600));
            loginWin.Arrange(new Rect(0, 0, 960, 600));
            var loginRtb = new RenderTargetBitmap(new PixelSize(960, 600), new Vector(96, 96));
            loginRtb.Render(loginWin);
            var loginPath = Path.Combine(outputDir, "view_00_login.png");
#pragma warning disable CS0618
            loginRtb.Save(loginPath);
#pragma warning restore CS0618
            if (Directory.Exists(ArtifactDir))
            {
                File.Copy(loginPath, Path.Combine(ArtifactDir, "view_00_login.png"), true);
            }
        }
        catch { }

        // Ensure Admin context with full permissions BEFORE creating MainShellWindow
        try
        {
            var loginModel = new LoginModel();
            loginModel.SelectedUserName = "admin";
            loginModel.Password = "123";
            if (await loginModel.EnsureContextsAsync())
            {
                await loginModel.LoginAsync();
            }
        }
        catch { }
        if (!SessionContext.Current.IsLoggedIn)
        {
            SessionContext.Current.IsLoggedIn = true;
            SessionContext.Current.RoleCode = "ROLE-ADMIN";
            SessionContext.Current.RoleName = "超级管理员";
        }

        var workbench = new WorkbenchModel();
        workbench.SyncWithSession();
        var main = new MainShellWindow(workbench);
        var shell = main.ViewModel;
        desktop.MainWindow = main;
        main.Show();

        main.Loaded += async (s, e) =>
        {
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
#pragma warning disable CS0618
                        rtb.Save(localPath);
#pragma warning restore CS0618
                        try
                        {
                            if (Directory.Exists(ArtifactDir))
                            {
                                File.Copy(localPath, Path.Combine(ArtifactDir, fileName), true);
                            }
                        }
                        catch { }
                    }

                    workbench.SyncWithSession();
                    shell.RebuildNavigation();

                    // 1. Workbench (我的待办)
                    shell.NavigateByKey("workbench");
                    await Task.Delay(400);
                    Capture("view_01_workbench.png");

                    // 2. Outpatient doctor (门诊医生站)
                    shell.NavigateByKey("opd");
                    workbench.SelectedOpdTabIndex = 1;
                    await Task.Delay(400);
                    Capture("view_02_opd_doctor.png");
                    Capture("current_window.png");

                    // 3. Inpatient doctor (住院医生站)
                    shell.NavigateByKey("ipd_doctor");
                    await Task.Delay(400);
                    Capture("view_03_ipd_doctor.png");

                    // 4. Inpatient nurse (病区护士站)
                    shell.NavigateByKey("ipd_nurse");
                    await Task.Delay(400);
                    Capture("view_04_ipd_nurse.png");

                    // 5. MedTech station (医技与检查 D2)
                    shell.NavigateByKey("medtech");
                    await Task.Delay(400);
                    Capture("view_05_medtech.png");

                    // 6. Pharmacy Window (窗口发药)
                    shell.NavigateByKey("pharmacy_window");
                    await Task.Delay(400);
                    Capture("view_06_pharmacy_window.png");

                    // 7. Pharmacy Warehouse (药库与目录 D6)
                    shell.NavigateByKey("pharmacy_warehouse");
                    await Task.Delay(400);
                    Capture("view_07_pharmacy_warehouse.png");

                    // 8. Outpatient Registration (门诊挂号 D1)
                    shell.NavigateByKey("registration");
                    await Task.Delay(400);
                    Capture("view_08_registration.png");

                    // 8b. Patient Registration Form (新建患者实名建档)
                    if (shell.CurrentStationView is Control { DataContext: HIS.Client.UrsaShell.Stations.Registration.RegistrationStationViewModel regVm })
                    {
                        regVm.Registration.ShowPatientForm();
                        await Task.Delay(300);
                        Capture("view_08b_patient_registration_form.png");
                        regVm.Registration.HidePatientForm();
                    }

                    // 9. Finance Outpatient Billing (收费结算)
                    shell.NavigateByKey("finance");
                    workbench.SelectedFinanceTabIndex = 0;
                    await Task.Delay(400);
                    Capture("view_09_finance_settle.png");

                    // 10. Inpatient Admission & Prepayment Cashier (住院登记与预交金 D1)
                    workbench.SelectedFinanceTabIndex = 2;
                    await Task.Delay(500);
                    Capture("view_10_inpatient_cashier.png");

                    // 11. EMR Archive (病历归档)
                    shell.NavigateByKey("emr_archive");
                    await Task.Delay(400);
                    Capture("view_11_emr_archive.png");

                    // 12. Dynamic Form (动态表单配置 D3)
                    shell.NavigateByKey("dynamic_form");
                    await Task.Delay(600);
                    if (shell.CurrentStationView is Control { DataContext: HIS.Client.UrsaShell.Stations.Admin.DynamicFormStationViewModel dfVm })
                    {
                        if (dfVm.SelectedTemplate == null && dfVm.FilteredTemplates.Count > 0)
                        {
                            dfVm.SelectedTemplate = dfVm.FilteredTemplates.FirstOrDefault(t => t.TemplateCode == "FORM-PRESCRIPTION-ITEM-FIELDS") ?? dfVm.FilteredTemplates[0];
                            await Task.Delay(500);
                        }
                    }
                    Capture("view_12_dynamic_form.png");

                    // 13. User Administration (用户账号维护)
                    shell.NavigateByKey("user_admin");
                    await Task.Delay(400);
                    Capture("view_13_user_admin.png");

                    // 14. Role & Permissions (角色与权限)
                    shell.NavigateByKey("role_admin");
                    await Task.Delay(400);
                    Capture("view_14_role_admin.png");

                    // 15. Audit Log (安全审计日志)
                    shell.NavigateByKey("audit_log");
                    await Task.Delay(400);
                    Capture("view_15_audit_log.png");

                    // 16. Workflow Designer (业务流程设计器)
                    shell.NavigateByKey("workflow_designer");
                    await Task.Delay(800);
                    if (shell.CurrentStationView is Control { DataContext: HIS.Client.UrsaShell.Stations.WorkflowDesigner.WorkflowDesignerViewModel wfVm })
                    {
                        if (wfVm.Definitions.Count > 0)
                        {
                            var defToSelect = wfVm.Definitions.FirstOrDefault(d => d.DefKey == "WF_DEF_CSM_REQUISITION") ?? wfVm.Definitions[0];
                            wfVm.SelectedDefinition = defToSelect;
                            await Task.Delay(600);
                        }
                        Capture("view_16_workflow_designer.png");

                        // 16b. User scenario: New workflow & Add START node
                        wfVm.NewDefinitionCommand.Execute(null);
                        wfVm.AddNodeCommand.Execute("START");
                        await Task.Delay(500);
                        Capture("view_16b_workflow_new_node.png");
                    }
                    else
                    {
                        Capture("view_16_workflow_designer.png");
                    }

                    // Done capturing, write success flag and shutdown
                    File.WriteAllText(Path.Combine(outputDir, "capture_done.txt"), DateTime.Now.ToString("o"));
                    await Task.Delay(300);
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
#endif
}
