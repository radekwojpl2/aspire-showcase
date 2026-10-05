using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// Custom telemetry for businesses: a span per operation, and counters for businesses
/// started and attempts turned down.
/// </summary>
/// <remarks>
/// The source and the meter are named after the application, which is the name the
/// ServiceDefaults project subscribes to. Names and links are left out: they are user input.
/// </remarks>
sealed class BusinessTelemetry
{
    const string Name = "AspireShowcase.Api";

    static readonly ActivitySource Source = new(Name);

    readonly Counter<long> _created;
    readonly Counter<long> _rejected;

    public BusinessTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        _created = meter.CreateCounter<long>("businesses.created", "{business}", "Businesses started.");
        _rejected = meter.CreateCounter<long>(
            "businesses.rejected", "{business}", "Attempts to start a business that were turned down, by reason.");
    }

    /// <summary>Starts a span for one operation, under the request's span.</summary>
    public Activity? StartActivity(string name) => Source.StartActivity(name);

    public void Created(Activity? activity, Business business)
    {
        _created.Add(1);
        activity?.SetTag("business.id", business.Id);
        activity?.AddEvent(new ActivityEvent("business.created"));
    }

    /// <param name="reason">invalid, slug_taken, already_owner or logto_unavailable.</param>
    public void Rejected(Activity? activity, string reason)
    {
        _rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
        activity?.SetTag("business.rejected", reason);
        activity?.AddEvent(new ActivityEvent("business.rejected"));
    }
}
