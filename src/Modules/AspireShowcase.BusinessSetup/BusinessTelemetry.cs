using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// Custom telemetry for Business Setup: a span per operation, and counters for businesses
/// started, attempts turned down, opening hours, services and staff changed.
/// </summary>
/// <remarks>
/// The source and the meter are named after the module; AddBusinessSetup subscribes to them.
/// Names and links are left out: they are user input.
/// </remarks>
sealed class BusinessTelemetry
{
    const string Name = BusinessSetupModule.TelemetryName;

    static readonly ActivitySource Source = new(Name);

    readonly Counter<long> _created;
    readonly Counter<long> _rejected;
    readonly Counter<long> _openingHoursChanged;
    readonly Counter<long> _servicesChanged;
    readonly Counter<long> _staffChanged;

    public BusinessTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        _created = meter.CreateCounter<long>("businesses.created", "{business}", "Businesses started.");
        _rejected = meter.CreateCounter<long>(
            "businesses.rejected", "{business}", "Attempts to start a business that were turned down, by reason.");
        _openingHoursChanged = meter.CreateCounter<long>(
            "businesses.opening_hours.changes", "{change}", "Opening hours saved, by result.");
        _servicesChanged = meter.CreateCounter<long>(
            "businesses.services.changes", "{change}", "Services added, changed, hidden and shown, by result.");
        _staffChanged = meter.CreateCounter<long>(
            "businesses.staff.changes", "{change}", "Staff members added and changed, by result.");
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

    /// <param name="result">added, changed, hidden, shown, invalid or name_taken.</param>
    public void ServiceChanged(Activity? activity, Service? service, string result)
    {
        _servicesChanged.Add(1, new KeyValuePair<string, object?>("result", result));
        if (service is not null)
        {
            activity?.SetTag("service.id", service.Id);
            activity?.SetTag("business.id", service.BusinessId);
        }
        activity?.AddEvent(new ActivityEvent($"service.{result}"));
    }

    /// <param name="result">added, changed, invalid or name_taken.</param>
    public void StaffChanged(Activity? activity, StaffMember? member, string result)
    {
        _staffChanged.Add(1, new KeyValuePair<string, object?>("result", result));
        if (member is not null)
        {
            activity?.SetTag("staff_member.id", member.Id);
            activity?.SetTag("business.id", member.BusinessId);
        }
        activity?.AddEvent(new ActivityEvent($"staff_member.{result}"));
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
