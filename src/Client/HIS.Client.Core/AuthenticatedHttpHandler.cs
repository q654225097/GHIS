using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Client.Core;

public sealed class AuthenticatedHttpHandler : DelegatingHandler
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly SessionContext _session;
	private readonly SemaphoreSlim _refreshLock = new(1, 1);
	private readonly HttpClient _refreshClient;
	private bool _disposed;

	public AuthenticatedHttpHandler(SessionContext session, HttpMessageHandler? innerHandler = null)
	{
		_session = session;
		InnerHandler = innerHandler ?? new HttpClientHandler();
		_refreshClient = new HttpClient
		{
			BaseAddress = new Uri(ApiEndpoints.BaseAddress),
			Timeout = TimeSpan.FromSeconds(10)
		};
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		if (IsAnonymousAuthRequest(request) || request.Headers.Authorization != null)
		{
			return await base.SendAsync(request, cancellationToken);
		}

		string tokenUsed = _session.AccessToken;
		AttachBearer(request, tokenUsed);
		HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
		if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrWhiteSpace(tokenUsed))
		{
			return response;
		}

		byte[]? requestBody = await ReadRequestBodyAsync(request, cancellationToken);
		AuthenticationSnapshot requestSnapshot = AuthenticationSnapshot.Capture(request);
		response.Dispose();

		await _refreshLock.WaitAsync(cancellationToken);
		try
		{
			string currentToken = _session.AccessToken;
			if (string.Equals(currentToken, tokenUsed, StringComparison.Ordinal))
			{
				if (!await RefreshAsync(cancellationToken))
				{
					return await SendWithoutAuthenticationAsync(requestSnapshot, requestBody, cancellationToken);
				}
				currentToken = _session.AccessToken;
			}

			using HttpRequestMessage retry = requestSnapshot.CreateRequest(requestBody);
			AttachBearer(retry, currentToken);
			HttpResponseMessage retryResponse = await base.SendAsync(retry, cancellationToken);
			retryResponse.RequestMessage = retry;
			return retryResponse;
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	private async Task<bool> RefreshAsync(CancellationToken cancellationToken)
	{
		string refreshToken = _session.RefreshToken;
		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			return false;
		}

		HttpResponseMessage response;
		try
		{
			response = await _refreshClient.PostAsJsonAsync(
				"/api/v4/platform/auth/refresh",
				new { RefreshToken = refreshToken },
				JsonOptions,
				cancellationToken);
		}
		catch (HttpRequestException)
		{
			return false;
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return false;
		}

		using (response)
		{
			TokenEnvelope? envelope = null;
			try
			{
				envelope = await response.Content.ReadFromJsonAsync<TokenEnvelope>(JsonOptions, cancellationToken);
			}
			catch (JsonException)
			{
				return false;
			}

			RefreshTokenDto? data = envelope?.Data;
			if (!response.IsSuccessStatusCode || envelope?.Code != 200 || data == null
				|| string.IsNullOrWhiteSpace(data.AccessToken)
				|| string.IsNullOrWhiteSpace(data.RefreshToken))
			{
				if (response.StatusCode == HttpStatusCode.Unauthorized)
				{
					_session.Logout();
					_session.NotifySessionExpired();
				}
				return false;
			}

			_session.UpdateTokens(
				data.AccessToken,
				data.RefreshToken,
				data.AccessTokenExpiresAtMs,
				data.RefreshTokenExpiresAtMs);
			return true;
		}
	}

	private async Task<HttpResponseMessage> SendWithoutAuthenticationAsync(
		AuthenticationSnapshot snapshot,
		byte[]? body,
		CancellationToken cancellationToken)
	{
		using HttpRequestMessage retry = snapshot.CreateRequest(body);
		retry.Headers.Authorization = null;
		HttpResponseMessage response = await base.SendAsync(retry, cancellationToken);
		response.RequestMessage = retry;
		return response;
	}

	private static async Task<byte[]?> ReadRequestBodyAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		if (request.Content == null)
		{
			return null;
		}

		return await request.Content.ReadAsByteArrayAsync(cancellationToken);
	}

	private static bool IsAnonymousAuthRequest(HttpRequestMessage request)
	{
		string path = request.RequestUri?.AbsolutePath ?? string.Empty;
		return path.EndsWith("/api/v4/platform/auth/login", StringComparison.OrdinalIgnoreCase)
			|| path.EndsWith("/api/v4/platform/auth/refresh", StringComparison.OrdinalIgnoreCase)
			|| path.EndsWith("/api/v4/platform/auth/contexts", StringComparison.OrdinalIgnoreCase);
	}

	private static void AttachBearer(HttpRequestMessage request, string accessToken)
	{
		request.Headers.Authorization = string.IsNullOrWhiteSpace(accessToken)
			? null
			: new AuthenticationHeaderValue("Bearer", accessToken);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && !_disposed)
		{
			_disposed = true;
			_refreshClient.Dispose();
			_refreshLock.Dispose();
		}
		base.Dispose(disposing);
	}

	private sealed class TokenEnvelope
	{
		public int Code { get; set; }
		public RefreshTokenDto? Data { get; set; }
	}

	private sealed class RefreshTokenDto
	{
		public string AccessToken { get; set; } = string.Empty;
		public string RefreshToken { get; set; } = string.Empty;
		public long AccessTokenExpiresAtMs { get; set; }
		public long RefreshTokenExpiresAtMs { get; set; }
	}

	private sealed class AuthenticationSnapshot
	{
		private readonly HttpMethod _method;
		private readonly Uri? _requestUri;
		private readonly Version _version;
		private readonly HttpVersionPolicy _versionPolicy;
		private readonly List<KeyValuePair<string, IEnumerable<string>>> _headers;
		private readonly List<KeyValuePair<string, IEnumerable<string>>> _contentHeaders;

		private AuthenticationSnapshot(
			HttpMethod method,
			Uri? requestUri,
			Version version,
			HttpVersionPolicy versionPolicy,
			List<KeyValuePair<string, IEnumerable<string>>> headers,
			List<KeyValuePair<string, IEnumerable<string>>> contentHeaders)
		{
			_method = method;
			_requestUri = requestUri;
			_version = version;
			_versionPolicy = versionPolicy;
			_headers = headers;
			_contentHeaders = contentHeaders;
		}

		public static AuthenticationSnapshot Capture(HttpRequestMessage request)
		{
			return new AuthenticationSnapshot(
				request.Method,
				request.RequestUri,
				request.Version,
				request.VersionPolicy,
				request.Headers
					.Where(header => !string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
					.Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray()))
					.ToList(),
				request.Content?.Headers
					.Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray()))
					.ToList() ?? new List<KeyValuePair<string, IEnumerable<string>>>());
		}

		public HttpRequestMessage CreateRequest(byte[]? body)
		{
			var request = new HttpRequestMessage(_method, _requestUri)
			{
				Version = _version,
				VersionPolicy = _versionPolicy
			};
			foreach (var header in _headers)
			{
				request.Headers.TryAddWithoutValidation(header.Key, header.Value);
			}
			if (body != null)
			{
				request.Content = new ByteArrayContent(body);
				foreach (var header in _contentHeaders)
				{
					request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
				}
			}
			return request;
		}
	}
}

public static class AuthenticatedHttpClientFactory
{
	public static HttpClient Create(
		SessionContext? session = null,
		HttpMessageHandler? transport = null,
		TimeSpan? timeout = null)
	{
		return new HttpClient(new AuthenticatedHttpHandler(session ?? SessionContext.Current, transport))
		{
			BaseAddress = new Uri(ApiEndpoints.BaseAddress),
			Timeout = timeout ?? TimeSpan.FromSeconds(30)
		};
	}
}
