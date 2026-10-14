using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Prompts;

/// <summary>A ready-made request for an owner's overview of a week; it changes nothing before the owner says so.</summary>
[McpServerPromptType]
sealed class OwnerWeekPrompt
{
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
