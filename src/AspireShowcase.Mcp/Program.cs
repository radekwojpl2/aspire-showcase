using AspireShowcase.Mcp;
using AspireShowcase.Mcp.Auth;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

// Signing in with Logto, which every request needs, and tokens for web for the user.
builder.AddMcpAuthentication();

// The API, as service discovery resolves "web": the AppHost's reference passes its address.
// Requests carry the signed-in user's token for web.
builder.Services.AddHttpClient<WebApi>(client => client.BaseAddress = new Uri("https+http://web"))
    .AddHttpMessageHandler<WebAccessTokenHandler>();

// The MCP server, over Streamable HTTP. Clients on the initialize handshake get a session,
// which lets a tool ask its user something mid-call (elicitation); newer clients are served
// statelessly and get asked through multi round-trip requests instead. The authorization
// filters check [Authorize] on tools, as the SDK asks every ASP.NET Core server to.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients)
    .AddAuthorizationFilters()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcpEndpoints();

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

app.Run();
