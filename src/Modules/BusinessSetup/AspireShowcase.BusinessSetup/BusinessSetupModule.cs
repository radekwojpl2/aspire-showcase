using AspireShowcase.BusinessSetup.Application;
using AspireShowcase.BusinessSetup.Application.Businesses;
using AspireShowcase.BusinessSetup.Application.Services;
using AspireShowcase.BusinessSetup.Application.Staff;
using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// Business Setup, what an owner configures: the business, its booking link, opening hours,
/// services and staff (user stories MVP-8 to MVP-11). This class is the module's whole public
/// surface; other modules talk to it only through AspireShowcase.BusinessSetup.PublicClient.
/// </summary>
public static class BusinessSetupModule
{
    /// <summary>The name of the module's spans and metrics.</summary>
    public const string TelemetryName = BusinessTelemetry.Name;

    /// <param name="connectionName">The database the AppHost passes in, holding the module's tables.</param>
    public static void AddBusinessSetup(this IHostApplicationBuilder builder, string connectionName)
    {
        // This adds a health check, retries, and traces and metrics for the queries.
        builder.AddNpgsqlDbContext<BusinessSetupDbContext>(
            connectionName, configureDbContextOptions: BusinessSetupDbContext.Configure);

        builder.Services.AddScoped<IBusinessSetupDbContext>(services => services.GetRequiredService<BusinessSetupDbContext>());
        builder.Services.AddSingleton<BusinessTelemetry>();

        // The use cases, one handler each.
        builder.Services.AddScoped<GetMyBusiness>();
        builder.Services.AddScoped<CheckSlug>();
        builder.Services.AddScoped<StartBusinessHandler>();
        builder.Services.AddScoped<SetContact>();
        builder.Services.AddScoped<GetOpeningHours>();
        builder.Services.AddScoped<SetOpeningHours>();
        builder.Services.AddScoped<ListServices>();
        builder.Services.AddScoped<AddService>();
        builder.Services.AddScoped<ChangeService>();
        builder.Services.AddScoped<SetServiceVisibility>();
        builder.Services.AddScoped<ListStaff>();
        builder.Services.AddScoped<AddStaffMember>();
        builder.Services.AddScoped<ChangeStaffMember>();

        builder.Services.AddScoped<IBusinessDirectory, BusinessDirectory>();
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
