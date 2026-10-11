using AspireShowcase.Mcp;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

// The API, as service discovery resolves "web": the AppHost's reference passes its address.
builder.Services.AddHttpClient<WebApi>(client => client.BaseAddress = new Uri("https+http://web"));

// The MCP server, over Streamable HTTP at /mcp. Clients on the initialize handshake get a
// session, which lets a tool ask its user something mid-call (elicitation); newer clients are
// served statelessly and get asked through multi round-trip requests instead.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients)
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

app.Run();
