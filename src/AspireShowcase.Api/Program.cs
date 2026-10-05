using AspireShowcase.Api.BusinessSetup;
using AspireShowcase.Api.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

// The app's PostgreSQL database. The AppHost passes its connection string as "app-db";
// this also adds a health check, retries, and traces and metrics for the queries.
builder.AddNpgsqlDbContext<AppDbContext>("app-db");

// The modules, as described in docs/architecture/ddd-modules.md. Identity & Access is the
// anti-corruption layer over Logto: token validation, the owner policy, the Management API.
builder.AddIdentityAccess();
builder.Services.AddSingleton<BusinessTelemetry>();

builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Creates or updates the tables on startup. Enough for a single instance; with several,
// run migrations as a separate step so they don't race.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

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

// Runtime settings for the React app, which gets them through bff. The Application Insights
// connection string is public by design (the browser SDK needs it) and only set in Azure.
api.MapGet("/config", (IConfiguration config) => new ClientConfig(
    config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
.WithName("GetClientConfig");

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

app.Run();

record ClientConfig(string? ApplicationInsightsConnectionString);
