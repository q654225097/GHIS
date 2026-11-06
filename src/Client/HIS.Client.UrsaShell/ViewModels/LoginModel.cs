using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class LoginUserOption
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string Avatar { get; set; } = "👤";
}

public class LoginModel : ClientModelBase
{
    private string _selectedUserName = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoggingIn;
    private LoginUserOption? _selectedUser;

    public ObservableCollection<LoginUserOption> KnownUsers { get; } = new();

    public LoginUserOption? SelectedUser
    {
        get => _selectedUser;
        set
        {
            SetField(ref _selectedUser, value);
            if (value != null) SelectedUserName = value.UserName;
        }
    }

    public string SelectedUserName
    {
        get => _selectedUserName;
        set => SetField(ref _selectedUserName, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public bool IsLoggingIn
    {
        get => _isLoggingIn;
        set => SetField(ref _isLoggingIn, value);
    }

    public LoginModel()
    {
        // Pre-populate known users (matches DB seed data)
        KnownUsers.Add(new LoginUserOption { UserName = "admin",      DisplayName = "系统管理员",    RoleName = "超级管理员",   RoleCode = "ROLE-ADMIN",         Avatar = "🛡️" });
        KnownUsers.Add(new LoginUserOption { UserName = "doc_opd",    DisplayName = "张建国",        RoleName = "门诊医生",     RoleCode = "ROLE-OPD-DOC",       Avatar = "🩺" });
        KnownUsers.Add(new LoginUserOption { UserName = "doc_res",    DisplayName = "李华",          RoleName = "住院医师",     RoleCode = "ROLE-IPD-RESIDENT",  Avatar = "🏥" });
        KnownUsers.Add(new LoginUserOption { UserName = "doc_att",    DisplayName = "王磊",          RoleName = "主治医师",     RoleCode = "ROLE-IPD-ATTENDING", Avatar = "👨‍⚕️" });
        KnownUsers.Add(new LoginUserOption { UserName = "nurse_w1",   DisplayName = "陈劳",          RoleName = "病区护士",     RoleCode = "ROLE-WARD-NURSE",    Avatar = "👩‍⚕️" });
        KnownUsers.Add(new LoginUserOption { UserName = "nurse_w2",   DisplayName = "赵敏",          RoleName = "病区护士",     RoleCode = "ROLE-WARD-NURSE",    Avatar = "👩‍⚕️" });
        KnownUsers.Add(new LoginUserOption { UserName = "phm_user",   DisplayName = "孙杰",          RoleName = "药剂师",       RoleCode = "ROLE-PHARMACIST",    Avatar = "💊" });
        KnownUsers.Add(new LoginUserOption { UserName = "cashier_01", DisplayName = "刘洋",          RoleName = "收费员",       RoleCode = "ROLE-CASHIER",       Avatar = "💰" });

        // Default selection
        SelectedUser = KnownUsers[1]; // doc_opd
        Password = "123456";
    }

    public async Task<bool> LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedUserName))
        {
            ErrorMessage = "请选择或输入用户名";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "请输入密码";
            return false;
        }

        IsLoggingIn = true;
        ErrorMessage = string.Empty;

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5200") };
            var payload = new { UserName = SelectedUserName, Password };
            var resp = await http.PostAsJsonAsync("/api/v4/platform/auth/login", payload);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
            {
                var msg = root.TryGetProperty("message", out var mp) ? mp.GetString() : "登录失败";
                ErrorMessage = msg ?? "登录失败，请检查用户名和密码";
                return false;
            }

            var data = root.GetProperty("data");
            var accessToken = data.GetProperty("accessToken").GetString() ?? string.Empty;
            var userId     = data.GetProperty("userId").GetString() ?? string.Empty;
            var displayName= data.GetProperty("displayName").GetString() ?? string.Empty;
            var deptId     = data.GetProperty("activeDeptId").GetString() ?? string.Empty;
            var deptCat    = data.GetProperty("activeDeptCategory").GetString() ?? "OPD";
            string? wardId = data.TryGetProperty("activeWardId", out var wProp) && wProp.ValueKind != JsonValueKind.Null
                           ? wProp.GetString() : null;

            // Parse JWT to get role info (decode claims)
            var (roleCode, roleName, deptName, prescribeLevel, surgeryLevel, emrSignLevel) =
                ParseJwtClaims(accessToken, SelectedUser);

            SessionContext.Current.SetFromLogin(
                userId, SelectedUserName, displayName,
                roleCode, roleName,
                accessToken,
                deptId, deptName, deptCat,
                wardId,
                prescribeLevel, surgeryLevel, emrSignLevel);

