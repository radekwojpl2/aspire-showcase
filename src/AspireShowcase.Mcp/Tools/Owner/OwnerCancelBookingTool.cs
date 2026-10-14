using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Owner;

/// <summary>An owner cancelling a client's booking (user story MVP-14), once they confirm it.</summary>
[McpServerToolType]
sealed class OwnerCancelBookingTool(WebApi web)
{
    [McpServerTool(Name = "owner_cancel_booking", Title = "Cancel a client's booking", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("For the owner of a business: cancels a client's booking, such as when someone's ill, once the " +
        "owner confirms it. The client is emailed, and the time is free again at once.")]
    public async Task<ChangeResult<CalendarBooking>> OwnerCancelBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from owner_calendar.")] Guid bookingId,
        [Description("The booking's day, yyyy-MM-dd, from owner_calendar.")] string date,
        CancellationToken cancellation = default)
    {
        var (booking, _) = await OwnerBooking.FindAsync(web, bookingId, date, cancellation);

        var answer = await AskUser.AskAsync(context,
            $"Cancel {OwnerBooking.Describe(booking)}? {booking.ClientName} will get an email.",
            new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["confirm"] = AskUser.YesNo("Cancel it") },
            cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<CalendarBooking>.Declined("The owner kept the booking.");
        }

        await web.CancelBookingAsync(bookingId, cancellation);
        return ChangeResult<CalendarBooking>.Made(booking);
    }
}
