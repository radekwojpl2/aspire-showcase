using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace AspireShowcase.Mcp.Auth;

/// <summary>
/// Logto settings, from the AppHost: the public URL, the Native app MCP clients sign in as,
/// the application the MCP server exchanges tokens as, and web's API resource it exchanges
/// them for.
/// </summary>
sealed class LogtoSettings
{
    public string? Endpoint { get; init; }
    public string? ClientId { get; init; }
    public string? AppId { get; init; }
    public string? AppSecret { get; init; }
    public string? ApiResource { get; init; }

    public string Issuer => $"{Endpoint}/oidc";

    /// <summary>The settings that are missing, by the AppHost parameter that sets each.</summary>
    public IEnumerable<string> Missing()
    {
        if (string.IsNullOrEmpty(Endpoint)) yield return "Logto:Endpoint";
        if (string.IsNullOrEmpty(ClientId)) yield return "logto-mcp-client-id";
        if (string.IsNullOrEmpty(AppId)) yield return "logto-mcp-app-id";
        if (string.IsNullOrEmpty(AppSecret)) yield return "logto-mcp-app-secret";
        if (string.IsNullOrEmpty(ApiResource)) yield return "Logto:ApiResource";
    }
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
    /// Every tool needs it: there's no way in without signing in.
    /// </summary>
    /// <remarks>
    /// Only as the Native app registered in Logto for MCP clients: a token issued to any other
    /// application, or to a client Logto let in without registering it (its dynamic CIMD app),
    /// is refused. So which clients may connect is decided in the Logto console.
    ///
    /// The resource is the URL the client connects to (http://localhost:5068/mcp locally),
    /// read from the request rather than configured: in Azure the address is only known once
    /// deployed. Aspire has ASP.NET Core trust the Container Apps ingress's X-Forwarded-*
    /// headers there, so the request's scheme and host are the public ones.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The Logto applications aren't set up yet.</exception>
    public static void AddMcpAuthentication(this WebApplicationBuilder builder)
    {
        var logto = builder.Configuration.GetSection("Logto").Get<LogtoSettings>() ?? new();
        if (logto.Missing().ToList() is { Count: > 0 } missing)
        {
            throw new InvalidOperationException(
                $"The MCP server needs its Logto applications, and won't run without them. Missing: {string.Join(", ", missing)}. " +
                "Create them in the Logto console and set the AppHost parameters: see step 7 of .claude/skills/setup/SKILL.md.");
        }

        builder.Services.AddSingleton(logto);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMemoryCache();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient<WebTokens>();
        builder.Services.AddTransient<WebAccessTokenHandler>();

        builder.Services.AddAuthorization();
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
                // Logto puts the application the token was issued to in client_id, in every
                // access token it issues.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.FindFirst("client_id")?.Value != logto.ClientId)
                        {
                            context.Fail("The token wasn't issued to the MCP clients' application.");
                        }
                        return Task.CompletedTask;
                    },
                };
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

    /// <summary>Maps the MCP endpoint, for signed-in users only.</summary>
    public static void MapMcpEndpoints(this WebApplication app) => app.MapMcp(Path).RequireAuthorization();
}
