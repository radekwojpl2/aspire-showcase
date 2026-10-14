using ModelContextProtocol;

namespace AspireShowcase.Mcp.Tools.Client;

/// <summary>One of the signed-in user's bookings, as the tools that cancel or move it find and describe it.</summary>
static class MyBooking
{
    public static async Task<ClientBooking> FindAsync(WebApi web, Guid bookingId, CancellationToken cancellation) =>
        (await web.GetMyBookingsAsync(cancellation)).FirstOrDefault(booking => booking.Id == bookingId)
            ?? throw new McpException($"None of the user's upcoming bookings is {bookingId}; my_bookings lists them.");

    public static string Describe(ClientBooking booking) =>
        $"{booking.ServiceName} with {booking.StaffName} at {booking.BusinessName} on {booking.Date} at {booking.Start}";
}
