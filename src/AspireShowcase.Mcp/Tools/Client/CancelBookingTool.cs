using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Client;

/// <summary>
/// Cancelling one of the signed-in client's bookings (user story MVP-7). The user confirms it,
/// through <see cref="AskUser"/>, before web makes it.
/// </summary>
[McpServerToolType]
sealed class CancelBookingTool(WebApi web)
{
    [McpServerTool(Name = "cancel_booking", Title = "Cancel a booking", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Cancels one of the signed-in user's bookings, once they confirm it. The time is free again at once.")]
    public async Task<ChangeResult<ClientBooking>> CancelBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from my_bookings.")] Guid bookingId,
        CancellationToken cancellation = default)
    {
        var booking = await MyBooking.FindAsync(web, bookingId, cancellation);

        var answer = await AskUser.AskAsync(context,
            $"Cancel {MyBooking.Describe(booking)}?",
            new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["confirm"] = AskUser.YesNo("Cancel it") },
            cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<ClientBooking>.Declined("The user kept the booking.");
        }

        await web.CancelMyBookingAsync(bookingId, cancellation);
        return ChangeResult<ClientBooking>.Made(booking);
    }
}
