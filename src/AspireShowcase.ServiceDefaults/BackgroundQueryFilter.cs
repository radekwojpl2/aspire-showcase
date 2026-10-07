using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Drops database spans that would show up as traces of their own: queries made outside any
/// request or message, and queries under a span that isn't recorded. MassTransit's inbox cleanup
/// deletes old inbox rows every few seconds in each service with an inbox, and the health checks
/// query each database under a /health request that isn't traced; each would be a trace, day
/// and night. Queries made for a recorded request or message are kept.
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
        if (activity.Source.Name == NpgsqlSource &&
            (string.IsNullOrEmpty(activity.ParentId) || activity.Parent is { Recorded: false }))
        {
            activity.IsAllDataRequested = false;
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}
