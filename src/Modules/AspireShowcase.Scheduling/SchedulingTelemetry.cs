using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireShowcase.Scheduling;

/// <summary>
/// Custom telemetry for Scheduling: a span per operation, and counters for attempts to book and
/// for cancellations. Named after the module; AddScheduling subscribes to it. Client names and
/// emails are left out: they are personal data.
/// </summary>
sealed class SchedulingTelemetry
{
    const string Name = SchedulingModule.TelemetryName;

    static readonly ActivitySource Source = new(Name);

    readonly Counter<long> _booked;
    readonly Counter<long> _cancelled;
    readonly Counter<long> _timeOff;

    public SchedulingTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        _booked = meter.CreateCounter<long>(
            "bookings.attempts", "{booking}", "Attempts to book, by result (booked, slot_taken) and source (client, sample).");
        _cancelled = meter.CreateCounter<long>(
            "bookings.cancellations", "{booking}", "Bookings cancelled, by who cancelled them.");
        _timeOff = meter.CreateCounter<long>(
            "time_off.changes", "{time_off}", "Time off added and removed, by result (added, removed, invalid).");
    }

    /// <summary>Starts a span for one operation, under the request's span.</summary>
    public Activity? StartActivity(string name) => Source.StartActivity(name);

    /// <param name="source">Where the booking came from: client, or sample in development.</param>
    public void Booking(BookingResult result, string source) => _booked.Add(1,
        new KeyValuePair<string, object?>("result", result == BookingResult.Booked ? "booked" : "slot_taken"),
        new KeyValuePair<string, object?>("source", source));

    /// <param name="by">Who cancelled: client or business.</param>
    public void Cancelled(Activity? activity, Booking booking, string by)
    {
        _cancelled.Add(1, new KeyValuePair<string, object?>("by", by));
        activity?.SetTag("booking.id", booking.Id.Value);
    }

    /// <param name="result">added, removed or invalid.</param>
    /// <param name="bookingsInside">For added: the confirmed bookings already in that time.</param>
    public void TimeOffChanged(Activity? activity, TimeOff? timeOff, string result, int bookingsInside = 0)
    {
        _timeOff.Add(1, new KeyValuePair<string, object?>("result", result));
        if (timeOff is not null)
        {
            activity?.SetTag("time_off.id", timeOff.Id.Value);
            activity?.SetTag("time_off.business_wide", timeOff.StaffMemberId is null);
        }
        var tags = new ActivityTagsCollection();
        if (result == "added")
        {
            tags["time_off.bookings_inside"] = bookingsInside;
        }
        activity?.AddEvent(new ActivityEvent($"time_off.{result}", tags: tags));
    }

    public void CalendarRead(Activity? activity, CalendarView view, int bookings)
    {
        activity?.SetTag("calendar.view", view.ToString().ToLowerInvariant());
        activity?.SetTag("calendar.bookings", bookings);
    }
}
