using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling;

namespace AspireShowcase.Scheduling.Tests;

/// <summary>The rules of user stories MVP-1 and MVP-2: which slots are free, and for whom.</summary>
public class AvailabilityCalculatorTests
{
    static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    // Tuesday 6 October 2026; Warsaw is UTC+2 then.
    static readonly DateOnly Tuesday = new(2026, 10, 6);
    static readonly DateTimeOffset LongBefore = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    static readonly StaffMemberId Anna = StaffMemberId.New();
    static readonly StaffMemberId Ben = StaffMemberId.New();

    static StaffSchedule Works(StaffMemberId staff, string start, string end, DayOfWeek day = DayOfWeek.Tuesday) =>
        new(staff, [new WorkingPeriod(day, TimeOnly.Parse(start), TimeOnly.Parse(end))]);

    static DateTimeOffset At(string localTime, DateOnly? day = null) =>
        AvailabilityCalculator.Instant(day ?? Tuesday, TimeOnly.Parse(localTime), Warsaw)!.Value;

    static IReadOnlyList<FreeSlot> Slots(
        IEnumerable<StaffSchedule> staff, int minutes, IEnumerable<BusyTime>? busy = null, DateTimeOffset? now = null) =>
        AvailabilityCalculator.FreeSlots(
            staff, TimeSpan.FromMinutes(minutes), busy ?? [], Warsaw, Tuesday, 1, now ?? LongBefore);

    static string[] LocalStarts(IEnumerable<FreeSlot> slots) =>
        slots.Select(slot => TimeZoneInfo.ConvertTime(slot.Start, Warsaw).ToString("HH:mm")).ToArray();

    [Fact]
    public void Slots_start_every_15_minutes_and_the_service_fits_in_the_working_hours()
    {
        var slots = Slots([Works(Anna, "09:00", "10:00")], 30);

        Assert.Equal(["09:00", "09:15", "09:30"], LocalStarts(slots));
    }

    [Fact]
    public void Times_outside_working_hours_and_days_off_arent_offered()
    {
        var slots = Slots([Works(Anna, "09:00", "10:00", DayOfWeek.Wednesday)], 30);

        Assert.Empty(slots);
    }

    [Fact]
    public void Booked_times_arent_offered()
    {
        var slots = Slots([Works(Anna, "09:00", "11:00")], 30, [new BusyTime(Anna, At("09:30"), At("10:00"))]);

        // 09:15 would run into the booking; 10:00 starts as it ends.
        Assert.Equal(["09:00", "10:00", "10:15", "10:30"], LocalStarts(slots));
    }

    [Fact]
    public void Slots_that_have_started_arent_offered()
    {
        var slots = Slots([Works(Anna, "09:00", "10:00")], 15, now: At("09:20"));

        Assert.Equal(["09:30", "09:45"], LocalStarts(slots));
    }

    [Fact]
    public void Anyone_is_the_union_of_the_staff_s_slots_and_lists_who_is_free()
    {
        var slots = Slots(
            [Works(Anna, "09:00", "10:00"), Works(Ben, "09:30", "10:30")], 30,
            [new BusyTime(Anna, At("09:00"), At("09:30"))]);

        Assert.Equal(["09:30", "09:45", "10:00"], LocalStarts(slots));
        Assert.Equal([Anna, Ben], slots[0].FreeStaff);
        Assert.Equal([Ben], slots[2].FreeStaff);
    }

    [Fact]
    public void Another_staff_member_s_booking_leaves_the_slot_free()
    {
        var slots = Slots([Works(Anna, "09:00", "09:30")], 30, [new BusyTime(Ben, At("09:00"), At("09:30"))]);

        Assert.Equal(["09:00"], LocalStarts(slots));
    }

    [Fact]
    public void A_period_ending_late_in_the_evening_doesnt_run_past_midnight()
    {
        var slots = Slots([Works(Anna, "23:05", "23:55")], 5);

        Assert.Equal(["23:05", "23:20", "23:35", "23:50"], LocalStarts(slots));
    }

    [Fact]
    public void Times_skipped_when_the_clocks_go_forward_arent_offered()
    {
        // Summer time starts in Europe on Sunday 29 March 2026: 02:00 becomes 03:00.
        var sunday = new DateOnly(2026, 3, 29);

        var slots = AvailabilityCalculator.FreeSlots(
            [Works(Anna, "01:30", "03:30", DayOfWeek.Sunday)], TimeSpan.FromMinutes(15), [], Warsaw, sunday, 1,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            ["01:30", "01:45", "03:00", "03:15"],
            slots.Select(slot => TimeZoneInfo.ConvertTime(slot.Start, Warsaw).ToString("HH:mm")));
    }
}
