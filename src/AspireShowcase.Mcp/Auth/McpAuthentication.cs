using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace AspireShowcase.Mcp.Auth;

/// <summary>
/// Logto settings, from the AppHost: the public URL, the application the MCP server exchanges
/// tokens as, and web's API resource it exchanges them for.
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

    public string Issuer => $"{Endpoint}/oidc";
}

static class McpAuthentication
{
    /// <summary>The MCP endpoint; its URL is also the resource its access tokens are issued for.</summary>
    public const string Path = "/mcp";

    /// <summary>
    /// Permission of web's API resource that Logto's "owner" role grants, asked for when
    /// exchanging a token; Logto only grants it to users who have the role.
    /// </summary>
    public const string ManageBusinessScope = "manage:business";

    /// <summary>
    /// Signing in to the MCP server as the MCP authorization spec has it: the server is an
    /// OAuth protected resource, Logto its authorization server. A client without a token gets
    /// a 401 pointing at /.well-known/oauth-protected-resource/mcp, which names Logto; it signs
    /// the user in there and comes back with an access token issued for this server's URL.
    /// </summary>
    /// <remarks>
    /// The resource is the URL the client connects to (http://localhost:5068/mcp locally),
    /// read from the request rather than configured: in Azure the address is only known once
    /// deployed. Aspire has ASP.NET Core trust the Container Apps ingress's X-Forwarded-*
    /// headers there, so the request's scheme and host are the public ones.
    /// </remarks>
    public static void AddMcpAuthentication(this WebApplicationBuilder builder)
    {
        var logto = builder.Configuration.GetSection("Logto").Get<LogtoSettings>() ?? new();
        builder.Services.AddSingleton(logto);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMemoryCache();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient<WebTokens>();
        builder.Services.AddTransient<WebAccessTokenHandler>();

        builder.Services.AddAuthorization();

        // Until the Logto application exists, the server runs without sign-in: the public
        // tools only.
        if (!logto.IsConfigured)
        {
            builder.Services.AddAuthentication();
            return;
        }

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = logto.Issuer;
                // Locally Logto is served over plain HTTP.
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                // Keep claim names as Logto sends them (sub, scope, client_id).
                options.MapInboundClaims = false;
                // Kept for exchanging it for a token for web; see WebTokens.
                options.SaveToken = true;
            })
            .AddMcp(options =>
            {
                options.Events.OnResourceMetadataRequest = context =>
                {
                    context.ResourceMetadata = new ProtectedResourceMetadata
                    {
                        Resource = ResourceOf(context.HttpContext.Request),
                        AuthorizationServers = [logto.Issuer],
                        // offline_access: a refresh token, so the client stays signed in.
                        ScopesSupported = ["openid", "profile", "email", "offline_access"],
                        ResourceName = "Aspire Showcase bookings",
                    };
                    return Task.CompletedTask;
                };
            });

        // The audience is this server's own URL, which depends on the request; see above.
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHttpContextAccessor>((options, http) =>
                options.TokenValidationParameters.AudienceValidator = (audiences, _, _) =>
                    http.HttpContext is { } context && audiences.Contains(ResourceOf(context.Request)));
    }

    /// <summary>The MCP endpoint's public URL, which the access tokens must be issued for.</summary>
    public static string ResourceOf(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}{Path}";

    /// <summary>
    /// Maps the MCP endpoints: /mcp for everything, which needs signing in once Logto is set
    /// up, and /mcp/public for the public tools without signing in, for clients that only
    /// look things up.
    /// </summary>
    public static void MapMcpEndpoints(this WebApplication app)
    {
        var mcp = app.MapMcp(Path);
        if (app.Services.GetRequiredService<LogtoSettings>().IsConfigured)
        {
            mcp.RequireAuthorization();
        }

        app.MapMcp($"{Path}/public");
    }
}

