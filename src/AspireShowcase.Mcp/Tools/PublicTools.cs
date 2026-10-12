using System.ComponentModel;
using System.Globalization;
using AspireShowcase.Mcp.Apps;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// What a business's public booking page shows (user stories MVP-1 and MVP-2): the business
/// with its services and staff, and its free times. web needs no user for these, but the MCP
/// server only lets in signed-in users.
/// </summary>
[McpServerToolType]
sealed class PublicTools(WebApi web, TimeProvider time)
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

    // Hosts that support MCP Apps show the result as the slot picker; see SlotPicker.
    [McpServerTool(Name = "find_free_slots", Title = "Find free times", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("ui", JsonValue = $$"""{"resourceUri":"{{SlotPicker.Uri}}"}""")]
    // The key hosts read before the extension settled on "ui"; deprecated, still read by some.
    [McpMeta("ui/resourceUri", SlotPicker.Uri)]
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
        return slots with { Days = Window(slots, from, days, time) };
    }

    /// <summary>
    /// The days of the calendar from <paramref name="from"/>, or today in the business's time zone,
    /// up to <paramref name="days"/> of them. Days without a free time aren't in web's answer, so
    /// they're counted by date, not by the days listed.
    /// </summary>
    internal static IReadOnlyList<PublicDay> Window(PublicSlots slots, string? from, int days, TimeProvider time)
    {
        DateOnly first;
        if (from is null)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(slots.TimeZone);
            first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), timeZone).DateTime);
        }
        else if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
        {
            throw new McpException("from must be a date, yyyy-MM-dd.");
        }

        var end = first.AddDays(Math.Clamp(days, 1, 28));
        return slots.Days
            .Where(day => DateOnly.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) is var date
                && date >= first && date < end)
            .ToList();
    }
}
