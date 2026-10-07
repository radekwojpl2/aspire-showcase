using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Drops database spans that start without a parent: queries made outside any request or
/// message. MassTransit's inbox cleanup deletes old inbox rows every few seconds in each service
/// with an inbox, and each run would be a trace of its own, day and night. Queries made for a
/// request or a message have that as their parent, so they're kept.
/// </summary>
/// <remarks>
/// It unmarks the span as it starts rather than filtering at the end, so it works whatever
/// order the exporters' processors are in: an exporter skips a span that isn't recorded.
/// </remarks>
sealed class BackgroundQueryFilter : BaseProcessor<Activity>
{
    /// <summary>The ActivitySource of Npgsql, which the Aspire PostgreSQL integrations trace.</summary>
    public const string NpgsqlSource = "Npgsql";

    public override void OnStart(Activity activity)
    {
        if (activity.Source.Name == NpgsqlSource && string.IsNullOrEmpty(activity.ParentId))
        {
            activity.IsAllDataRequested = false;
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}
