using System.Globalization;
using System.Net.Mail;
using System.Text;
using AspireShowcase.Scheduling.PublicClient;

/// <summary>
/// The booking as a calendar event (user story V1-7): an iCalendar file (RFC 5545) attached to
/// the client's emails, which calendar apps offer to add, and to update when it changes.
/// </summary>
/// <remarks>
/// Every invite for a booking has the same UID, and a sequence number that grows with each
/// change. That's what lets a calendar move the event it already has, or remove it when the
/// booking is cancelled (METHOD:CANCEL), instead of adding a second one. The sequence comes from
/// when the change happened, so it grows without anything having to count the changes.
/// </remarks>
static class CalendarInvite
{
    public const string FileName = "booking.ics";

    // Sequences count seconds from here: small enough for an integer for decades.
    static readonly DateTimeOffset SequenceEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <param name="sender">The From address of the emails, the event's organizer.</param>
    /// <returns>The attachment, or null for a notice from before invites, which lacks the instants.</returns>
    public static EmailAttachment? For(BookingNotice booking, string? sender)
    {
        if (booking is not { StartsAt: { } startsAt, EndsAt: { } endsAt, OccurredAt: { } occurredAt })
        {
            return null;
        }

        var cancelled = booking.Kind == "cancelled";
        var method = cancelled ? "CANCEL" : "REQUEST";
        List<string> lines =
        [
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Aspire Showcase//Bookings//EN",
            "CALSCALE:GREGORIAN",
            $"METHOD:{method}",
            "BEGIN:VEVENT",
            $"UID:{booking.BookingId}@aspire-showcase",
            $"SEQUENCE:{Math.Max(0, (long)(occurredAt - SequenceEpoch).TotalSeconds)}",
            $"DTSTAMP:{Utc(occurredAt)}",
            $"DTSTART:{Utc(startsAt)}",
            $"DTEND:{Utc(endsAt)}",
            $"SUMMARY:{Text($"{booking.ServiceName} at {booking.BusinessName}")}",
            $"DESCRIPTION:{Text($"{booking.ServiceName} with {booking.StaffName}.")}",
            $"STATUS:{(cancelled ? "CANCELLED" : "CONFIRMED")}",
        ];
        if (booking.BusinessAddress is { } address)
        {
            lines.Add($"LOCATION:{Text(address)}");
        }
        if (MailAddress.TryCreate(sender, out var organizer))
        {
            lines.Add($"ORGANIZER;CN={Parameter(booking.BusinessName)}:mailto:{organizer.Address}");
        }
        if (booking.Client.Email is { } email)
        {
            // Nothing to answer: the booking is made, so no RSVP buttons.
            lines.Add($"ATTENDEE;CN={Parameter(booking.Client.Name)};ROLE=REQ-PARTICIPANT;RSVP=FALSE:mailto:{email}");
        }
        lines.AddRange(["END:VEVENT", "END:VCALENDAR"]);

        var content = string.Concat(lines.Select(line => Fold(line) + "\r\n"));
        return new EmailAttachment(FileName, $"text/calendar; charset=utf-8; method={method}", Encoding.UTF8.GetBytes(content));
    }

    static string Utc(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // A TEXT value: backslashes, commas, semicolons and line breaks are escaped (RFC 5545, 3.3.11).
    static string Text(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").ReplaceLineEndings("\\n");

    // A parameter value (a name in CN=) is quoted, and can't hold quotes or line breaks.
    static string Parameter(string value) =>
        $"\"{value.Replace("\"", "'").ReplaceLineEndings(" ")}\"";

    // Lines longer than 75 octets go on over several, each after the first starting with a
    // space (RFC 5545, 3.1). Split between characters, never inside one's UTF-8 bytes.
    static string Fold(string line)
    {
        var folded = new StringBuilder();
        var octets = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            if (octets + rune.Utf8SequenceLength > limit)
            {
                folded.Append("\r\n ");
                octets = 0;
                limit = 74; // The leading space counts.
            }
            folded.Append(rune.ToString());
            octets += rune.Utf8SequenceLength;
        }
        return folded.ToString();
    }
}
