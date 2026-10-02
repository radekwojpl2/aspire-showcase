using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// Custom telemetry for the notifications service, next to what ASP.NET Core records on its
/// own: a span per operation with events for what happened inside it, and metrics for what
/// was received, rejected and is currently stored.
/// </summary>
/// <remarks>
/// The source and the meter are named after the application, which is the name the
/// ServiceDefaults project subscribes to. Messages are left out: they hold user input.
/// </remarks>
sealed class NotificationTelemetry
{
    const string Name = "AspireShowcase.Notifications";

    static readonly ActivitySource Source = new(Name);

    readonly Counter<long> _received;
    readonly Counter<long> _rejected;
    readonly Counter<long> _digested;
    readonly Histogram<int> _messageLength;

    public NotificationTelemetry(IMeterFactory meterFactory, NotificationStore store)
    {
        var meter = meterFactory.Create(Name);

        _received = meter.CreateCounter<long>(
            "notifications.received", "{notification}", "Notifications accepted and stored, by kind.");
        _rejected = meter.CreateCounter<long>(
            "notifications.rejected", "{notification}", "Notifications refused because they were invalid.");
        _digested = meter.CreateCounter<long>(
            "notifications.digested", "{notification}", "Notifications summed up by the scheduled digest.");

        // The default buckets are meant for milliseconds; these fit a message of up to 300 characters.
        _messageLength = meter.CreateHistogram(
            "notifications.message.length", "{character}", "Length of the messages received.",
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = [10, 25, 50, 100, 150, 200, 300] });

        // Read from the store each time metrics are exported.
        meter.CreateObservableGauge(
            "notifications.stored", () => store.Count, "{notification}", "Notifications currently kept in memory.");
    }

    /// <summary>Starts a span for one operation, under the request's span.</summary>
    public Activity? StartActivity(string name) => Source.StartActivity(name);

    /// <param name="evicted">How many old notifications were dropped to make room.</param>
    public void Recorded(Activity? activity, Notification notification, int evicted)
    {
        _received.Add(1, new KeyValuePair<string, object?>("kind", notification.Kind));
        _messageLength.Record(notification.Message.Length);

        activity?.SetTag("notification.id", notification.Id);
        activity?.SetTag("notification.kind", notification.Kind);
        activity?.AddEvent(new ActivityEvent("notification.recorded"));
        if (evicted > 0)
        {
            activity?.AddEvent(new ActivityEvent(
                "notification.evicted", tags: new ActivityTagsCollection { ["count"] = evicted }));
        }
    }

    public void Rejected(Activity? activity, string reason)
    {
        _rejected.Add(1);
        activity?.AddEvent(new ActivityEvent(
            "notification.rejected", tags: new ActivityTagsCollection { ["reason"] = reason }));
    }

    /// <summary>Starts the digest's span, linked to the spans that recorded its notifications.</summary>
    public Activity? StartDigest(IEnumerable<ActivityLink> links) =>
        Source.StartActivity("notifications.digest", ActivityKind.Internal, parentContext: default, links: links);

    public void Digested(Activity? activity, int count)
    {
        _digested.Add(count);
        activity?.SetTag("notifications.count", count);
    }

    public void Listed(Activity? activity, int count) => activity?.SetTag("notifications.count", count);
}
