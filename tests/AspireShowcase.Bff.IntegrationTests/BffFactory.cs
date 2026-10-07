using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Yarp.ReverseProxy.Forwarder;

namespace AspireShowcase.Bff.IntegrationTests;

/// <summary>
/// bff as the AppHost runs it, without Logto (so without sign-in), against PostgreSQL in a
/// container. web is a stub: the proxy's requests to it are recorded and answered with 200.
/// </summary>
public sealed class BffFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public StubWeb Web { get; } = new();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
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
    readonly ConcurrentDictionary<string, HttpRequestMessage> _received = new();

    public bool Received(string path) => _received.ContainsKey(path);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _received[request.RequestUri!.AbsolutePath] = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
