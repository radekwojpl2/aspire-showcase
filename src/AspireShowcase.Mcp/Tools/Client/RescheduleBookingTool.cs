using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Client;

/// <summary>
/// Moving one of the signed-in client's bookings to another time (user story V1-4). The user
/// confirms it, through <see cref="AskUser"/>, before web makes it.
/// </summary>
[McpServerToolType]
sealed class RescheduleBookingTool(WebApi web)
{
    [McpServerTool(Name = "reschedule_booking", Title = "Move a booking", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Moves one of the signed-in user's bookings to another time. Without startsAt, the user picks " +
        "one of the next free times; either way they confirm the move before it's made.")]
    public async Task<ChangeResult<BookingConfirmation>> RescheduleBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from my_bookings.")] Guid bookingId,
        [Description("A startsAt from find_reschedule_slots; leave out to let the user pick.")] DateTimeOffset? startsAt = null,
        [Description("Who it should be with; leave out for anyone who's free.")] Guid? staffMemberId = null,
        CancellationToken cancellation = default)
    {
        var booking = await MyBooking.FindAsync(web, bookingId, cancellation);

        var fields = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        if (startsAt is null)
        {
            var slots = await web.GetMyBookingSlotsAsync(bookingId, staffMemberId, cancellation);
            fields["startsAt"] = AskUser.Choice("New time", FreeTimes.Choices(slots));
        }
        else
        {
            fields["confirm"] = AskUser.YesNo($"Move it to {FreeTimes.Local(startsAt.Value, booking.TimeZone)}");
        }

        var answer = await AskUser.AskAsync(context, $"Move {MyBooking.Describe(booking)}?", fields, cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The user kept the booking where it was.");
        }

        var newStart = startsAt ?? FreeTimes.Picked(answer)
            ?? throw new McpException("Ask the user for the new time, and pass it as startsAt.");
        return ChangeResult<BookingConfirmation>.Made(
            await web.RescheduleMyBookingAsync(bookingId, newStart, staffMemberId, cancellation));
    }
}
