using ModelContextProtocol;

namespace AspireShowcase.Mcp.Tools.Owner;

/// <summary>A booking of the owner's business, as the tools that cancel or move it find and describe it.</summary>
/// <remarks>
/// The owner tools are listed to every signed-in user: the MCP server's token says who they are,
/// not whether they own a business, which only the token exchanged for web carries. web answers
/// 403 to a client. Each change is confirmed by the owner, through <see cref="AskUser"/>, and web
/// emails the client as it does for a change made in the app.
/// </remarks>
static class OwnerBooking
{
    /// <summary>
    /// The booking, from its day of the calendar: web has no endpoint for one booking, and the
    /// owner is asked about it by what the calendar shows.
    /// </summary>
    public static async Task<(CalendarBooking Booking, string TimeZone)> FindAsync(
        WebApi web, Guid bookingId, string date, CancellationToken cancellation)
    {
        var calendar = await web.GetCalendarAsync("day", date, null, cancellation);
        return calendar.Bookings.FirstOrDefault(booking => booking.Id == bookingId) is { } found
            ? (found, calendar.TimeZone)
            : throw new McpException($"The business has no booking {bookingId} on {date}; owner_calendar lists them.");
    }

    public static string Describe(CalendarBooking booking) =>
        $"{booking.ServiceName} with {booking.StaffName} for {booking.ClientName} on {booking.Day} at {booking.Start}";
}
