using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Owner;

/// <summary>The bookings on an owner's dashboard calendar (user story MVP-12).</summary>
[McpServerToolType]
sealed class OwnerCalendarTool(WebApi web)
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
