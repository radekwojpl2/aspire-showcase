using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// What anyone may see without signing in, as on a business's public booking page (user
/// stories MVP-1 and MVP-2): the business with its services and staff, and its free times.
/// </summary>
[McpServerToolType]
sealed class PublicTools(WebApi web)
{
    // The API answers four weeks at once; a week is plenty to choose from and keeps the
    // answer small enough for the model's context.
    const int DefaultDays = 7;

    [McpServerTool(Name = "get_business", Title = "Get a business", ReadOnly = true, OpenWorld = false)]
    [Description("A business's public booking page: its name, address, time zone, cancellation notice, " +
        "and its services with their duration, price and the staff who offer them. The IDs it returns are " +
        "what find_free_slots takes.")]
    public Task<PublicBusiness> GetBusiness(
        [Description("The business's booking link, the last part of its URL, such as \"anna-hair\".")] string slug,
        CancellationToken cancellation) =>
        web.GetBusinessAsync(slug, cancellation);

    [McpServerTool(Name = "find_free_slots", Title = "Find free times", ReadOnly = true, OpenWorld = false)]
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
        return slots with { Days = Window(slots.Days, from, days) };
    }

    /// <summary>The days from <paramref name="from"/> on, at most <paramref name="days"/> of them.</summary>
    internal static IReadOnlyList<PublicDay> Window(IReadOnlyList<PublicDay> all, string? from, int days) =>
        all
            // yyyy-MM-dd compares as dates do.
            .Where(day => from is null || string.CompareOrdinal(day.Date, from) >= 0)
            .Take(Math.Clamp(days, 1, 28))
            .ToList();
}
