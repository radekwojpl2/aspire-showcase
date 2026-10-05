using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// Business Setup, what an owner configures: the business, its booking link and its opening
/// hours (user stories MVP-8 and MVP-9). This is the module's whole public surface; the domain
/// model and its storage are internal to the project.
/// </summary>
public static class BusinessSetupModule
{
    /// <summary>The name of the module's spans and metrics.</summary>
    public const string TelemetryName = "AspireShowcase.BusinessSetup";

    /// <param name="connectionName">The database the AppHost passes in, holding the module's tables.</param>
    public static void AddBusinessSetup(this IHostApplicationBuilder builder, string connectionName)
    {
        // This adds a health check, retries, and traces and metrics for the queries.
        builder.AddNpgsqlDbContext<BusinessSetupDbContext>(
            connectionName, configureDbContextOptions: BusinessSetupDbContext.Configure);

        builder.Services.AddSingleton<BusinessTelemetry>();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(TelemetryName))
            .WithMetrics(metrics => metrics.AddMeter(TelemetryName));
    }

    /// <summary>Maps the module's endpoints, under /businesses.</summary>
    public static void MapBusinessSetup(this IEndpointRouteBuilder api) => BusinessSetupEndpoints.MapEndpoints(api);

    /// <summary>
    /// Creates or updates the module's tables. Enough for a single instance; with several, run
    /// migrations as a separate step so they don't race.
    /// </summary>
    public static async Task MigrateBusinessSetupAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BusinessSetupDbContext>().Database.MigrateAsync();
    }
}
