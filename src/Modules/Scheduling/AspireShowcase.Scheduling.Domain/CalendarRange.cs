namespace AspireShowcase.Scheduling.Domain;

enum CalendarView
{
    Day,
    Week,
}

/// <summary>
/// The days a calendar view shows, in the business's time zone, and the instants they span.
/// Weeks start on Monday. A day isn't always 24 hours: the instants follow the time zone's
/// daylight saving changes.
/// </summary>
sealed record CalendarRange(DateOnly FirstDay, DateOnly LastDay, DateTimeOffset From, DateTimeOffset To)
{
    public static CalendarRange For(DateOnly date, CalendarView view, TimeZoneInfo timeZone)
    {
        var first = view == CalendarView.Week
            ? date.AddDays(-(((int)date.DayOfWeek + 6) % 7))
            : date;
        var last = view == CalendarView.Week ? first.AddDays(6) : first;
        return new CalendarRange(first, last, StartOf(first, timeZone), StartOf(last.AddDays(1), timeZone));
    }

    /// <summary>Local midnight of the day, as an instant.</summary>
    static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Where midnight is skipped by a daylight saving change, the day starts an hour later.
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
