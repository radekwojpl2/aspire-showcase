using AspireShowcase.BusinessSetup;
using AspireShowcase.Identity;
using AspireShowcase.Scheduling;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

// The modules, as described in docs/architecture/ddd-modules.md, each a project in src/Modules.
// Identity & Access is the anti-corruption layer over Logto. Business Setup and Scheduling keep
// their tables in the app's PostgreSQL database, whose connection string the AppHost passes as
// "app-db", each in its own schema.
builder.AddIdentityAccess();
builder.AddBusinessSetup("app-db");
builder.AddScheduling("app-db");

// The message bus: RabbitMQ, whose connection string the AppHost passes as "messaging". Modules
// add their part (Scheduling: its outbox and consumers); failed messages are retried, waiting
// longer each time, before they end up in an _error queue.
builder.Services.AddMassTransit(bus =>
{
    bus.SetKebabCaseEndpointNameFormatter();
    bus.AddSchedulingMessaging();
    bus.UsingRabbitMq((context, rabbit) =>
    {
        rabbit.Host(new Uri(builder.Configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("The messaging connection string is missing.")));
        rabbit.UseMessageRetry(retry => retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5)));
        rabbit.ConfigureEndpoints(context);
    });
});

builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Each module creates or updates its own tables on startup.
await app.Services.MigrateBusinessSetupAsync();
await app.Services.MigrateSchedulingAsync();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI at /swagger, reading the document that MapOpenApi serves.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AspireShowcase API"));
}

app.UseHttpsRedirection();

var api = app.MapGroup("/api");

api.MapBusinessSetup();
// Sample bookings are a development tool, called from the Aspire dashboard (see the AppHost).
api.MapScheduling(includeDevelopmentTools: app.Environment.IsDevelopment());

// Runtime settings for the React app, which gets them through bff. The Application Insights
// connection string is public by design (the browser SDK needs it) and only set in Azure.
api.MapGet("/config", (IConfiguration config) => new ClientConfig(
    config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
.WithName("GetClientConfig");

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

app.Run();

record ClientConfig(string? ApplicationInsightsConnectionString);
