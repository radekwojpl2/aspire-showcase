using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AspireShowcase.Scheduling;

/// <summary>
/// Scheduling, the core of the booking SaaS: availability and bookings. Clients see free slots
/// and book them (user stories MVP-1 to MVP-4) and see and cancel their own (MVP-7), owners see
/// their calendar (MVP-12), block time off (V1-1) and set a cancellation policy (V1-3); clients and owners move bookings (V1-4),
/// and owners book for clients who phoned (V1-5). This class
/// is the module's whole public surface; it reads businesses, staff and services only through
/// Business Setup's public client.
/// </summary>
public static class SchedulingModule
{
    /// <summary>The name of the module's spans and metrics.</summary>
    public const string TelemetryName = "AspireShowcase.Scheduling";

    /// <param name="connectionName">The database the AppHost passes in, holding the module's tables.</param>
    public static void AddScheduling(this IHostApplicationBuilder builder, string connectionName)
    {
        // Not pooled, unlike Business Setup's context: this one takes the request's BusinessScope
        // for its query filter. Enrich adds what AddNpgsqlDbContext would: retries, a health
        // check, and traces and metrics for the queries.
        builder.Services.AddScoped<BusinessScope>();
        builder.Services.AddDbContext<SchedulingDbContext>(options => options.UseNpgsql(
            builder.Configuration.GetConnectionString(connectionName),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SchedulingDbContext.Schema)));
        builder.EnrichNpgsqlDbContext<SchedulingDbContext>();

        builder.Services.AddScoped<Bookings>();
        builder.Services.AddScoped<Availability>();
        builder.Services.AddSingleton<SchedulingTelemetry>();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(TelemetryName))
            .WithMetrics(metrics => metrics.AddMeter(TelemetryName));
    }

    /// <summary>
    /// Scheduling's part of the host's MassTransit bus: the transactional outbox in Scheduling's
    /// database, which booking events are published through, and the consumer that turns them
    /// into notices for the notifications service. The host chooses the transport.
    /// </summary>
    public static void AddSchedulingMessaging(this IBusRegistrationConfigurator bus)
    {
        bus.AddEntityFrameworkOutbox<SchedulingDbContext>(outbox =>
        {
            outbox.UsePostgres();
            // Publishing from a request writes to the outbox; a background service sends it.
            outbox.UseBusOutbox();
        });
        bus.AddConsumer<BookingNoticeConsumer, BookingNoticeConsumerDefinition>();
    }

    /// <param name="includeDevelopmentTools">Also maps /dev/sample-bookings; only ever in Development.</param>
    public static void MapScheduling(this IEndpointRouteBuilder api, bool includeDevelopmentTools)
    {
        CalendarEndpoints.Map(api);
        OwnerBookingEndpoints.Map(api);
        TimeOffEndpoints.Map(api);
        CancellationPolicyEndpoints.Map(api);
        RescheduleEndpoints.Map(api);
        PublicBookingEndpoints.Map(api);
        ClientBookingEndpoints.Map(api);
        if (includeDevelopmentTools)
        {
            SampleBookings.Map(api);
        }
    }

    /// <summary>
    /// Creates or updates the module's tables. Enough for a single instance; with several, run
    /// migrations as a separate step so they don't race.
    /// </summary>
    public static async Task MigrateSchedulingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.MigrateAsync();
    }
}
