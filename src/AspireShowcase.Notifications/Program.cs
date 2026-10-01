using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenTelemetry, health checks, service discovery and resilience defaults.
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<NotificationStore>();

var app = builder.Build();

app.UseExceptionHandler();

// Called by the API after a to-do item is added or removed.
app.MapPost("/notifications", (CreateNotification request, NotificationStore store, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Kind) || string.IsNullOrWhiteSpace(request.Message)
        || request.Message.Length > NotificationStore.MaxMessageLength)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["message"] = [$"Kind and message are required; the message can be at most {NotificationStore.MaxMessageLength} characters."],
        });
    }

    var notification = store.Add(request.Kind, request.Message);
    // The message holds what the user typed, so only the kind is logged.
    logger.LogInformation("Notification {Id} of kind {Kind} recorded", notification.Id, notification.Kind);
    return Results.Created($"/notifications/{notification.Id}", notification);
})
.WithName("CreateNotification");

// The most recent notifications, newest first.
app.MapGet("/notifications", (NotificationStore store) => store.Recent())
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

    public Notification Add(string kind, string message)
    {
        var notification = new Notification(Guid.NewGuid(), DateTimeOffset.UtcNow, kind, message);
        _notifications.Enqueue(notification);
        while (_notifications.Count > Capacity)
        {
            _notifications.TryDequeue(out _);
        }

        return notification;
    }

    public IEnumerable<Notification> Recent() => _notifications.Reverse();
}
