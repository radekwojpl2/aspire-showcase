using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Owner;

/// <summary>The times an owner can move a client's booking to (user story V1-4).</summary>
[McpServerToolType]
sealed class OwnerFindRescheduleSlotsTool(WebApi web, TimeProvider time)
{
    [McpServerTool(Name = "owner_find_reschedule_slots", Title = "Find times to move a client's booking to", ReadOnly = true, OpenWorld = false)]
    [Description("For the owner of a business: the free times a client's booking can be moved to, day by day, " +
        "in the business's time zone.")]
    public async Task<PublicSlots> OwnerFindRescheduleSlots(
        [Description("The booking's ID, from owner_calendar.")] Guid bookingId,
        [Description("Only times with this staff member; leave out for anyone who's free.")] Guid? staffMemberId = null,
        [Description("The first day, yyyy-MM-dd; leave out for today.")] string? from = null,
        [Description("How many days from the first one, up to 28.")] int days = 7,
        CancellationToken cancellation = default)
    {
        var slots = await web.GetBookingSlotsAsync(bookingId, staffMemberId, cancellation);
        return slots with { Days = FreeTimes.Window(slots, from, days, time) };
    }
}
