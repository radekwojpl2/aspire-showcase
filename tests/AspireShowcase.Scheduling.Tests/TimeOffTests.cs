using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Scheduling;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.Scheduling.Tests;

/// <summary>The rules of user story V1-1 on the TimeOff aggregate.</summary>
public class TimeOffTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    static readonly StaffMemberId Anna = StaffMemberId.New();
    static readonly StaffMemberId Ben = StaffMemberId.New();

    static TimeOff Block(StaffMemberId? staff, DateTimeOffset start, DateTimeOffset end, string? note = null) =>
        TimeOff.Block(BusinessId.New(), staff, start, end, note, Now);

    static IReadOnlyDictionary<string, string[]> ErrorsOf(Action block) =>
        Assert.Throws<DomainValidationException>(block).Errors;

    [Fact]
    public void Time_off_is_stored_in_UTC_with_its_note_trimmed()
    {
        var start = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(2));

        var holiday = Block(Anna, start, start.AddDays(5), "  Holiday ");

        Assert.Equal(new DateTimeOffset(2026, 10, 11, 22, 0, 0, TimeSpan.Zero), holiday.Start);
        Assert.Equal(TimeSpan.Zero, holiday.Start.Offset);
        Assert.Equal("Holiday", holiday.Note);
        Assert.Equal(Anna, holiday.StaffMemberId);
    }

    [Fact]
    public void Time_off_of_the_business_covers_everyone_and_of_a_staff_member_only_them()
    {
        var closed = Block(null, Now.AddDays(1), Now.AddDays(2));
        var annaAway = Block(Anna, Now.AddDays(1), Now.AddDays(2));

        Assert.True(closed.Covers(Anna) && closed.Covers(Ben));
        Assert.True(annaAway.Covers(Anna));
        Assert.False(annaAway.Covers(Ben));
    }

    [Fact]
    public void Time_off_overlaps_what_starts_before_it_ends_and_ends_after_it_starts()
    {
        var lunch = Block(null, Now.AddHours(1), Now.AddHours(2));

        Assert.True(lunch.Overlaps(Now.AddMinutes(30), Now.AddMinutes(90)));
        // Back to back is fine: [start, end).
        Assert.False(lunch.Overlaps(Now.AddHours(2), Now.AddHours(3)));
        Assert.False(lunch.Overlaps(Now, Now.AddHours(1)));
    }

    [Fact]
    public void Time_off_has_to_end_after_it_starts()
    {
        var errors = ErrorsOf(() => Block(null, Now.AddDays(1), Now.AddDays(1)));

        Assert.True(errors.ContainsKey("to"));
    }

    [Fact]
    public void Time_off_that_is_already_over_is_refused()
    {
        var errors = ErrorsOf(() => Block(null, Now.AddDays(-2), Now.AddDays(-1)));

        Assert.True(errors.ContainsKey("to"));
    }

    [Fact]
    public void Time_off_that_has_started_but_not_ended_is_accepted()
    {
        var sick = Block(Anna, Now.AddHours(-1), Now.AddDays(2));

        Assert.Equal(Now.AddHours(-1), sick.Start);
    }

    [Fact]
    public void Time_off_lasts_at_most_a_year()
    {
        var errors = ErrorsOf(() => Block(null, Now.AddDays(1), Now.AddDays(1) + TimeOff.MaxLength + TimeSpan.FromMinutes(5)));

        Assert.True(errors.ContainsKey("to"));
    }

    [Fact]
    public void A_long_note_is_refused_and_an_empty_one_is_none()
    {
        var errors = ErrorsOf(() => Block(null, Now.AddDays(1), Now.AddDays(2), new string('x', TimeOff.MaxNoteLength + 1)));
        var withoutNote = Block(null, Now.AddDays(1), Now.AddDays(2), "   ");

        Assert.True(errors.ContainsKey("note"));
        Assert.Null(withoutNote.Note);
    }
}
