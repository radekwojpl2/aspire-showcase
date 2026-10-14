using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.TimeOff;

/// <summary>
/// Blocking time off (user story V1-1), confirmed by the owner through <see cref="AskUser"/>.
/// Bookings already inside it stay: the owner decides about each one.
/// </summary>
[McpServerToolType]
sealed class AddTimeOffTool(WebApi web)
{
    [McpServerTool(Name = "add_time_off", Title = "Add time off", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("For the owner of a business: blocks a stretch of time, for the whole business or one staff " +
        "member, once the owner confirms it; no one can book them then. Bookings already inside it stay, and " +
        "are returned: offer the owner to cancel them with owner_cancel_booking.")]
    public async Task<ChangeResult<TimeOffResponse>> AddTimeOff(
        RequestContext<CallToolRequestParams> context,
        [Description("The local start, yyyy-MM-ddTHH:mm, in the business's time zone.")] string from,
        [Description("The local end, the same way; the next day's 00:00 for a whole last day.")] string to,
        [Description("Who's away, from owner_calendar's staff; leave out for the whole business.")] Guid? staffMemberId = null,
        [Description("Why, for the owner's own calendar, such as \"Dentist\"; clients don't see it.")] string? note = null,
        CancellationToken cancellation = default)
    {
        var who = staffMemberId is { } id ? await StaffNameAsync(id, from, cancellation) : "the whole business";
        var answer = await AskUser.AskAsync(context,
            $"Block {who} from {from.Replace('T', ' ')} to {to.Replace('T', ' ')}{(note is null ? "" : $" ({note})")}?",
            new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["confirm"] = AskUser.YesNo("Add the time off") },
            cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<TimeOffResponse>.Declined("The owner didn't add the time off.");
        }

        return ChangeResult<TimeOffResponse>.Made(await web.AddTimeOffAsync(staffMemberId, from, to, note, cancellation));
    }

    /// <summary>The staff member's name, so the owner confirms who's away rather than an ID.</summary>
    async Task<string> StaffNameAsync(Guid staffMemberId, string from, CancellationToken cancellation)
    {
        var day = from.Split('T')[0];
        var calendar = await web.GetCalendarAsync("day", day, null, cancellation);
        return calendar.Staff.FirstOrDefault(staff => staff.Id == staffMemberId)?.Name
            ?? throw new McpException($"The business has no staff member {staffMemberId}; owner_calendar lists them.");
    }
}
