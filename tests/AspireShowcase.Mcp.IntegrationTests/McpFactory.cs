using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// The MCP server as the AppHost runs it, with web replaced by a stub that answers what each
/// test tells it to and records what it was asked.
/// </summary>
public sealed class McpFactory : WebApplicationFactory<Program>
{
    public StubWeb Web { get; } = new();

    /// <summary>An MCP client connected to /mcp, as Claude Code or the MCP Inspector would be.</summary>
    public Task<McpClient> ConnectAsync(McpClientOptions? options = null) =>
        McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions { Endpoint = new Uri(Server.BaseAddress, "/mcp") },
                CreateClient(),
                ownsHttpClient: true),
            options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // What service discovery resolves "web" to; never connected to, the stub answers instead.
        builder.UseSetting("services:web:http:0", "http://web.test");

        builder.ConfigureTestServices(services =>
            services.AddHttpClient<WebApi>().ConfigurePrimaryHttpMessageHandler(() => Web));
    }
}

/// <summary>web, as far as the MCP server can tell: canned answers by method and path.</summary>
public sealed class StubWeb : HttpMessageHandler
{
    readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _answers = new();

    /// <summary>The requests it received, as "GET /api/...?query", with the body of each.</summary>
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
