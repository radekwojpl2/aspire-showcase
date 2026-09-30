var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

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

// Runtime settings for the React app. All of them are meant to be public: the browser SDKs
// need them. The Application Insights connection string is only set in Azure, and the Logto
// app ID only once an application has been created in the Logto console.
api.MapGet("/config", (IConfiguration config) => new ClientConfig(
    config["APPLICATIONINSIGHTS_CONNECTION_STRING"],
    config["Logto:Endpoint"],
    config["Logto:AppId"]))
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

record ClientConfig(string? ApplicationInsightsConnectionString, string? LogtoEndpoint, string? LogtoAppId);
