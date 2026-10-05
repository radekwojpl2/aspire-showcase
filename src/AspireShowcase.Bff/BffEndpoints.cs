using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;

/// <summary>What the React app knows about the session: whether sign-in is set up, and who is signed in.</summary>
record SessionInfo(bool SignInEnabled, SessionUser? User);

/// <param name="Email">From the ID token, when the account has one: pre-fills booking forms.</param>
/// <param name="IsOwner">Whether the access token has the owner role's permission.</param>
record SessionUser(string Name, string? Email, bool IsOwner);

static class BffEndpoints
{
    /// <summary>
    /// Sign-in, sign-out and the current user, under /bff. Logto returns to /signin-oidc and
    /// /signout-callback-oidc, which the OpenID Connect handler answers on its own.
    /// </summary>
    public static void MapBffEndpoints(this WebApplication app)
    {
        var bff = app.MapGroup("/bff");

        // A page navigation, not a fetch: it ends at Logto's sign-in page.
        bff.MapGet("/login", (string? returnUrl, bool? signup, LogtoSettings logto) =>
        {
            if (!logto.IsConfigured)
            {
                return Results.NotFound();
            }

            var properties = new AuthenticationProperties { RedirectUri = IsLocalUrl(returnUrl) ? returnUrl : "/" };
            if (signup == true)
            {
                properties.Items[LogtoAuthentication.SignUpKey] = "true";
            }
            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        });

        // A form post from the app, so the browser follows the redirect to Logto's sign-out.
        // Another site can't sign users out: the SameSite=Strict cookie isn't sent with its posts.
        bff.MapPost("/logout", (LogtoSettings logto) => Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            logto.IsConfigured
                ? [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]
                : [CookieAuthenticationDefaults.AuthenticationScheme]));

        bff.MapGet("/user", async (HttpContext context, LogtoSettings logto) =>
        {
            var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!result.Succeeded)
            {
                return new SessionInfo(logto.IsConfigured, null);
            }

            return new SessionInfo(true, new SessionUser(
                DisplayName(result.Principal),
                result.Principal.FindFirstValue("email"),
                HasScope(result.Properties.GetTokenValue("access_token"), LogtoAuthentication.ManageBusinessScope)));
        });

        app.MapFallback("/bff/{**path}", () => Results.NotFound());
    }

    // Only paths on this site, so /bff/login can't be used to send users elsewhere.
    static bool IsLocalUrl(string? url) =>
        url is ['/', ..] && !url.StartsWith("//") && !url.StartsWith("/\\");

    static string DisplayName(ClaimsPrincipal user) =>
        new[] { "name", "username", "email", "sub" }
            .Select(user.FindFirstValue)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? "";

    // The token came straight from Logto's token endpoint, so it's read, not validated again.
    static bool HasScope(string? accessToken, string scope)
    {
        if (accessToken is null)
        {
            return false;
        }

        try
        {
            return new JsonWebToken(accessToken).TryGetPayloadValue<string>("scope", out var scopes) &&
                scopes.Split(' ').Contains(scope);
        }
        catch (ArgumentException)
        {
            // Not a JWT: no permissions to read.
            return false;
        }
    }
}
