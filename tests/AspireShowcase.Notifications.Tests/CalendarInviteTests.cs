using System.Text;
using AspireShowcase.Scheduling.PublicClient;

namespace AspireShowcase.Notifications.Tests;

/// <summary>The booking as a calendar invite (V1-7), which calendars add, move and remove.</summary>
public class CalendarInviteTests
{
    const string Sender = "Bookings <bookings@salon.test>";

    static readonly Guid BookingId = Guid.Parse("0199c0de-0000-7000-8000-000000000001");

    static BookingNotice Notice(string kind, DateTimeOffset occurredAt) => new(
        kind, kind == "cancelled" ? "client" : null, BookingId, "Anna Hair", "anna-hair", "Haircut", "Ben Barber",
        "2026-11-03", "11:00", "12:00", "Europe/Warsaw", new BookingParty("Cleo Client", "cleo@example.com"),
        new BookingParty("Olivia Owner", "olivia@salon.example"),
        StartsAt: new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
        EndsAt: new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero),
        OccurredAt: occurredAt,
        BusinessAddress: "Main Street 1\n00-001 Warsaw");

    static readonly DateTimeOffset Booked = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    static string Text(BookingNotice notice) => Encoding.UTF8.GetString(CalendarInvite.For(notice, Sender)!.Content);

    // The lines as a calendar reads them, folded lines joined again.
    static string[] Lines(BookingNotice notice) => Text(notice).Replace("\r\n ", "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    static string Value(string[] lines, string name) => lines.Single(line => line.StartsWith($"{name}:") || line.StartsWith($"{name};"));

    [Fact]
    public void An_invite_has_the_time_in_UTC_and_the_business_as_its_place()
    {
        var lines = Lines(Notice("confirmed", Booked));

        Assert.Contains("METHOD:REQUEST", lines);
        Assert.Contains("DTSTART:20261103T100000Z", lines);
        Assert.Contains("DTEND:20261103T110000Z", lines);
        Assert.Contains("SUMMARY:Haircut at Anna Hair", lines);
        Assert.Contains(@"LOCATION:Main Street 1\n00-001 Warsaw", lines);
        Assert.Contains($"UID:{BookingId}@aspire-showcase", lines);
        Assert.Equal("ORGANIZER;CN=\"Anna Hair\":mailto:bookings@salon.test", Value(lines, "ORGANIZER"));
    }

    [Fact]
    public void A_moved_booking_updates_the_same_event_with_a_higher_sequence()
    {
        var booked = Lines(Notice("confirmed", Booked));
        var moved = Lines(Notice("rescheduled", Booked.AddDays(1)));

        Assert.Equal(Value(booked, "UID"), Value(moved, "UID"));
        Assert.True(Sequence(moved) > Sequence(booked));

        static long Sequence(string[] lines) => long.Parse(Value(lines, "SEQUENCE")["SEQUENCE:".Length..]);
    }

    [Fact]
    public void A_cancelled_booking_removes_the_event()
    {
        var invite = CalendarInvite.For(Notice("cancelled", Booked), Sender)!;
        var lines = Lines(Notice("cancelled", Booked));

        Assert.Contains("METHOD:CANCEL", lines);
        Assert.Contains("STATUS:CANCELLED", lines);
        Assert.EndsWith("method=CANCEL", invite.ContentType);
    }

    [Fact]
    public void Commas_semicolons_and_line_breaks_typed_by_users_are_escaped()
    {
        var notice = Notice("confirmed", Booked) with { ServiceName = "Cut, wash; dry" };

        Assert.Contains(@"SUMMARY:Cut\, wash\; dry at Anna Hair", Lines(notice));
    }

    [Fact]
    public void Long_lines_are_folded_at_75_bytes_without_splitting_a_character()
    {
        var notice = Notice("confirmed", Booked) with { BusinessAddress = string.Concat(Enumerable.Repeat("Żółta ulica ", 20)) };

        var physical = Text(notice).Split("\r\n");

        Assert.All(physical, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.Contains($"LOCATION:{notice.BusinessAddress}", Lines(notice));
    }

    [Fact]
    public void A_notice_from_before_invites_has_none()
    {
        Assert.Null(CalendarInvite.For(Notice("confirmed", Booked) with { StartsAt = null }, Sender));
    }
}
