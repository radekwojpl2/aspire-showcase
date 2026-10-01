using System.Diagnostics;

record Notification(Guid Id, DateTimeOffset CreatedAt, string Kind, string Message);

/// <summary>
/// Calls the notifications service. Its address, "notifications", is the resource name in
/// the AppHost; service discovery turns it into the real one, and the HttpClient defaults
/// from ServiceDefaults add retries and a circuit breaker.
/// </summary>
/// <remarks>
/// Notifications are an extra, so the service being down must not hold up the to-do list:
/// a call gets a few seconds, and after a failure the service is left alone for a while.
/// </remarks>
sealed class NotificationsClient(HttpClient http, ILogger<NotificationsClient> logger)
{
    // The resilience defaults would keep retrying for up to 30 seconds.
    static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    // Shared by all instances: a typed client is created per request.
    static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(15);
    static long _skipUntil;

    /// <summary>
    /// Tells the notifications service about a change. A failure to notify never fails
    /// the change itself: it is logged and marked on the current span.
    /// </summary>
    public async Task NotifyAsync(string kind, string message, CancellationToken cancellation)
    {
        try
        {
            await CallAsync(async timeout =>
            {
                using var response = await http.PostAsJsonAsync("/notifications", new { kind, message }, timeout);
                response.EnsureSuccessStatusCode();
                return true;
            }, cancellation);
            Activity.Current?.AddEvent(new ActivityEvent("notification.sent"));
        }
        catch (Exception exception) when (!cancellation.IsCancellationRequested)
        {
            Activity.Current?.AddEvent(new ActivityEvent("notification.failed"));
            logger.LogWarning(exception, "Could not send the {Kind} notification", kind);
        }
    }

    /// <summary>The most recent notifications, newest first. Throws when the service can't be reached.</summary>
    public Task<List<Notification>> RecentAsync(CancellationToken cancellation) =>
        CallAsync(async timeout =>
            await http.GetFromJsonAsync<List<Notification>>("/notifications", timeout) ?? [], cancellation);

    async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellation)
    {
        if (Stopwatch.GetTimestamp() < Volatile.Read(ref _skipUntil))
        {
            throw new InvalidOperationException(
                $"The notifications service failed moments ago; not calling it for {RetryAfter.TotalSeconds} seconds.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Budget);
        try
        {
            return await call(timeout.Token);
        }
        catch when (!cancellation.IsCancellationRequested)
        {
            Volatile.Write(ref _skipUntil, Stopwatch.GetTimestamp() + (long)(RetryAfter.TotalSeconds * Stopwatch.Frequency));
            throw;
        }
    }
}

static class NotificationEndpoints
{
    /// <summary>
    /// The browser can only reach this API, so the recent notifications are passed on
    /// from the notifications service.
    /// </summary>
    public static void MapNotifications(this IEndpointRouteBuilder api) =>
        api.MapGet("/notifications", async (NotificationsClient notifications, CancellationToken cancellation) =>
        {
            try
            {
                return Results.Ok(await notifications.RecentAsync(cancellation));
            }
            catch (Exception exception) when (!cancellation.IsCancellationRequested)
            {
                return Results.Problem(
                    title: "The notifications service is unavailable.",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("GetNotifications");
}
