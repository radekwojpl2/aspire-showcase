using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace AspireShowcase.Mcp.Auth;

/// <summary>
/// Access tokens for web, for the user who called a tool. The MCP client's token was issued
/// for the MCP server, and the MCP spec forbids passing it on; instead Logto exchanges it
/// (RFC 8693 token exchange) for a token for web's API resource, for the same user. The MCP
/// server asks as its own application, which has token exchange turned on in Logto.
/// </summary>
/// <remarks>
/// Logto only exchanges tokens issued to first-party applications, so MCP clients sign in as
/// the "Native app" created for them, not as Logto's dynamic (CIMD) app.
/// </remarks>
sealed class WebTokens(HttpClient http, LogtoSettings logto, IMemoryCache cache, TimeProvider time, ILogger<WebTokens> logger)
{
    static readonly TimeSpan ExpireBefore = TimeSpan.FromMinutes(1);

    /// <summary>A token for web, or null when nobody signed in.</summary>
    public async Task<string?> GetAsync(HttpContext context, CancellationToken cancellation)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            await context.GetTokenAsync("access_token") is not { } subjectToken)
        {
            return null;
        }

        // Cached by a hash of the token, so the cache never holds the token itself.
        var key = $"web-token:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subjectToken)))}";
        if (cache.TryGetValue<string>(key, out var cached))
        {
            return cached;
        }

        var exchanged = await ExchangeAsync(subjectToken, cancellation);
        var lifetime = TimeSpan.FromSeconds(exchanged.ExpiresIn) - ExpireBefore;
        if (lifetime > TimeSpan.Zero)
        {
            cache.Set(key, exchanged.AccessToken, time.GetUtcNow() + lifetime);
        }
        return exchanged.AccessToken;
    }

    async Task<TokenResponse> ExchangeAsync(string subjectToken, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{logto.Issuer}/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = subjectToken,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["resource"] = logto.ApiResource!,
                // Granted only if the user's roles have it: owners.
                ["scope"] = McpAuthentication.ManageBusinessScope,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{logto.AppId}:{logto.AppSecret}")));

        using var response = await http.SendAsync(request, cancellation);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            // Typically the application doesn't have token exchange turned on in Logto.
            logger.LogWarning("Exchanging the access token for web failed with {StatusCode}: {Body}",
                (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellation));
            throw new ModelContextProtocol.McpException(
                "The MCP server couldn't act for you: Logto refused the token exchange. Sign in again, " +
                "or ask whoever runs the server to check its Logto application.");
        }
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellation))!;
    }

    sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Sends web the user's token, for the tools that act for them.</summary>
sealed class WebAccessTokenHandler(IHttpContextAccessor http, WebTokens tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (http.HttpContext is { } context && await tokens.GetAsync(context, cancellationToken) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
