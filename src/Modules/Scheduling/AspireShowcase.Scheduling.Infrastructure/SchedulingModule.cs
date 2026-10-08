using AspireShowcase.Scheduling.Application;
using AspireShowcase.Scheduling.Application.Calendar;
using AspireShowcase.Scheduling.Application.ClientBookings;
using AspireShowcase.Scheduling.Application.Development;
using AspireShowcase.Scheduling.Application.Notices;
using AspireShowcase.Scheduling.Application.Policies;
using AspireShowcase.Scheduling.Application.PublicBooking;
using AspireShowcase.Scheduling.Application.Rescheduling;
using AspireShowcase.Scheduling.Application.TimeOffs;
using AspireShowcase.Scheduling.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AspireShowcase.Scheduling;

/// <summary>
/// Scheduling, the core of the booking SaaS: availability and bookings. Clients see free slots
/// and book them (user stories MVP-1 to MVP-4) and see and cancel their own (MVP-7), owners see
/// their calendar (MVP-12), block time off (V1-1) and set a cancellation policy (V1-3); clients and owners move bookings (V1-4). This class
/// is the module's whole public surface; it reads businesses, staff and services only through
/// Business Setup's public client.
/// </summary>
public static class SchedulingModule
{
    /// <summary>The name of the module's spans and metrics.</summary>
    public const string TelemetryName = SchedulingTelemetry.Name;

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

        builder.Services.AddScoped<ISchedulingDbContext>(services => services.GetRequiredService<SchedulingDbContext>());
        builder.Services.AddScoped<Bookings>();
        builder.Services.AddScoped<IBookings>(services => services.GetRequiredService<Bookings>());
        builder.Services.AddScoped<Availability>();
        builder.Services.AddSingleton<SchedulingTelemetry>();

        // The use cases, one handler each.
        builder.Services.AddScoped<GetPublicBusiness>();
        builder.Services.AddScoped<GetFreeSlots>();
        builder.Services.AddScoped<BookSlotHandler>();
        builder.Services.AddScoped<ListMyBookings>();
        builder.Services.AddScoped<CancelMyBooking>();
        builder.Services.AddScoped<GetCalendar>();
        builder.Services.AddScoped<CancelBooking>();
        builder.Services.AddScoped<Moves>();
        builder.Services.AddScoped<GetMyBookingSlots>();
        builder.Services.AddScoped<RescheduleMyBooking>();
        builder.Services.AddScoped<GetBookingSlots>();
        builder.Services.AddScoped<RescheduleBooking>();
        builder.Services.AddScoped<ListTimeOff>();
        builder.Services.AddScoped<AddTimeOff>();
        builder.Services.AddScoped<RemoveTimeOff>();
        builder.Services.AddScoped<GetCancellationPolicy>();
        builder.Services.AddScoped<SetCancellationPolicy>();
        builder.Services.AddScoped<MakeSampleBookings>();
        builder.Services.AddScoped<BuildBookingNotice>();
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
