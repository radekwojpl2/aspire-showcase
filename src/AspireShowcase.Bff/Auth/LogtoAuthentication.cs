using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

/// <summary>
/// Logto settings, from the AppHost: the public URL, the "Traditional web" application bff
/// signs in with, and the API resource whose access tokens it passes on to web.
/// </summary>
sealed class LogtoSettings
{
    public string? Endpoint { get; init; }
    public string? AppId { get; init; }
    public string? AppSecret { get; init; }
    public string? ApiResource { get; init; }

    /// <summary>The application only exists once it's been created in the Logto console.</summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(Endpoint) && !string.IsNullOrEmpty(AppId) &&
        !string.IsNullOrEmpty(AppSecret) && !string.IsNullOrEmpty(ApiResource);
}

static class LogtoAuthentication
{
    /// <summary>How long a sign-in lasts. Fixed, not sliding: see the cookie options below.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Permission of the API resource that Logto's "owner" role grants. Requested at every
    /// sign-in; Logto only puts it in the access token for users who have the role.
    /// </summary>
    public const string ManageBusinessScope = "manage:business";

    /// <summary>Set by /bff/login?signup=true, so Logto opens on its sign-up form.</summary>
    public const string SignUpKey = ".bff.signup";

    /// <summary>The session cookie, which holds only the session's ID.</summary>
    public const string SessionCookie = "bff-session";

    /// <summary>
    /// Signs users in with Logto's authorization code flow as a confidential client (it has an
    /// app secret, which a browser app can't keep), and keeps the result in a server-side session.
    /// The browser gets only an HttpOnly session cookie; the tokens stay in bff-db.
    /// </summary>
    public static void AddLogtoAuthentication(this WebApplicationBuilder builder)
    {
        var logto = builder.Configuration.GetSection("Logto").Get<LogtoSettings>() ?? new();
        builder.Services.AddSingleton(logto);

        builder.Services.AddSingleton<ITicketStore, SessionStore>();
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<ITicketStore>((options, sessions) => options.SessionStore = sessions);

        var authentication = builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = SessionCookie;
                // Only sent with requests from the app's own pages, which is half of the CSRF
                // protection; the X-CSRF header on /api is the other half.
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = SessionLifetime;
                // A sliding renewal would write back the ticket this request started with, and
                // could overwrite tokens that AccessTokens refreshed in the meantime.
                options.SlidingExpiration = false;
                // Callers of bff are fetch calls: answer with a status, not a redirect.
                options.Events.OnRedirectToLogin = context => SetStatus(context.Response, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => SetStatus(context.Response, StatusCodes.Status403Forbidden);
            });

        // Until the Logto application exists, the app runs without sign-in.
        if (!logto.IsConfigured)
        {
            return;
        }

        authentication.AddOpenIdConnect(options =>
        {
            options.Authority = $"{logto.Endpoint}/oidc";
            options.ClientId = logto.AppId;
            options.ClientSecret = logto.AppSecret;
            // Locally Logto is served over plain HTTP.
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

            options.ResponseType = "code";
            // Logto sends the code back in the query of a plain redirect, so the correlation and
            // nonce cookies can be SameSite=Lax, and work on http://localhost too.
            options.ResponseMode = "query";
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

            options.Scope.Clear();
            foreach (var scope in new[] { "openid", "profile", "email", "offline_access", ManageBusinessScope })
            {
                options.Scope.Add(scope);
            }
            // Logto only issues a refresh token for offline_access when consent is prompted.
            // First-party applications are consented automatically, so users don't see a page.
            options.Prompt = "consent";
            // The access token is issued for web's API resource (RFC 8707 resource indicators).
            options.AdditionalAuthorizationParameters["resource"] = logto.ApiResource!;

            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = false;
            // Keep claim names as Logto sends them (sub, name, username, email).
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";

            options.Events.OnRedirectToIdentityProvider = context =>
            {
                if (context.Properties.Items.ContainsKey(SignUpKey))
                {
                    context.ProtocolMessage.SetParameter("first_screen", "register");
                }
                return Task.CompletedTask;
            };
            options.Events.OnAuthorizationCodeReceived = context =>
            {
                context.TokenEndpointRequest!.Resource = logto.ApiResource;
                return Task.CompletedTask;
            };
        });
    }

    static Task SetStatus(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
