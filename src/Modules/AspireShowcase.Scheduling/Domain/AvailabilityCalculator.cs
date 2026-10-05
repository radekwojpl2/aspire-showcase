using AspireShowcase.BusinessSetup.PublicClient;

namespace AspireShowcase.Scheduling;

/// <summary>When a staff member works: periods of weekdays, in the business's local time.</summary>
sealed record StaffSchedule(StaffMemberId StaffMemberId, IReadOnlyList<WorkingPeriod> WorkingHours);

/// <summary>A time a staff member is already booked.</summary>
sealed record BusyTime(StaffMemberId StaffMemberId, DateTimeOffset Start, DateTimeOffset End);

/// <summary>A start time with the staff members who are free for the whole service from then.</summary>
sealed record FreeSlot(DateTimeOffset Start, IReadOnlyList<StaffMemberId> FreeStaff);

/// <summary>
/// Works out the free slots (user stories MVP-1 and MVP-2): from the staff's working hours, the
/// service's duration and their existing bookings. A domain service: the rule needs bookings and
/// Business Setup's hours together, so it belongs to no single aggregate.
/// </summary>
/// <remarks>
/// Slots start on a 15-minute grid from the start of each working period, in the business's
/// time zone, and the whole service has to fit in the period. Times a daylight saving change
/// skips don't exist and aren't offered. A slot lists every free staff member, so "anyone"
/// is the union of their slots, and booking can assign whoever is free.
/// </remarks>
static class AvailabilityCalculator
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    /// <param name="firstDay">The first local day to look at.</param>
    /// <param name="days">How many days, from <paramref name="firstDay"/>.</param>
    /// <param name="now">Slots that start before this aren't offered.</param>
    public static IReadOnlyList<FreeSlot> FreeSlots(
        IEnumerable<StaffSchedule> staff, TimeSpan duration, IEnumerable<BusyTime> busy, TimeZoneInfo timeZone,
        DateOnly firstDay, int days, DateTimeOffset now)
    {
        var busyByStaff = busy.ToLookup(time => time.StaffMemberId);
        var slots = new SortedDictionary<DateTimeOffset, List<StaffMemberId>>();

        foreach (var member in staff)
        {
            var theirBusyTimes = busyByStaff[member.StaffMemberId].ToList();
            for (var day = firstDay; day < firstDay.AddDays(days); day = day.AddDays(1))
            {
                foreach (var period in member.WorkingHours.Where(period => period.Day == day.DayOfWeek))
                {
                    // A TimeSpan rather than a TimeOnly, which would wrap around midnight. Periods end
                    // within their day, so the offset stays below 24 hours.
                    for (var offset = period.Start.ToTimeSpan(); offset + duration <= period.End.ToTimeSpan(); offset += Step)
                    {
                        if (Instant(day, TimeOnly.FromTimeSpan(offset), timeZone) is not { } start || start < now)
                        {
                            continue;
                        }
                        var end = start + duration;
                        if (theirBusyTimes.Any(busyTime => busyTime.Start < end && start < busyTime.End))
                        {
                            continue;
                        }

                        if (!slots.TryGetValue(start, out var free))
                        {
                            slots[start] = free = [];
                        }
                        free.Add(member.StaffMemberId);
                    }
                }
            }
        }

        return slots.Select(slot => new FreeSlot(slot.Key, slot.Value)).ToList();
    }

    /// <summary>The instant a local time happens, or null when a daylight saving change skips it.</summary>
    public static DateTimeOffset? Instant(DateOnly day, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            return null;
        }
        // When the clocks go back, a time happens twice; the first one, still in summer time, counts.
        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
