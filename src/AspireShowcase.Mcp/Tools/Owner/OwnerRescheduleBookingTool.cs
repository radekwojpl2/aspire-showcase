using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Owner;

/// <summary>An owner moving a client's booking to another time or staff member (user story V1-4).</summary>
[McpServerToolType]
sealed class OwnerRescheduleBookingTool(WebApi web)
{
    [McpServerTool(Name = "owner_reschedule_booking", Title = "Move a client's booking", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("For the owner of a business: moves a client's booking to another time or staff member. Without " +
        "startsAt, the owner picks one of the next free times; either way they confirm the move before it's made. " +
        "The client is emailed.")]
    public async Task<ChangeResult<BookingConfirmation>> OwnerRescheduleBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from owner_calendar.")] Guid bookingId,
        [Description("The booking's day, yyyy-MM-dd, from owner_calendar.")] string date,
        [Description("A startsAt from owner_find_reschedule_slots; leave out to let the owner pick.")] DateTimeOffset? startsAt = null,
        [Description("Who it should be with; leave out for anyone who's free.")] Guid? staffMemberId = null,
        CancellationToken cancellation = default)
    {
        var (booking, timeZone) = await OwnerBooking.FindAsync(web, bookingId, date, cancellation);

        var fields = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        if (startsAt is null)
        {
            var slots = await web.GetBookingSlotsAsync(bookingId, staffMemberId, cancellation);
            fields["startsAt"] = AskUser.Choice("New time", FreeTimes.Choices(slots));
        }
        else
        {
            fields["confirm"] = AskUser.YesNo($"Move it to {FreeTimes.Local(startsAt.Value, timeZone)}");
        }

        var answer = await AskUser.AskAsync(context,
            $"Move {OwnerBooking.Describe(booking)}? {booking.ClientName} will get an email.", fields, cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The owner kept the booking where it was.");
        }

        var newStart = startsAt ?? FreeTimes.Picked(answer)
            ?? throw new McpException("Ask the owner for the new time, and pass it as startsAt.");
        return ChangeResult<BookingConfirmation>.Made(
            await web.RescheduleBookingAsync(bookingId, newStart, staffMemberId, cancellation));
    }
}
