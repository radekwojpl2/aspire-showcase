using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Prompts;

/// <summary>A ready-made request to go through the user's bookings; the tools still ask before anything changes.</summary>
[McpServerPromptType]
sealed class MyUpcomingBookingsPrompt
{
    [McpServerPrompt(Name = "my_upcoming_bookings", Title = "My upcoming bookings")]
    [Description("Go through your upcoming bookings, to move or cancel one.")]
    public static string MyUpcomingBookings() =>
        """
        Show me my upcoming bookings with my_bookings, soonest first: the business, the service, the
        staff member, the time, and until when each can still be changed. Then ask me whether I want to
        move or cancel one. To move one, show me its new times with find_reschedule_slots and move it
        with reschedule_booking; to cancel one, use cancel_booking.
        """;
}
