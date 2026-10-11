using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// An owner's time off (user story V1-1): when the business or a staff member is away, so no
/// one can book them then. Adding and removing it are confirmed by the owner, through
/// <see cref="AskUser"/>. Bookings already inside it stay: the owner decides about each one.
/// </summary>
[McpServerToolType]
sealed class TimeOffTools(WebApi web)
{
    [McpServerTool(Name = "list_time_off", Title = "My business's time off", ReadOnly = true, OpenWorld = false)]
    [Description("For the owner of a business: its time off that hasn't ended yet, for the whole business or one " +
        "staff member, in the business's time zone, with the bookings still inside each.")]
    public Task<TimeOffList> ListTimeOff(CancellationToken cancellation) => web.GetTimeOffAsync(cancellation);

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

    [McpServerTool(Name = "remove_time_off", Title = "Remove time off", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("For the owner of a business: removes one of its time off, once the owner confirms it; the time " +
        "can be booked again.")]
    public async Task<ChangeResult<TimeOffResponse>> RemoveTimeOff(
        RequestContext<CallToolRequestParams> context,
        [Description("The time off's ID, from list_time_off.")] Guid timeOffId,
        CancellationToken cancellation = default)
    {
        var timeOff = (await web.GetTimeOffAsync(cancellation)).TimeOff.FirstOrDefault(timeOff => timeOff.Id == timeOffId)
            ?? throw new McpException($"The business has no time off {timeOffId} that hasn't ended; list_time_off lists them.");

        var answer = await AskUser.AskAsync(context,
            $"Remove the time off of {timeOff.StaffName ?? "the whole business"} from {timeOff.From.Replace('T', ' ')} " +
            $"to {timeOff.To.Replace('T', ' ')}{(timeOff.Note is null ? "" : $" ({timeOff.Note})")}? Clients can book that time again.",
            new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["confirm"] = AskUser.YesNo("Remove it") },
            cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<TimeOffResponse>.Declined("The owner kept the time off.");
        }

        await web.RemoveTimeOffAsync(timeOffId, cancellation);
        return ChangeResult<TimeOffResponse>.Made(timeOff);
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
