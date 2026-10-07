using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

/// <summary>
/// The signed-in user's access token for web, refreshed with the refresh token shortly
/// before it expires and saved back to the session.
/// </summary>
/// <remarks>
/// Logto rotates refresh tokens: a used one stops working, so two requests refreshing at
/// once would fail the second. Refreshes are serialized per session, and a request that
/// waited reads the session again to pick up the other one's result. With several bff
/// replicas two can still race; the loser's request goes to web without a token and gets 401.
/// </remarks>
sealed class AccessTokens(
    ITicketStore sessions,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    LogtoSettings logto,
    TimeProvider time,
    ILogger<AccessTokens> logger)
{
    static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(1);

    // Striped rather than one lock per session, so the number of locks stays fixed.
    static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>The access token to send to web, or null when nobody is signed in or it can't be refreshed.</summary>
    public async Task<string?> GetAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded)
        {
            return null;
        }

        if (IsFresh(result.Properties))
        {
            return result.Properties.GetTokenValue("access_token");
        }

        if (!result.Properties.Items.TryGetValue(SessionStore.SessionIdKey, out var sessionId) || sessionId is null)
        {
            return null;
        }

        var gate = Locks[(uint)StringComparer.Ordinal.GetHashCode(sessionId) % Locks.Length];
        await gate.WaitAsync(context.RequestAborted);
        try
        {
            var ticket = await sessions.RetrieveAsync(sessionId);
            if (ticket is null)
            {
                return null;
            }

            if (!IsFresh(ticket.Properties))
            {
                if (!await RefreshAsync(ticket.Properties, context.RequestAborted))
                {
                    // Another replica may have just used the same refresh token, and saved the result.
                    ticket = await sessions.RetrieveAsync(sessionId);
                    return ticket is not null && IsFresh(ticket.Properties)
                        ? ticket.Properties.GetTokenValue("access_token")
                        : null;
                }
                await sessions.RenewAsync(sessionId, ticket);
            }

            return ticket.Properties.GetTokenValue("access_token");
        }
        finally
        {
            gate.Release();
        }
    }

    bool IsFresh(AuthenticationProperties properties) =>
        properties.GetTokenValue("expires_at") is { } expiresAt &&
        DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires) &&
        expires - RefreshBefore > time.GetUtcNow();

    async Task<bool> RefreshAsync(AuthenticationProperties properties, CancellationToken cancellation)
    {
        if (properties.GetTokenValue("refresh_token") is not { } refreshToken)
        {
            return false;
        }

        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellation);

        using var response = await options.Backchannel.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["resource"] = logto.ApiResource!,
            }), cancellation);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            // Typically an expired or revoked refresh token: the user has to sign in again.
            logger.LogWarning("Refreshing the access token failed with {StatusCode}", (int)response.StatusCode);
            return false;
        }
        // Any other failure is Logto's, not the session's: this request fails, the session stays.
        response.EnsureSuccessStatusCode();

        var refreshed = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellation);
        if (refreshed?.AccessToken is null)
        {
            return false;
        }

        var tokens = properties.GetTokens().ToDictionary(token => token.Name, token => token.Value);
        tokens["access_token"] = refreshed.AccessToken;
        tokens["expires_at"] = time.GetUtcNow().AddSeconds(refreshed.ExpiresIn)
            .ToString("o", CultureInfo.InvariantCulture);
        if (refreshed.RefreshToken is not null)
        {
            tokens["refresh_token"] = refreshed.RefreshToken;
        }
        properties.StoreTokens(tokens.Select(token => new AuthenticationToken { Name = token.Key, Value = token.Value }));
        return true;
    }

    sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