            return true;
        }
        catch (HttpRequestException)
        {
            // Offline fallback: allow quick-login with preset users (dev/demo mode)
            return DoOfflineLogin();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"登录异常: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    /// <summary>Offline/demo login: uses hardcoded credentials to skip server requirement.</summary>
    private bool DoOfflineLogin()
    {
        if (Password != "123456" && Password != SelectedUserName)
        {
            ErrorMessage = "密码错误 (离线模式: 密码为 123456)";
            return false;
        }

        var user = SelectedUser ?? KnownUsers[1];
        var deptMap = new System.Collections.Generic.Dictionary<string, (string id, string name, string cat)>
        {
            ["ROLE-ADMIN"]          = ("DEPT-ADMIN-01", "信息管理中心",         "ADMIN"),
            ["ROLE-OPD-DOC"]        = ("DEPT-OPD-01",   "心血管内科门诊",       "OPD"),
            ["ROLE-IPD-RESIDENT"]   = ("DEPT-IPD-01",   "心血管内科住院一科",   "IPD"),
            ["ROLE-IPD-ATTENDING"]  = ("DEPT-IPD-01",   "心血管内科住院一科",   "IPD"),
            ["ROLE-WARD-NURSE"]     = ("DEPT-WARD-01",  "内科一病区",           "WARD"),
            ["ROLE-PHARMACIST"]     = ("DEPT-PHM-01",   "药剂科发药室",         "PHARMACY"),
            ["ROLE-CASHIER"]        = ("DEPT-FIN-01",   "门诊收费处",           "FINANCE"),
        };

        var (deptId, deptName, deptCat) = deptMap.TryGetValue(user.RoleCode, out var d) ? d : ("DEPT-OPD-01", "门诊", "OPD");
        var (pl, sl, el) = user.RoleCode switch
        {
            "ROLE-ADMIN"         => (3, 3, 3),
            "ROLE-IPD-ATTENDING" => (2, 2, 2),
            "ROLE-OPD-DOC"       => (2, 1, 2),
            "ROLE-IPD-RESIDENT"  => (1, 1, 1),
            _ => (0, 0, 0)
        };

        SessionContext.Current.SetFromLogin(
            userId: "USR-" + user.UserName.ToUpper(),
            userName: user.UserName,
            displayName: user.DisplayName,
            roleCode: user.RoleCode,
            roleName: user.RoleName,
            accessToken: "offline-token",
            activeDeptId: deptId,
            activeDeptName: deptName,
            activeDeptCategory: deptCat,
            activeWardId: null,
            prescribeLevel: pl,
            surgeryLevel: sl,
            emrSignLevel: el);

        return true;
    }

    private (string roleCode, string roleName, string deptName, int pl, int sl, int el) ParseJwtClaims(
        string token, LoginUserOption? user)
    {
        // Simple JWT payload decode (base64url → JSON)
        try
        {
            var parts = token.Split('.');
            if (parts.Length >= 2)
            {
                var payload = parts[1];
                // Fix base64url padding
                payload = payload.Replace('-', '+').Replace('_', '/');
                var mod = payload.Length % 4;
                if (mod > 0) payload += new string('=', 4 - mod);
                var bytes = Convert.FromBase64String(payload);
                using var doc2 = JsonDocument.Parse(bytes);
                var r = doc2.RootElement;

                var roleCode = r.TryGetProperty("role_code", out var rcp) ? (rcp.GetString() ?? user?.RoleCode ?? "ROLE-OPD-DOC") : (user?.RoleCode ?? "ROLE-OPD-DOC");
                var roleName = r.TryGetProperty("role_name", out var rnp) ? (rnp.GetString() ?? user?.RoleName ?? "门诊医生") : (user?.RoleName ?? "门诊医生");
                var deptName = r.TryGetProperty("dept_name", out var dnp) ? (dnp.GetString() ?? "心血管内科门诊") : "心血管内科门诊";
                var pl = r.TryGetProperty("prescribe_level", out var plp) ? plp.GetInt32() : 1;
                var sl = r.TryGetProperty("surgery_level",   out var slp) ? slp.GetInt32() : 1;
                var el = r.TryGetProperty("emr_sign_level",  out var elp) ? elp.GetInt32() : 1;
                return (roleCode, roleName, deptName, pl, sl, el);
            }
        }
        catch { /* ignore parse errors, fall back to user preset */ }

        return (user?.RoleCode ?? "ROLE-OPD-DOC", user?.RoleName ?? "门诊医生", "心血管内科门诊", 1, 1, 1);
    }
}

