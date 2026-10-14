using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Client;

/// <summary>The signed-in client's bookings, as under My bookings (user story MVP-7).</summary>
[McpServerToolType]
sealed class MyBookingsTool(WebApi web)
{
    [McpServerTool(Name = "my_bookings", Title = "My bookings", ReadOnly = true, OpenWorld = false)]
    [Description("The signed-in user's upcoming bookings, with every business they booked with, and until when " +
        "each can still be cancelled or moved.")]
    public Task<IReadOnlyList<ClientBooking>> MyBookings(CancellationToken cancellation) =>
        web.GetMyBookingsAsync(cancellation);
}
