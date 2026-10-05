using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

// The app's PostgreSQL database. The AppHost passes its connection string as "app-db";
// this also adds a health check, retries, and traces and metrics for the queries.
builder.AddNpgsqlDbContext<AppDbContext>("app-db");

// Custom spans and metrics for businesses.
builder.Services.AddSingleton<BusinessTelemetry>();

// Logto's Management API, for giving owners their role. Called as the machine-to-machine
// application the AppHost passes in; Logto's public URL is its address.
var logtoManagement = builder.Configuration.GetSection("Logto").Get<LogtoManagementSettings>() ?? new();
builder.Services.AddSingleton(logtoManagement);
builder.Services.AddHttpClient<LogtoManagement>(client =>
{
    if (!string.IsNullOrEmpty(logtoManagement.Endpoint))
    {
        client.BaseAddress = new Uri($"{logtoManagement.Endpoint}/");
    }
});

builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Accepts Logto access tokens issued for this API's resource, which bff adds to the requests
// it forwards. Logto's issuer is its public URL + /oidc, and the signing keys come from its
// discovery document there.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var logtoEndpoint = builder.Configuration["Logto:Endpoint"];
        if (!string.IsNullOrEmpty(logtoEndpoint))
        {
            options.Authority = $"{logtoEndpoint}/oidc";
        }
        options.Audience = builder.Configuration["Logto:ApiResource"];
        // Locally Logto is served over plain HTTP.
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        // Keep claim names as Logto sends them (sub, scope, client_id).
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();

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

api.MapBusinesses();

// Runtime settings for the React app, which gets them through bff. The Application Insights
// connection string is public by design (the browser SDK needs it) and only set in Azure.
api.MapGet("/config", (IConfiguration config) => new ClientConfig(
    config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
.WithName("GetClientConfig");

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

app.Run();

record ClientConfig(string? ApplicationInsightsConnectionString);
