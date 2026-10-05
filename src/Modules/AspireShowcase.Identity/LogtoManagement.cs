using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspireShowcase.Identity;

/// <summary>The machine-to-machine application web calls Logto's Management API as.</summary>
sealed class LogtoManagementSettings
{
    public string? Endpoint { get; init; }
    public string? M2mAppId { get; init; }
    public string? M2mAppSecret { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(Endpoint) && !string.IsNullOrEmpty(M2mAppId) && !string.IsNullOrEmpty(M2mAppSecret);
}

/// <summary>
/// Calls Logto's Management API: gives users the "owner" role, which grants the
/// manage:business permission of web's API resource, and reads user profiles.
/// </summary>
sealed class LogtoManagement(HttpClient http, LogtoManagementSettings settings, TimeProvider time)
    : IOwnerRoles, IUserProfiles
{
    /// <summary>The user role in Logto that owners get; it has to exist in the Logto console.</summary>
    public const string OwnerRole = "owner";

    // In a self-hosted Logto, the Management API is the API resource of the "default" tenant.
    const string ManagementApiResource = "https://default.logto.app/api";

    // Shared by all instances: a typed client is created per request.
    static readonly SemaphoreSlim TokenLock = new(1, 1);
    static (string Value, DateTimeOffset ExpiresAt)? _token;
    static string? _ownerRoleId;

    /// <summary>Gives the user the owner role, unless they have it already.</summary>
    public async Task AssignOwnerRoleAsync(string userId, CancellationToken cancellation)
    {
        if (!settings.IsConfigured)
        {
            throw new OwnerRoleUnavailableException("The Logto machine-to-machine application isn't configured.");
        }

        try
        {
            var roleId = await OwnerRoleIdAsync(cancellation);
            var userRoles = $"api/users/{Uri.EscapeDataString(userId)}/roles";

            var current = await GetAsync<List<Role>>(userRoles, cancellation);
            if (current?.Any(role => role.Id == roleId) == true)
            {
                return;
            }

            using var assigned = await SendAsync(HttpMethod.Post, userRoles, new { roleIds = new[] { roleId } }, cancellation);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new OwnerRoleUnavailableException("Logto's Management API call failed.", exception);
        }
    }

    async Task<string> OwnerRoleIdAsync(CancellationToken cancellation)
    {
        if (_ownerRoleId is { } cached)
        {
            return cached;
        }

        var roles = await GetAsync<List<Role>>("api/roles?page=1&page_size=100", cancellation);
        return _ownerRoleId = roles?.FirstOrDefault(role => role is { Name: OwnerRole, Type: "User" })?.Id
            ?? throw new OwnerRoleUnavailableException($"Logto has no user role named \"{OwnerRole}\".");
    }

    public async Task<UserProfile?> FindAsync(string userId, CancellationToken cancellation)
    {
        if (!settings.IsConfigured)
        {
            throw new UserProfilesUnavailableException("The Logto machine-to-machine application isn't configured.");
        }

        try
        {
            var user = await GetAsync<User>($"api/users/{Uri.EscapeDataString(userId)}", cancellation);
            return user is null
                ? null
                : new UserProfile(
                    user.Id,
                    new[] { user.Name, user.Username, user.PrimaryEmail, user.Id }.First(value => !string.IsNullOrEmpty(value))!,
                    string.IsNullOrEmpty(user.PrimaryEmail) ? null : user.PrimaryEmail);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new UserProfilesUnavailableException("Logto's Management API call failed.", exception);
        }
    }

    async Task<T?> GetAsync<T>(string path, CancellationToken cancellation)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellation);
        return await response.Content.ReadFromJsonAsync<T>(cancellation);
    }

    /// <summary>Sends a Management API request; throws unless it succeeded.</summary>
    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellation));

        var response = await http.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"{method} {path} answered {(int)response.StatusCode}.", null, response.StatusCode);
        }
        return response;
    }

    // A client credentials token for the Management API, reused until shortly before it expires.
    async Task<string> TokenAsync(CancellationToken cancellation)
    {
        await TokenLock.WaitAsync(cancellation);
        try
        {
            if (_token is { } token && token.ExpiresAt > time.GetUtcNow().AddMinutes(1))
            {
                return token.Value;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "oidc/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["resource"] = ManagementApiResource,
                    ["scope"] = "all",
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.M2mAppId}:{settings.M2mAppSecret}")));

            using var response = await http.SendAsync(request, cancellation);
            response.EnsureSuccessStatusCode();
            var issued = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellation)
                ?? throw new JsonException("Empty token response.");

            _token = (issued.AccessToken, time.GetUtcNow().AddSeconds(issued.ExpiresIn));
            return issued.AccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    sealed record Role(string Id, string Name, string? Type);

    sealed record User(string Id, string? Name, string? Username, string? PrimaryEmail);

    sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
