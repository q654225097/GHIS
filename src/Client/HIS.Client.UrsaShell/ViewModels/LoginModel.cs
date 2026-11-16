using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class LoginModel : ClientModelBase
{
	public sealed class ApiEnvelope<T>
	{
		public int Code { get; set; }

		public string? Message { get; set; }

		public T? Data { get; set; }
	}

	public sealed class LoginContextDto
	{
		public string PostId { get; set; } = string.Empty;

		public string UserId { get; set; } = string.Empty;

		public string UserName { get; set; } = string.Empty;

		public string DisplayName { get; set; } = string.Empty;

		public string EmployeeNo { get; set; } = string.Empty;

		public int PrescribeLevel { get; set; }

		public int SurgeryLevel { get; set; }

		public int EmrSignLevel { get; set; }

		public string RoleId { get; set; } = string.Empty;

		public string RoleCode { get; set; } = string.Empty;

		public string RoleName { get; set; } = string.Empty;

		public string DeptId { get; set; } = string.Empty;

		public string DeptName { get; set; } = string.Empty;

		public string DeptCategory { get; set; } = string.Empty;

		public string? WardId { get; set; }

		public string DataScope { get; set; } = string.Empty;

		public bool IsDefault { get; set; }

		public LoginContextOption ToOption()
		{
			return new LoginContextOption
			{
				PostId = PostId,
				UserId = UserId,
				UserName = UserName,
				DisplayName = DisplayName,
				EmployeeNo = EmployeeNo,
				PrescribeLevel = PrescribeLevel,
				SurgeryLevel = SurgeryLevel,
				EmrSignLevel = EmrSignLevel,
				RoleId = RoleId,
				RoleCode = RoleCode,
				RoleName = RoleName,
				DeptId = DeptId,
				DeptName = DeptName,
				DeptCategory = DeptCategory,
				WardId = WardId,
				DataScope = DataScope,
				IsDefault = IsDefault
			};
		}

		public LoginContextSnapshot ToSnapshot()
		{
			return new LoginContextSnapshot(
				PostId,
				UserId,
				UserName,
				DisplayName,
				EmployeeNo,
				PrescribeLevel,
				SurgeryLevel,
				EmrSignLevel,
				RoleId,
				RoleCode,
				RoleName,
				DeptId,
				DeptName,
				DeptCategory,
				WardId,
				DataScope,
				IsDefault);
		}
	}

	public sealed class LoginResponseDto
	{
		public string AccessToken { get; set; } = string.Empty;

		public string? RefreshToken { get; set; }

		public long AccessTokenExpiresAtMs { get; set; }

		public long RefreshTokenExpiresAtMs { get; set; }

		public string UserId { get; set; } = string.Empty;

		public string UserName { get; set; } = string.Empty;

		public string DisplayName { get; set; } = string.Empty;

		public string ActiveDeptCategory { get; set; } = string.Empty;

		public string ActiveDeptId { get; set; } = string.Empty;

		public string? ActiveWardId { get; set; }

		public IReadOnlyList<LoginContextDto>? Contexts { get; set; }

		public string? PostId { get; set; }

		public string? RoleId { get; set; }

		public string? RoleCode { get; set; }

		public string? RoleName { get; set; }

		public string? ActiveDeptName { get; set; }

		public string? EmployeeNo { get; set; }

		public int PrescribeLevel { get; set; }

		public int SurgeryLevel { get; set; }

		public int EmrSignLevel { get; set; }
	}

	public event Action? ContextsLoaded;

	public event Action? LoginSucceeded;

	private string _selectedUserName = string.Empty;

	private string _password = "123";

	private string _errorMessage = string.Empty;

	private bool _isLoggingIn;

	private bool _isLoadingContexts;

	private LoginUserOption? _selectedUser;

	private LoginContextOption? _selectedContext;

	private string _loadedUserName = string.Empty;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	public ObservableCollection<LoginUserOption> KnownUsers { get; } = new ObservableCollection<LoginUserOption>();

	public ObservableCollection<LoginContextOption> AvailableContexts { get; } = new ObservableCollection<LoginContextOption>();

	public bool HasContexts => AvailableContexts.Count > 0;

	public bool HasNoContexts => !IsLoadingContexts && AvailableContexts.Count == 0;

	public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

	public string LoginHeading => SelectedUser == null
		? "选择本次登录身份"
		: "登录 " + SelectedUser.DisplayName;

	public string LoginSubheading => "先确认账号，再选择本次执业身份。";

	public string ContextStatusText => IsLoadingContexts
		? "正在加载"
		: AvailableContexts.Count == 0
			? "未加载"
			: AvailableContexts.Count + " 个身份";

	public string ContextHelpText => IsLoadingContexts
		? "正在读取账号绑定的角色与科室。"
		: string.IsNullOrWhiteSpace(SelectedUserName)
			? "输入用户名或工号后加载身份。"
			: "该账号暂无可登录身份，请联系系统管理员配置角色与科室。";

	public bool HasLoginBlockedReason => !CanLogin;

	public string LoginBlockedReason
	{
		get
		{
			if (IsLoadingContexts)
			{
				return "正在加载本次登录身份。";
			}
			if (IsLoggingIn)
			{
				return "正在验证账号与身份。";
			}
			if (string.IsNullOrWhiteSpace(SelectedUserName))
			{
				return "请选择或输入用户名。";
			}
			if (SelectedContext == null)
			{
				return "请选择一个本次登录的执业身份。";
			}
			if (string.IsNullOrWhiteSpace(Password))
			{
				return "请输入登录密码。";
			}
			if (!string.Equals(_loadedUserName, SelectedUserName.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return "请先加载该账号的角色与科室身份。";
			}
			return string.Empty;
		}
	}

	public bool CanLogin => !IsLoggingIn && !IsLoadingContexts && SelectedContext != null
		&& !string.IsNullOrWhiteSpace(Password)
		&& string.Equals(_loadedUserName, SelectedUserName.Trim(), StringComparison.OrdinalIgnoreCase);

	public LoginUserOption? SelectedUser
	{
		get
		{
			return _selectedUser;
		}
		set
		{
			if (SetField(ref _selectedUser, value, "SelectedUser") && value != null)
			{
				SelectedUserName = value.UserName;
				OnPropertyChanged(nameof(LoginHeading));
			}
		}
	}

	public LoginContextOption? SelectedContext
	{
		get
		{
			return _selectedContext;
		}
		set
		{
			if (_selectedContext != null)
			{
				_selectedContext.IsSelected = false;
			}
			if (SetField(ref _selectedContext, value, "SelectedContext") && value != null)
			{
				value.IsSelected = true;
			}
			OnPropertyChanged("CanLogin");
			OnPropertyChanged(nameof(HasLoginBlockedReason));
			OnPropertyChanged(nameof(LoginBlockedReason));
		}
	}

	public string SelectedUserName
	{
		get
		{
			return _selectedUserName;
		}
		set
		{
			if (SetField(ref _selectedUserName, value, "SelectedUserName"))
			{
				if (!string.Equals(value.Trim(), _loadedUserName, StringComparison.OrdinalIgnoreCase))
				{
					AvailableContexts.Clear();
					SelectedContext = null;
					OnPropertyChanged("HasContexts");
					OnPropertyChanged("HasNoContexts");
				}
				OnPropertyChanged("CanLogin");
				OnPropertyChanged(nameof(HasLoginBlockedReason));
				OnPropertyChanged(nameof(LoginBlockedReason));
				OnPropertyChanged(nameof(ContextHelpText));
				LoginUserOption loginUserOption = KnownUsers.FirstOrDefault((LoginUserOption x) => x.UserName.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
				if (loginUserOption != null && loginUserOption != SelectedUser)
				{
					SelectedUser = loginUserOption;
				}
			}
		}
	}

	public string Password
	{
		get
		{
			return _password;
		}
		set
		{
			if (SetField(ref _password, value, "Password"))
			{
				OnPropertyChanged("CanLogin");
				OnPropertyChanged(nameof(HasLoginBlockedReason));
				OnPropertyChanged(nameof(LoginBlockedReason));
			}
		}
	}

	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		set
		{
			if (SetField(ref _errorMessage, value, "ErrorMessage"))
			{
				OnPropertyChanged(nameof(HasError));
			}
		}
	}

	public bool IsLoggingIn
	{
		get
		{
			return _isLoggingIn;
		}
		set
		{
			if (SetField(ref _isLoggingIn, value, "IsLoggingIn"))
			{
				OnPropertyChanged("CanLogin");
				OnPropertyChanged(nameof(LoginHeading));
				OnPropertyChanged(nameof(HasLoginBlockedReason));
				OnPropertyChanged(nameof(LoginBlockedReason));
			}
		}
	}

	public bool IsLoadingContexts
	{
		get
		{
			return _isLoadingContexts;
		}
		set
		{
			if (SetField(ref _isLoadingContexts, value, "IsLoadingContexts"))
			{
				OnPropertyChanged("CanLogin");
				OnPropertyChanged(nameof(HasLoginBlockedReason));
				OnPropertyChanged(nameof(LoginBlockedReason));
				OnPropertyChanged(nameof(ContextStatusText));
				OnPropertyChanged(nameof(ContextHelpText));
			}
		}
	}

	public LoginModel()
	{
		SelectUserCommand = new AsyncRelayCommand(async param =>
		{
			if (param is LoginUserOption user)
			{
				await SelectUserAsync(user);
			}
		});
		LoginCommand = new AsyncRelayCommand(LoginWithContextAsync);
		RefreshContextsCommand = new AsyncRelayCommand(LoadContextsAsync);

		KnownUsers.Add(new LoginUserOption
		{
			UserName = "admin",
			DisplayName = "系统管理员",
			RoleName = "全院超级管理员",
			RoleCode = "ADMIN",
			EmployeeNo = "A001"
		});
		KnownUsers.Add(new LoginUserOption
		{
			UserName = "doc_opd",
			DisplayName = "张建国",
			RoleName = "门诊/住院多身份主治",
			RoleCode = "OPD_DOC / IPD_ATTENDING",
			EmployeeNo = "D001"
		});
		KnownUsers.Add(new LoginUserOption
		{
			UserName = "doc_res",
			DisplayName = "李华",
			RoleName = "心内住院医师",
			RoleCode = "IPD_RESIDENT",
			EmployeeNo = "D002"
		});
		KnownUsers.Add(new LoginUserOption
		{
			UserName = "doc_att",
			DisplayName = "王崇",
			RoleName = "多科室主治医师",
			RoleCode = "IPD_ATTENDING",
			EmployeeNo = "D003"
		});
		KnownUsers.Add(new LoginUserOption
		{
			UserName = "nurse_w1",
			DisplayName = "陈芳",
			RoleName = "内科一病区护士",
			RoleCode = "WARD_NURSE",
			EmployeeNo = "N001"
		});
		KnownUsers.Add(new LoginUserOption
		{
			UserName = "nurse_w2",
			DisplayName = "赵敏",
			RoleName = "内科二病区护士",
			RoleCode = "WARD_NURSE",
			EmployeeNo = "N002"
		});
		KnownUsers.Add(new LoginUserOption
		{
			UserName = "phm_user",
			DisplayName = "孙伟",
			RoleName = "门诊西药房药师",
			RoleCode = "PHARMACIST",
			EmployeeNo = "P001"
		});
		KnownUsers.Add(new LoginUserOption
		{
			UserName = "cashier_01",
			DisplayName = "刘洋",
			RoleName = "财务收费员",
			RoleCode = "CASHIER",
			EmployeeNo = "C001"
		});
		SelectedUser = KnownUsers[1];
	}

	public ICommand SelectUserCommand { get; }

	public ICommand LoginCommand { get; }

	public ICommand RefreshContextsCommand { get; }

	public async Task LoadContextsAsync()
	{
		string userName = SelectedUserName.Trim();
		if (string.IsNullOrWhiteSpace(userName) || IsLoadingContexts)
		{
			return;
		}
		IsLoadingContexts = true;
		ErrorMessage = string.Empty;
		AvailableContexts.Clear();
		SelectedContext = null;
		OnPropertyChanged("HasContexts");
		OnPropertyChanged("HasNoContexts");
		try
		{
			using HttpClient http = new HttpClient
			{
				BaseAddress = new Uri(ApiEndpoints.BaseAddress),
				Timeout = TimeSpan.FromSeconds(8)
			};
			using HttpResponseMessage response = await http.GetAsync("/api/v4/platform/auth/contexts?userName=" + Uri.EscapeDataString(userName));
			ApiEnvelope<List<LoginContextDto>> envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<LoginContextDto>>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200)
			{
				ErrorMessage = envelope?.Message ?? $"身份加载失败（HTTP {(int)response.StatusCode}）";
				return;
			}
			if (envelope.Data == null || envelope.Data.Count == 0)
			{
				ErrorMessage = "该账号尚未配置可用的角色与科室身份，请联系系统管理员";
				return;
			}
			foreach (LoginContextDto context in envelope.Data)
			{
				AvailableContexts.Add(context.ToOption());
			}
			_loadedUserName = userName;
			SelectedContext = AvailableContexts.FirstOrDefault((LoginContextOption x) => x.IsDefault) ?? AvailableContexts.FirstOrDefault();
			OnPropertyChanged("HasContexts");
			OnPropertyChanged("HasNoContexts");
			ContextsLoaded?.Invoke();
		}
		catch (HttpRequestException ex)
		{
			ErrorMessage = "无法连接身份服务，请确认 HIS 服务端已启动：" + ex.Message;
		}
		catch (TaskCanceledException)
		{
			ErrorMessage = "身份服务响应超时，请稍后重试";
		}
		catch (JsonException ex)
		{
			ErrorMessage = "身份服务返回了无法解析的数据：" + ex.Message;
		}
		finally
		{
			IsLoadingContexts = false;
			OnPropertyChanged("HasNoContexts");
			OnPropertyChanged(nameof(ContextStatusText));
			OnPropertyChanged(nameof(ContextHelpText));
			OnPropertyChanged("CanLogin");
			OnPropertyChanged(nameof(HasLoginBlockedReason));
			OnPropertyChanged(nameof(LoginBlockedReason));
		}
	}

	public async Task SelectUserAsync(LoginUserOption user)
	{
		SelectedUser = user;
		await LoadContextsAsync();
	}

	public async Task<bool> EnsureContextsAsync()
	{
		if (SelectedContext != null && string.Equals(_loadedUserName, SelectedUserName.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		await LoadContextsAsync();
		return SelectedContext != null;
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
		if (SelectedContext == null)
		{
			ErrorMessage = "请选择本次登录的执业身份";
			return false;
		}
		IsLoggingIn = true;
		ErrorMessage = string.Empty;
		try
		{
			using HttpClient http = new HttpClient
			{
				BaseAddress = new Uri(ApiEndpoints.BaseAddress),
				Timeout = TimeSpan.FromSeconds(10)
			};
			using HttpResponseMessage response = await http.PostAsJsonAsync("/api/v4/platform/auth/login", new
			{
				UserName = SelectedUserName.Trim(),
				Password = Password,
				ContextId = SelectedContext.PostId
			});
			ApiEnvelope<LoginResponseDto> envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LoginResponseDto>>(JsonOptions);
			if (!response.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || envelope.Data == null)
			{
				ErrorMessage = envelope?.Message ?? $"登录失败（HTTP {(int)response.StatusCode}）";
				return false;
			}
			if (string.IsNullOrWhiteSpace(envelope.Data.AccessToken))
			{
				ErrorMessage = "登录响应缺少有效访问令牌，已拒绝进入系统";
				return false;
			}
			ApplyLogin(envelope.Data);
			return true;
		}
		catch (HttpRequestException ex)
		{
			ErrorMessage = "无法连接认证服务，请确认 HIS 服务端已启动：" + ex.Message;
		}
		catch (TaskCanceledException)
		{
			ErrorMessage = "认证服务响应超时，请稍后重试";
		}
		catch (JsonException ex)
		{
			ErrorMessage = "认证服务返回了无法解析的数据：" + ex.Message;
		}
		finally
		{
			IsLoggingIn = false;
		}
		return false;
	}

	private async Task LoginWithContextAsync()
	{
		if (!await EnsureContextsAsync())
		{
			return;
		}
		if (await LoginAsync())
		{
			LoginSucceeded?.Invoke();
		}
	}

	public void ApplyLogin(LoginResponseDto data)
	{
		List<LoginContextSnapshot> availableContexts = data.Contexts?.Select((LoginContextDto x) => x.ToSnapshot()).ToList() ?? new List<LoginContextSnapshot>();
		string authoritativeUserName = string.IsNullOrWhiteSpace(data.UserName) ? SelectedUserName.Trim() : data.UserName.Trim();
		SessionContext.Current.SetFromLogin(data.UserId, authoritativeUserName, data.DisplayName, data.RoleCode ?? SelectedContext?.RoleCode ?? string.Empty, data.RoleName ?? SelectedContext?.RoleName ?? string.Empty, data.AccessToken, data.ActiveDeptId, data.ActiveDeptName ?? SelectedContext?.DeptName ?? string.Empty, data.ActiveDeptCategory, data.ActiveWardId, data.PrescribeLevel, data.SurgeryLevel, data.EmrSignLevel, data.RoleId, data.PostId, data.EmployeeNo, availableContexts, data.RefreshToken, data.AccessTokenExpiresAtMs, data.RefreshTokenExpiresAtMs);
	}
}
