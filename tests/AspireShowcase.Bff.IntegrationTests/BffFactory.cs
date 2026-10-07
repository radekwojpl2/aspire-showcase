using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Yarp.ReverseProxy.Forwarder;

namespace AspireShowcase.Bff.IntegrationTests;

/// <summary>
/// bff as the AppHost runs it, without Logto (so without sign-in), against PostgreSQL in a
/// container. web is a stub: the proxy's requests to it are recorded and answered with 200.
/// </summary>
public sealed class BffFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The claim the cookie handler keeps the session's key in, when it has a session store.
    const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public StubWeb Web { get; } = new();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A client that sends only the cookies a test gives it.</summary>
    public HttpClient CreateClientWithoutCookies() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>
    /// A session as signing in with Logto leaves it in bff-db, with an access token valid until
    /// <paramref name="accessTokenExpiresAt"/> and no refresh token, so it can't be refreshed.
    /// </summary>
    /// <returns>The session cookie ("bff-session=..."), and the session's key in the store.</returns>
    public async Task<(string Cookie, string SessionId)> SignInAsync(string accessToken, DateTimeOffset accessTokenExpiresAt)
    {
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = accessToken },
            new AuthenticationToken { Name = "expires_at", Value = accessTokenExpiresAt.ToString("o") },
        ]);
        var scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", $"user-{Guid.NewGuid():N}")], scheme));
        var sessionId = await Services.GetRequiredService<ITicketStore>()
            .StoreAsync(new AuthenticationTicket(user, properties, scheme));

        // The cookie holds only the session's key, as the cookie handler writes it.
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var cookieTicket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionIdClaim, sessionId)], scheme)), scheme);
        return ($"{options.Cookie.Name}={options.TicketDataFormat.Protect(cookieTicket)}", sessionId);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:bff-db", _postgres.GetConnectionString());
        // What service discovery resolves "web" to; never connected to, the stub answers instead.
        builder.UseSetting("services:web:http:0", "http://web.test");

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IForwarderHttpClientFactory>(new StubWebClientFactory(Web)));
    }

    sealed class StubWebClientFactory(StubWeb web) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(web);
    }
}

/// <summary>web, as far as the proxy can tell: every request it receives, by path.</summary>
public sealed class StubWeb : HttpMessageHandler
{
    readonly ConcurrentDictionary<string, string?> _authorization = new();

    public bool Received(string path) => _authorization.ContainsKey(path);

    /// <summary>The Authorization header of the request to <paramref name="path"/>, if it had one.</summary>
    public string? AuthorizationOf(string path) => _authorization[path];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _authorization[request.RequestUri!.AbsolutePath] = request.Headers.Authorization?.ToString();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
