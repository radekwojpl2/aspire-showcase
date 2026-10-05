using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireShowcase.Api.BusinessSetup;

/// <summary>
/// Custom telemetry for Business Setup: a span per operation, and counters for businesses
/// started, attempts turned down, and opening hours changed.
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
    readonly Counter<long> _openingHoursChanged;

    public BusinessTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        _created = meter.CreateCounter<long>("businesses.created", "{business}", "Businesses started.");
        _rejected = meter.CreateCounter<long>(
            "businesses.rejected", "{business}", "Attempts to start a business that were turned down, by reason.");
        _openingHoursChanged = meter.CreateCounter<long>(
            "businesses.opening_hours.changes", "{change}", "Opening hours saved, by result.");
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

    /// <param name="result">saved or invalid.</param>
    public void OpeningHoursChanged(Activity? activity, Business? business, string result)
    {
        _openingHoursChanged.Add(1, new KeyValuePair<string, object?>("result", result));
        if (business is not null)
        {
            activity?.SetTag("business.id", business.Id);
            activity?.SetTag("business.opening_hours.periods", business.OpeningHours.Periods.Count);
        }
        activity?.AddEvent(new ActivityEvent($"opening_hours.{result}"));
    }
}
