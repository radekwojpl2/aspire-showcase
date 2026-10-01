using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

// The app's PostgreSQL database. The AppHost passes its connection string as "app-db";
// this also adds a health check, retries, and traces and metrics for the queries.
builder.AddNpgsqlDbContext<AppDbContext>("app-db");

// Redis behind IDistributedCache, for the cached to-do list. Like the database, it comes
// with a health check and traces for the Redis commands.
builder.AddRedisDistributedCache("cache");

// Custom spans, span events and metrics for the to-do list.
builder.Services.AddSingleton<TodoTelemetry>();
builder.Services.AddSingleton<TodoListCache>();

// The notifications service, told about to-do items being added and removed. "notifications"
// is its resource name in the AppHost, resolved by service discovery.
builder.Services.AddHttpClient<NotificationsClient>(client =>
    client.BaseAddress = new Uri("https+http://notifications"));

// Failures that the Aspire dashboard can switch on, to see how they show up in telemetry.
builder.Services.AddSingleton<SimulatedFailures>();

builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Accepts Logto access tokens issued for this API's resource. Logto's issuer is its public
// URL + /oidc, and the signing keys come from its discovery document there.
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

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

var api = app.MapGroup("/api");

api.MapGet("/weatherforecast", (ILogger<Program> logger) =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();

    logger.LogInformation("Generated {Count} forecasts", forecast.Length);
    return forecast;
})
.WithName("GetWeatherForecast");

// Only for signed-in users: needs a Logto access token for this API (401 without one).
api.MapGet("/me", (ClaimsPrincipal user) => new CurrentUser(
    user.FindFirstValue("sub"),
    user.FindFirstValue("client_id"),
    user.FindFirstValue("scope")))
.RequireAuthorization()
.WithName("GetCurrentUser");

api.MapTodos();
api.MapNotifications();

if (app.Environment.IsDevelopment())
{
    // The switches for the simulated failures, called by the commands the AppHost
    // adds to this resource in the Aspire dashboard.
    api.MapSimulatedFailures();
}

// Runtime settings for the React app. All of them are meant to be public: the browser SDKs
// need them. The Application Insights connection string is only set in Azure, and the Logto
// app ID only once an application has been created in the Logto console.
api.MapGet("/config", (IConfiguration config) => new ClientConfig(
    config["APPLICATIONINSIGHTS_CONNECTION_STRING"],
    config["Logto:Endpoint"],
    config["Logto:AppId"],
    config["Logto:ApiResource"]))
.WithName("GetClientConfig");

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

// Serves the React app, which is copied into wwwroot when the container is published.
app.UseFileServer();

// Client-side routes such as /callback (where Logto returns after sign-in) get the app too,
// but unknown /api paths stay 404s rather than returning the page.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record ClientConfig(
    string? ApplicationInsightsConnectionString,
    string? LogtoEndpoint,
    string? LogtoAppId,
    string? LogtoApiResource);

record CurrentUser(string? Id, string? ClientId, string? Scopes);
