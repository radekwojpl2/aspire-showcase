using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

// bff's own database, for sessions and data protection keys. The AppHost passes its
// connection string as "bff-db".
builder.AddNpgsqlDbContext<BffDbContext>("bff-db");

// The keys that encrypt the session cookie and the stored sessions. Kept in the database so a
// restart or another replica can still read the cookies already handed out.
builder.Services.AddDataProtection()
    .SetApplicationName("AspireShowcase.Bff")
    .PersistKeysToDbContext<BffDbContext>();

builder.AddLogtoAuthentication();
builder.AddApiProxy();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Creates or updates the tables on startup. Enough for a single instance; with several,
// run migrations as a separate step so they don't race.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<BffDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseCsrfHeaderCheck();

app.MapBffEndpoints();
app.MapReverseProxy();

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

// Serves the React app, which is copied into wwwroot when the container is published.
// Client-side routes such as /start get the app too.
app.UseFileServer();
app.MapFallbackToFile("index.html");

app.Run();
