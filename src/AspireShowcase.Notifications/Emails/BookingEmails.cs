using System.Globalization;
using System.Net;
using AspireShowcase.Scheduling.PublicClient;

/// <summary>An email to one recipient.</summary>
/// <param name="Recipient">client or owner: with the message's ID, what makes the email unique.</param>
sealed record OutgoingEmail(string Recipient, string Kind, EmailMessage Message);

/// <summary>
/// Which emails a booking event sends, and what they say (user stories MVP-5, MVP-7, MVP-13,
/// MVP-14 and V1-4). Someone without an email address gets none.
/// </summary>
static class BookingEmails
{
    public static IEnumerable<OutgoingEmail> For(BookingNotice booking, string? appUrl)
    {
        var app = (appUrl ?? "").TrimEnd('/');
        var when = $"{Day(booking.Date)}, {booking.Start}–{booking.End} ({booking.TimeZone.Replace('_', ' ')})";
        var what = $"{booking.ServiceName} with {booking.StaffName}";
        var before = booking.PreviousDate is { } previousDate ? $"{Day(previousDate)}, {booking.PreviousStart}" : "";

        IEnumerable<OutgoingEmail> emails = (booking.Kind, booking.CancelledBy) switch
        {
            // MVP-5 and MVP-13.
            ("confirmed", _) =>
            [
                Email(booking.Client, "client", "confirmation",
                    $"Booked: {booking.ServiceName} at {booking.BusinessName}, {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Client.Name},",
                    [$"You're booked at {booking.BusinessName}:", what, when],
                    ("See or cancel your bookings", $"{app}/my-bookings")),
                Email(booking.Owner, "owner", "new-booking",
                    $"New booking: {booking.Client.Name}, {booking.ServiceName}, {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Owner.Name},",
                    [$"{booking.Client.Name} ({booking.Client.Email ?? "no email"}) booked:", what, when],
                    ("Open your bookings", $"{app}/bookings")),
            ],
            // MVP-7: the client cancelled; both hear about it.
            ("cancelled", "client") =>
            [
                Email(booking.Client, "client", "cancelled-by-client",
                    $"Cancelled: {booking.ServiceName} at {booking.BusinessName}, {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Client.Name},",
                    [$"You cancelled your booking at {booking.BusinessName}:", what, when],
                    ("Book another time", $"{app}/book/{booking.BusinessSlug}")),
                Email(booking.Owner, "owner", "cancelled-by-client",
                    $"Cancelled by {booking.Client.Name}: {booking.ServiceName}, {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Owner.Name},",
                    [$"{booking.Client.Name} cancelled, so this time is free again:", what, when],
                    ("Open your bookings", $"{app}/bookings")),
            ],
            // V1-4: the client moved it; both hear about it.
            ("rescheduled", _) when booking.RescheduledBy == "client" =>
            [
                Email(booking.Client, "client", "rescheduled-by-client",
                    $"Moved: {booking.ServiceName} at {booking.BusinessName}, now {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Client.Name},",
                    [$"Your booking at {booking.BusinessName} is moved:", what, when, $"It was {before}."],
                    ("See or change your bookings", $"{app}/my-bookings")),
                Email(booking.Owner, "owner", "rescheduled-by-client",
                    $"Moved by {booking.Client.Name}: {booking.ServiceName}, now {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Owner.Name},",
                    [$"{booking.Client.Name} moved their booking from {before} to:", what, when],
                    ("Open your bookings", $"{app}/bookings")),
            ],
            // V1-4: the business moved it, such as for time off; the client hears about it.
            ("rescheduled", _) =>
            [
                Email(booking.Client, "client", "rescheduled-by-business",
                    $"{booking.BusinessName} moved your booking: {booking.ServiceName}, now {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Client.Name},",
                    [$"{booking.BusinessName} moved your booking from {before} to:", what, when],
                    ("See or change your bookings", $"{app}/my-bookings")),
            ],
            // MVP-14: the business cancelled; the client hears about it.
            ("cancelled", _) =>
            [
                Email(booking.Client, "client", "cancelled-by-business",
                    $"{booking.BusinessName} had to cancel: {booking.ServiceName}, {Day(booking.Date)} {booking.Start}",
                    $"Hi {booking.Client.Name},",
                    [$"Sorry, {booking.BusinessName} had to cancel your booking:", what, when],
                    ("Book another time", $"{app}/book/{booking.BusinessSlug}")),
            ],
            _ => [],
        };
        return emails.Where(email => !string.IsNullOrEmpty(email.Message.To));
    }

    static OutgoingEmail Email(
        BookingParty to, string recipient, string kind, string subject, string greeting, string[] lines,
        (string Text, string Url) link)
    {
        var text = string.Join("\n\n", [greeting, string.Join("\n", lines), $"{link.Text}: {link.Url}"]);
        var html =
            $"<p>{Encode(greeting)}</p>" +
            $"<p>{string.Join("<br>", lines.Select(Encode))}</p>" +
            $"<p><a href=\"{Encode(link.Url)}\">{Encode(link.Text)}</a></p>";
        return new OutgoingEmail(recipient, kind, new EmailMessage(to.Email ?? "", subject, text, html));
    }

    // Names and services are typed by users, so they're encoded before going into HTML.
    static string Encode(string value) => WebUtility.HtmlEncode(value);

    static string Day(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("dddd d MMMM", CultureInfo.GetCultureInfo("en-GB"));
}
