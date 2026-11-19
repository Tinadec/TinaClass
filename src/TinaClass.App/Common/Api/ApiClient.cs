using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TinaClass.Contracts;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.App.Common.Api;

/// <summary>
/// Calls the API: JSON through <see cref="ContractsJsonContext"/>, the access token as a Bearer header, one refresh and retry
/// when it has expired, problem details turned into <see cref="ServiceException"/>s. The feature folders' <c>Api*</c> services use
/// it; nothing else talks HTTP. A call that needs a user while nobody is signed in waits for the sign-in
/// (<see cref="ApiSession.WaitForSignInAsync"/>).
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly ApiSession _session;
    private readonly TokenStore _tokens;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private TokenPair? _pair;

    public ApiClient(ApiSession session, TokenStore tokens, HttpMessageHandler? handler = null)
    {
        _session = session;
        _tokens = tokens;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = new Uri(ApiSettings.Current.ApiBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.PackageId.Replace('.', '-'), AppInfo.Current.Version));
        // Names this device in the user's sessions. Header values must be ASCII: another name is sent percent-encoded.
        var device = $"{OperatingSystemName()} - {AppInfo.Current.Name} {AppInfo.Current.Version}";
        _http.DefaultRequestHeaders.Add("X-Client-Device", device.All(char.IsAscii) ? device : Uri.EscapeDataString(device));
    }

    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public ApiSession Session => _session;

    /// <summary>Whether a refresh token is kept from an earlier "remember me" sign-in.</summary>
    public bool HasRememberedSession => _tokens.Load() is not null;

    /// <summary>Signs in with email and password; the session is then open and the user is returned.</summary>
    public async Task<CurrentUser> SignInAsync(string email, string password, bool remember, CancellationToken cancellation = default)
    {
        var pair = await PostAsync(ApiRoutes.Auth + "/login", new LoginRequest(email.Trim(), password, remember), Json.LoginRequest, Json.TokenPair,
            cancellation, authenticate: false);
        UseTokens(pair, remember);
        return await CompleteSignInAsync(cancellation);
    }

    /// <summary>Exchanges the remembered refresh token for a session; null when there is none or it was refused.</summary>
    public async Task<CurrentUser?> ResumeAsync(CancellationToken cancellation = default)
    {
        if (_tokens.Load() is not { } refresh)
        {
            return null;
        }
        _pair = new TokenPair(string.Empty, DateTimeOffset.MinValue, refresh);
        return await RefreshAsync(cancellation) ? await CompleteSignInAsync(cancellation) : null;
    }

    /// <summary>Signs out on the server (best effort), forgets the tokens and ends the session.</summary>
    public async Task SignOutAsync()
    {
        if (_pair is { } pair)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Post, ApiRoutes.Auth.TrimStart('/') + "/logout",
                    JsonContent.Create(new RefreshRequest(pair.RefreshToken), Json.RefreshRequest), authenticate: false, CancellationToken.None);
            }
            catch (ServiceException)
            {
                // Signing out here is what matters.
            }
        }
        _pair = null;
        _tokens.Clear();
        _session.SignOut();
    }

    private async Task<CurrentUser> CompleteSignInAsync(CancellationToken cancellation)
    {
        var user = await GetAsync(ApiRoutes.Me, Json.CurrentUser, cancellation);
        _session.SignIn(user);
        return user;
    }

    private void UseTokens(TokenPair pair, bool remember)
    {
        _pair = pair;
        if (remember)
        {
            _tokens.Save(pair.RefreshToken);
        }
        else
        {
            _tokens.Clear();
        }
    }

    public async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, authenticate: true, cancellation);
        return await ReadAsync(response, type, cancellation);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType, CancellationToken cancellation = default, bool authenticate = true)
    {
        using var response = await SendAsync(HttpMethod.Post, path, JsonContent.Create(body, requestType), authenticate, cancellation);
        return await ReadAsync(response, responseType, cancellation);
    }

    public async Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Put, path, JsonContent.Create(body, requestType), authenticate: true, cancellation);
        return await ReadAsync(response, responseType, cancellation);
    }

    public async Task<TResponse> PatchAsync<TRequest, TResponse>(string path, TRequest body, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Patch, path, JsonContent.Create(body, requestType), authenticate: true, cancellation);
        return await ReadAsync(response, responseType, cancellation);
    }

    /// <summary>A POST whose answer is not needed (204 or a body that is ignored).</summary>
    public async Task PostAsync<TRequest>(string path, TRequest body, JsonTypeInfo<TRequest> requestType, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Post, path, JsonContent.Create(body, requestType), authenticate: true, cancellation);
    }

    /// <summary>A POST with no body (publish, cancel, refresh, stop) that answers with a value.</summary>
    public async Task<TResponse> PostAsync<TResponse>(string path, JsonTypeInfo<TResponse> responseType, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Post, path, null, authenticate: true, cancellation);
        return await ReadAsync(response, responseType, cancellation);
    }

    public async Task PostEmptyAsync(string path, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Post, path, null, authenticate: true, cancellation);
    }

    /// <summary>A DELETE that answers with a value (clearing a provider's key answers with the provider).</summary>
    public async Task<TResponse> DeleteAsync<TResponse>(string path, JsonTypeInfo<TResponse> responseType, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, path, null, authenticate: true, cancellation);
        return await ReadAsync(response, responseType, cancellation);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, path, null, authenticate: true, cancellation);
    }

    /// <summary>
    /// POSTs a body and reads the answer as Server-Sent Events: each <c>event:</c> name with its <c>data:</c> text, as they arrive. The
    /// request has no total time limit (the stream lives as long as the answer); cancel the token to stop reading.
    /// </summary>
    public async IAsyncEnumerable<(string Event, string Data)> StreamAsync<TRequest>(string path, TRequest body, JsonTypeInfo<TRequest> requestType,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Post, path, JsonContent.Create(body, requestType), authenticate: true, cancellation, HttpCompletionOption.ResponseHeadersRead);
        await foreach (var item in ReadEventsAsync(response, cancellation))
        {
            yield return item;
        }
    }

    /// <summary>The same for a POST without a body (regenerate).</summary>
    public async IAsyncEnumerable<(string Event, string Data)> StreamAsync(string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation = default)
    {
        using var response = await SendAsync(HttpMethod.Post, path, null, authenticate: true, cancellation, HttpCompletionOption.ResponseHeadersRead);
        await foreach (var item in ReadEventsAsync(response, cancellation))
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<(string Event, string Data)> ReadEventsAsync(HttpResponseMessage response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var reader = new StreamReader(stream);
        string? name = null;
        var data = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync(cancellation) is { } line)
        {
            if (line.Length == 0)
            {
                if (name is not null || data.Length > 0)
                {
                    yield return (name ?? "message", data.ToString());
                }
                name = null;
                data.Clear();
            }
            else if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line[5..].TrimStart());
            }
        }
    }

    private static string OperatingSystemName() =>
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Desktop";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, bool authenticate, CancellationToken cancellation,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        if (authenticate && _pair is null)
        {
            await _session.WaitForSignInAsync(cancellation);
        }
        var buffered = content is null ? null : await content.ReadAsByteArrayAsync(cancellation);
        var mediaType = content?.Headers.ContentType;
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path.TrimStart('/'));
            if (buffered is not null)
            {
                request.Content = new ByteArrayContent(buffered);
                request.Content.Headers.ContentType = mediaType;
            }
            if (authenticate && _pair is { AccessToken.Length: > 0 } pair)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pair.AccessToken);
            }
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, completion, cancellation);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
            {
                throw new ServiceException(ServiceException.Unreachable, inner: exception);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && authenticate && attempt == 0 && _pair is not null)
            {
                response.Dispose();
                if (await RefreshAsync(cancellation))
                {
                    continue;
                }
                _session.SignOut(ErrorText.For(ServiceException.SessionExpired));
                throw new ServiceException(ServiceException.SessionExpired);
            }
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadProblemAsync(response, cancellation);
                response.Dispose();
                throw error;
            }
            return response;
        }
    }

    /// <summary>One refresh at a time: the server accepts a refresh token once, so two parallel calls must not both use it.</summary>
    private async Task<bool> RefreshAsync(CancellationToken cancellation)
    {
        var used = _pair;
        await _refreshing.WaitAsync(cancellation);
        try
        {
            if (_pair is not { } pair)
            {
                return false;
            }
            if (!ReferenceEquals(pair, used))
            {
                // Another call refreshed while this one waited: its new tokens are in place, use them.
                return true;
            }
            using var response = await SendAsync(HttpMethod.Post, ApiRoutes.Auth.TrimStart('/') + "/refresh",
                JsonContent.Create(new RefreshRequest(pair.RefreshToken), Json.RefreshRequest), authenticate: false, cancellation);
            var fresh = await ReadAsync(response, Json.TokenPair, cancellation);
            UseTokens(fresh, remember: _tokens.Load() is not null);
            return true;
        }
        catch (ServiceException exception) when (exception.Code != ServiceException.Unreachable)
        {
            _pair = null;
            _tokens.Clear();
            return false;
        }
        finally
        {
            _refreshing.Release();
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellation)
    {
        var value = await response.Content.ReadFromJsonAsync(type, cancellation);
        return value ?? throw new ServiceException("empty_response");
    }

    private static async Task<ServiceException> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        var fallback = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ErrorCodes.Unauthorized,
            HttpStatusCode.Forbidden => ErrorCodes.Forbidden,
            HttpStatusCode.NotFound => ErrorCodes.NotFound,
            HttpStatusCode.Conflict => ErrorCodes.Conflict,
            HttpStatusCode.BadRequest => ErrorCodes.Validation,
            HttpStatusCode.TooManyRequests => ErrorCodes.RateLimited,
            _ => "http_" + (int)response.StatusCode,
        };
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : fallback;
            var fields = new Dictionary<string, string[]>();
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    fields[field.Name] = field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray()
                        : [field.Value.ToString()];
                }
            }
            return new ServiceException(code, fields);
        }
        catch (JsonException)
        {
            return new ServiceException(fallback);
        }
    }
}
