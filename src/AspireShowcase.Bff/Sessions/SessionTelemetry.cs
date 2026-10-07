using System.Diagnostics.Metrics;

/// <summary>
/// Custom telemetry for bff's sessions: how many it ended, and why. A 401 from bff and one
/// from web look the same in the request telemetry, so this is how ended sessions are seen.
/// </summary>
/// <remarks>
/// The meter is named after the application, which is the name the ServiceDefaults project
/// subscribes to.
/// </remarks>
sealed class SessionTelemetry
{
    const string Name = "AspireShowcase.Bff";

    readonly Counter<long> _ended;

    public SessionTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _ended = meter.CreateCounter<long>(
            "sessions.ended", "{session}", "Sessions ended because bff couldn't get an access token for them, by reason.");
    }

    /// <param name="reason">
    /// refresh_failed (the session was there, but its token couldn't be refreshed) or
    /// session_not_found (the cookie couldn't be read, or its session is gone from bff-db).
    /// </param>
    public void Ended(string reason) => _ended.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
