using System.Collections.Concurrent;
using System.Diagnostics;
using Quartz;

/// <summary>
/// Runs on a schedule and sums up the notifications recorded since its last run. It has no
/// request to belong to, so its span starts a trace of its own and carries a span link to
/// each <c>notifications.record</c> span that fed it. The dashboard shows them under Links,
/// and this span under Backlinks on the other side.
/// </summary>
[DisallowConcurrentExecution]
sealed class NotificationDigestJob(
    PendingDigest pending, NotificationTelemetry telemetry, ILogger<NotificationDigestJob> logger) : IJob
{
    public const string Schedule = "0/30 * * * * ?"; // every 30 seconds

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var recorded = pending.Drain();

        // Nothing new: no span, so idle runs don't fill the trace list.
        if (recorded.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        using var activity = telemetry.StartDigest(recorded.Select(item => new ActivityLink(item.Span)));
        telemetry.Digested(activity, recorded.Count);
        logger.LogInformation(
            "Digest of {Count} notifications: {Kinds}",
            recorded.Count,
            string.Join(", ", recorded.GroupBy(item => item.Kind).Select(kind => $"{kind.Count()} {kind.Key}")));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Notifications waiting for the next digest, each with the span that recorded it.</summary>
sealed class PendingDigest
{
    readonly ConcurrentQueue<(string Kind, ActivityContext Span)> _recorded = new();

    public void Add(string kind, Activity? span)
    {
        // Without a span (not sampled, no listener) there is nothing to link to.
        if (span is not null)
        {
            _recorded.Enqueue((kind, span.Context));
        }
    }

    public List<(string Kind, ActivityContext Span)> Drain()
    {
        var recorded = new List<(string Kind, ActivityContext Span)>();
        while (_recorded.TryDequeue(out var item))
        {
            recorded.Add(item);
        }

        return recorded;
    }
}
