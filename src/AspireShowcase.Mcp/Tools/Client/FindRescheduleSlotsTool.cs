using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Client;

/// <summary>The times one of the signed-in client's bookings can be moved to (user story V1-4).</summary>
[McpServerToolType]
sealed class FindRescheduleSlotsTool(WebApi web, TimeProvider time)
{
    [McpServerTool(Name = "find_reschedule_slots", Title = "Find times to move a booking to", ReadOnly = true, OpenWorld = false)]
    [Description("The free times one of the signed-in user's bookings can be moved to, day by day, in the " +
        "business's time zone.")]
    public async Task<PublicSlots> FindRescheduleSlots(
        [Description("The booking's ID, from my_bookings.")] Guid bookingId,
        [Description("Only times with this staff member; leave out for anyone who's free.")] Guid? staffMemberId = null,
        [Description("The first day, yyyy-MM-dd; leave out for today.")] string? from = null,
        [Description("How many days from the first one, up to 28.")] int days = 7,
        CancellationToken cancellation = default)
    {
        var slots = await web.GetMyBookingSlotsAsync(bookingId, staffMemberId, cancellation);
        return slots with { Days = FreeTimes.Window(slots, from, days, time) };
    }
}
