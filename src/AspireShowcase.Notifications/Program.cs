using System.Collections.Concurrent;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<NotificationStore>();

// Custom spans, span events and metrics for the notifications.
builder.Services.AddSingleton<NotificationTelemetry>();

// A scheduled job (Quartz.NET) that sums up what was recorded since its last run.
builder.Services.AddSingleton<PendingDigest>();
builder.Services.AddQuartz(quartz => quartz.ScheduleJob<NotificationDigestJob>(
    trigger => trigger.WithCronSchedule(NotificationDigestJob.Schedule)));
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var app = builder.Build();

app.UseExceptionHandler();

// Called by the API after a to-do item is added or removed.
app.MapPost("/notifications", (
    CreateNotification request, NotificationStore store, PendingDigest pending,
    NotificationTelemetry telemetry, ILogger<Program> logger) =>
{
    using var activity = telemetry.StartActivity("notifications.record");

    if (string.IsNullOrWhiteSpace(request.Kind) || string.IsNullOrWhiteSpace(request.Message))
    {
        telemetry.Rejected(activity, "missing kind or message");
        return Invalid();
    }

    if (request.Message.Length > NotificationStore.MaxMessageLength)
    {
        telemetry.Rejected(activity, "message too long");
        return Invalid();
    }

    var notification = store.Add(request.Kind, request.Message, out var evicted);
    telemetry.Recorded(activity, notification, evicted);
    pending.Add(notification.Kind, activity);
    // The message holds what the user typed, so only the kind is logged.
    logger.LogInformation("Notification {Id} of kind {Kind} recorded", notification.Id, notification.Kind);
    return Results.Created($"/notifications/{notification.Id}", notification);

    static IResult Invalid() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["message"] = [$"Kind and message are required; the message can be at most {NotificationStore.MaxMessageLength} characters."],
    });
})
.WithName("CreateNotification");

// The most recent notifications, newest first.
app.MapGet("/notifications", (NotificationStore store, NotificationTelemetry telemetry) =>
{
    using var activity = telemetry.StartActivity("notifications.list");

    var recent = store.Recent();
    telemetry.Listed(activity, recent.Count);
    return recent;
})
.WithName("GetNotifications");

// Maps /health and /alive endpoints (development only by default).
app.MapDefaultEndpoints();

app.Run();

record CreateNotification(string? Kind, string? Message);

record Notification(Guid Id, DateTimeOffset CreatedAt, string Kind, string Message);

/// <summary>
/// The latest notifications, kept in memory: enough for a showcase with one instance,
/// and empty again after a restart.
/// </summary>
sealed class NotificationStore
{
    public const int MaxMessageLength = 300;

    const int Capacity = 50;

    readonly ConcurrentQueue<Notification> _notifications = new();

    public int Count => _notifications.Count;

    /// <param name="evicted">How many old notifications were dropped to stay within capacity.</param>
    public Notification Add(string kind, string message, out int evicted)
    {
        var notification = new Notification(Guid.NewGuid(), DateTimeOffset.UtcNow, kind, message);
        _notifications.Enqueue(notification);

        evicted = 0;
        while (_notifications.Count > Capacity && _notifications.TryDequeue(out _))
        {
            evicted++;
        }

        return notification;
    }

    public List<Notification> Recent() => _notifications.Reverse().ToList();
}
