using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// What an owner sees on their dashboard's calendar (user stories MVP-12 and V1-1). Listed to
/// every signed-in user: the MCP server's token says who they are, not whether they own a
/// business, which only the token exchanged for web carries. web answers 403 to a client.
/// </summary>
[McpServerToolType]
sealed class OwnerTools(WebApi web)
{
    [McpServerTool(Name = "owner_calendar", Title = "My business's calendar", ReadOnly = true, OpenWorld = false)]
    [Description("For the owner of a business: its bookings and time off for a day or a week, for everyone or " +
        "one staff member, in the business's time zone, with each client's name and email.")]
    public Task<CalendarResponse> OwnerCalendar(
        [Description("\"day\" or \"week\".")] string view = "week",
        [Description("A day in it, yyyy-MM-dd; leave out for today.")] string? date = null,
        [Description("Only this staff member's bookings; leave out for everyone's.")] Guid? staffMemberId = null,
        CancellationToken cancellation = default) =>
        web.GetCalendarAsync(view, date, staffMemberId, cancellation);
}
