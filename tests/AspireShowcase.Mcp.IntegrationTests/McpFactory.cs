using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AspireShowcase.Mcp.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// The MCP server as the AppHost runs it once its Logto applications exist. web and Logto are
/// stubs: web answers what each test tells it to and records what it was asked; Logto's tokens
/// are signed with a test key, and its token endpoint exchanges any token for
/// <see cref="ExchangedToken"/>.
/// </summary>
public class McpFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "http://logto.test/oidc";
    public const string ClientId = "mcp-clients";
    public const string ExchangedToken = "token-for-web";

    static readonly SymmetricSecurityKey SigningKey = new("a-test-key-of-at-least-256-bits-long!!"u8.ToArray());

    public StubWeb Web { get; } = new();

    public StubLogto Logto { get; } = new();

    /// <summary>The MCP server's clock: Monday 2 November 2026, 08:00 in Warsaw.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// An MCP client connected to /mcp, as Claude Code would be, signed
    /// in with <paramref name="accessToken"/>: by default a token of the MCP clients' app.
    /// </summary>
    public Task<McpClient> ConnectAsync(McpClientOptions? options = null, string? accessToken = null) =>
        McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(Server.BaseAddress, "/mcp"),
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken ?? AccessToken()}" },
                },
                CreateClient(),
                ownsHttpClient: true),
            options);

    /// <summary>An access token Logto would issue to <paramref name="clientId"/> for <paramref name="audience"/>.</summary>
    public static string AccessToken(
        string audience = "http://localhost/mcp", string clientId = ClientId, string userId = "user-1") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", userId), new Claim("client_id", clientId)]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // What service discovery resolves "web" to; never connected to, the stub answers instead.
        builder.UseSetting("services:web:http:0", "http://web.test");
        builder.UseSetting("Logto:Endpoint", "http://logto.test");
        builder.UseSetting("Logto:ClientId", ClientId);
        builder.UseSetting("Logto:AppId", "mcp-server");
        builder.UseSetting("Logto:AppSecret", "mcp-secret");
        builder.UseSetting("Logto:ApiResource", "https://api.aspire-showcase");

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<WebApi>().ConfigurePrimaryHttpMessageHandler(() => Web);
            services.AddHttpClient<WebTokens>().ConfigurePrimaryHttpMessageHandler(() => Logto);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            // Validates the test tokens, instead of fetching Logto's signing keys.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.Configuration = new() { Issuer = Issuer };
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }
}

/// <summary>Logto's token endpoint: exchanges any token, and records the requests.</summary>
public sealed class StubLogto : HttpMessageHandler
{
    public ConcurrentQueue<Dictionary<string, string>> Exchanges { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
            .Select(pair => pair.Split('='))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        Exchanges.Enqueue(form);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { access_token = McpFactory.ExchangedToken, expires_in = 3600, token_type = "Bearer" }),
        };
    }
}

/// <summary>web, as far as the MCP server can tell: canned answers by method and path.</summary>
public sealed class StubWeb : HttpMessageHandler
{
    readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _answers = new();

    /// <summary>The requests it received, as "GET /api/...?query", with the body and Authorization header of each.</summary>
    public ConcurrentQueue<(string Request, string? Body, string? Authorization)> Received { get; } = new();

    public void Answer(HttpMethod method, string pathAndQuery, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        _answers[$"{method} {pathAndQuery}"] = () => new HttpResponseMessage(status) { Content = JsonContent.Create(body) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Received.Enqueue((key, body, request.Headers.Authorization?.ToString()));

        return _answers.TryGetValue(key, out var answer)
            ? answer()
            : new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { title = "Not found." }, mediaType: new("application/problem+json")),
            };
    }
}
