using System.ComponentModel;
using AspireShowcase.Mcp.Apps;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Public;

/// <summary>The free times on a business's public booking page (user stories MVP-1 and MVP-2).</summary>
[McpServerToolType]
sealed class FindFreeSlotsTool(WebApi web, TimeProvider time)
{
    // The API answers four weeks at once; a week is plenty to choose from and keeps the
    // answer small enough for the model's context.
    const int DefaultDays = 7;

    // Hosts that support MCP Apps show the result as the slot picker; see SlotPickerResource.
    [McpServerTool(Name = "find_free_slots", Title = "Find free times", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("ui", JsonValue = $$"""{"resourceUri":"{{SlotPickerResource.Uri}}"}""")]
    // The key hosts read before the extension settled on "ui"; deprecated, still read by some.
    [McpMeta("ui/resourceUri", SlotPickerResource.Uri)]
    [Description("The times a service can still be booked at a business, day by day, in the business's " +
        "time zone. Each slot's startsAt is the instant to book it with.")]
    public async Task<PublicSlots> FindFreeSlots(
        [Description("The business's booking link, such as \"anna-hair\".")] string slug,
        [Description("The service's ID, from get_business.")] Guid serviceId,
        [Description("Only times with this staff member; leave out for anyone who's free.")] Guid? staffMemberId = null,
        [Description("The first day, yyyy-MM-dd; leave out for today.")] string? from = null,
        [Description("How many days from the first one, up to 28.")] int days = DefaultDays,
        CancellationToken cancellation = default)
    {
        var slots = await web.GetFreeSlotsAsync(slug, serviceId, staffMemberId, cancellation);
        return slots with { Days = FreeTimes.Window(slots, from, days, time) };
    }
}
