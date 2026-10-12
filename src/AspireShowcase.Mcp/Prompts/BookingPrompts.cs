using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Prompts;

/// <summary>
/// Ready-made requests the user starts themselves, such as a slash command in Claude Code: each
/// walks the model through the tools in the right order. They only write the request; the tools
/// still ask the user before anything changes.
/// </summary>
[McpServerPromptType]
sealed class BookingPrompts
{
    [McpServerPrompt(Name = "book_at", Title = "Book at a business")]
    [Description("Book a service at a business, from its services and free times.")]
    public static string BookAt(
        [Description("The business's booking link, such as \"anna-hair\", or words from its name.")] string business,
        [Description("The service to book, such as \"haircut\"; leave out to choose from the list.")] string? service = null) =>
        $"""
        I want to book {(service is null ? "a service" : $"a {service}")} at "{business}".

        1. If "{business}" isn't a booking link, find the business with list_businesses and use its booking link.
        2. Show me its services, with their duration and price, and who does them, with get_business.
        3. {(service is null ? "Ask me which service I want." : $"Pick the service that matches \"{service}\", or ask me if none or several do.")}
        4. Show me its free times for the next seven days with find_free_slots, grouped by day.
        5. Ask me which time I want, then book it with book_slot.
        """;

    [McpServerPrompt(Name = "my_upcoming_bookings", Title = "My upcoming bookings")]
    [Description("Go through your upcoming bookings, to move or cancel one.")]
    public static string MyUpcomingBookings() =>
        """
        Show me my upcoming bookings with my_bookings, soonest first: the business, the service, the
        staff member, the time, and until when each can still be changed. Then ask me whether I want to
        move or cancel one. To move one, show me its new times with find_reschedule_slots and move it
        with reschedule_booking; to cancel one, use cancel_booking.
        """;

    [McpServerPrompt(Name = "owner_week", Title = "My business's week")]
    [Description("For an owner: the week's bookings and time off at a glance, with what's still free.")]
    public static string OwnerWeek(
        [Description("Any day of the week, yyyy-MM-dd; leave out for this week.")] string? week = null) =>
        $"""
        Give me an overview of my business's week{(week is null ? "" : $" with {week} in it")}.

        1. Get the calendar with owner_calendar, view "week"{(week is null ? "" : $" and date {week}")}, and the time off with list_time_off.
        2. For each day, list the bookings by staff member, with the time, the service and the client, and any time off.
        3. Point out days with no bookings, and bookings that fall inside time off.
        4. Ask me whether I want to change anything. Don't change anything before I say so.

        If owner_calendar says I don't own a business, tell me so and stop.
        """;
}
