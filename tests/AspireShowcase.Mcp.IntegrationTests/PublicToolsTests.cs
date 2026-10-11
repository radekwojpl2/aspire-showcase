using System.Net;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// The tools of a business's public booking page: the business and its free times, read from
/// web's public endpoints.
/// </summary>
public sealed class PublicToolsTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    static readonly Guid Haircut = Guid.Parse("0199b1c4-7a3e-7f00-8a1b-2c3d4e5f6a01");

    [Fact]
    public async Task The_server_offers_the_public_tools_as_read_only()
    {
        await using var client = await mcp.ConnectAsync();

        var tools = await client.ListToolsAsync();

        foreach (var name in new[] { "list_businesses", "get_business", "find_free_slots" })
        {
            var tool = Assert.Single(tools, tool => tool.Name == name);
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        }
    }

    [Fact]
    public async Task Businesses_are_found_by_text_in_their_name_with_their_booking_link()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/public/businesses?query=hair", new[]
        {
            new { name = "Anna's Hair", slug = "anna-hair", address = "Main Street 1", description = (string?)null, logoUrl = (string?)null },
        });
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("list_businesses", new Dictionary<string, object?> { ["query"] = "hair" });

        Assert.NotEqual(true, result.IsError);
        var business = Assert.Single(JsonDocument.Parse(Text(result)).RootElement.EnumerateArray());
        Assert.Equal("anna-hair", business.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task A_business_is_read_from_its_public_booking_page()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/public/businesses/anna-hair", new
        {
            name = "Anna's Hair",
            slug = "anna-hair",
            timeZone = "Europe/Warsaw",
            cancellationNoticeHours = 24,
            services = new[] { new { id = Haircut, name = "Haircut", durationMinutes = 30, price = 80m, currency = "PLN", staff = Array.Empty<object>() } },
        });
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("get_business", new Dictionary<string, object?> { ["slug"] = "anna-hair" });

        Assert.NotEqual(true, result.IsError);
        var business = Structured(result);
        Assert.Equal("Anna's Hair", business.GetProperty("name").GetString());
        Assert.Equal("Haircut", business.GetProperty("services")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_business_that_does_not_exist_is_an_error_the_model_can_read()
    {
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("get_business", new Dictionary<string, object?> { ["slug"] = "nobody" });

        Assert.True(result.IsError);
        Assert.Contains("Not found.", Text(result));
    }

    [Fact]
    public async Task Free_times_are_given_for_a_week_from_the_day_asked_for()
    {
        var days = Enumerable.Range(1, 28)
            .Select(day => new { date = $"2026-11-{day:00}", slots = new[] { new { start = "10:00", startsAt = $"2026-11-{day:00}T09:00:00+00:00" } } })
            .ToArray();
        mcp.Web.Answer(HttpMethod.Get, $"/api/public/businesses/anna-hair/slots?serviceId={Haircut}",
            new { timeZone = "Europe/Warsaw", days });
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("find_free_slots", new Dictionary<string, object?>
        {
            ["slug"] = "anna-hair",
            ["serviceId"] = Haircut,
            ["from"] = "2026-11-10",
        });

        var dates = Structured(result).GetProperty("days").EnumerateArray()
            .Select(day => day.GetProperty("date").GetString()).ToList();
        Assert.Equal(7, dates.Count);
        Assert.Equal("2026-11-10", dates[0]);
        Assert.Equal("2026-11-16", dates[^1]);
    }

    [Fact]
    public async Task A_week_of_free_times_ends_after_seven_days_even_when_the_business_is_closed_on_some()
    {
        var slug = AnswerMondaysAndTuesdays();
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("find_free_slots", new Dictionary<string, object?>
        {
            ["slug"] = slug,
            ["serviceId"] = Haircut,
        });

        Assert.Equal(["2026-11-02", "2026-11-03"], Dates(result));
    }

    [Fact]
    public async Task Free_times_from_a_day_the_business_is_closed_start_at_its_next_open_day()
    {
        var slug = AnswerMondaysAndTuesdays();
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("find_free_slots", new Dictionary<string, object?>
        {
            ["slug"] = slug,
            ["serviceId"] = Haircut,
            ["from"] = "2026-11-04",
            ["days"] = 14,
        });

        Assert.Equal(["2026-11-09", "2026-11-10", "2026-11-16", "2026-11-17"], Dates(result));
    }

    /// <summary>A business open on Mondays and Tuesdays: web lists only the days with free times.</summary>
    string AnswerMondaysAndTuesdays()
    {
        var slug = $"open-{Guid.NewGuid():N}";
        var days = Enumerable.Range(0, 28)
            .Select(offset => new DateOnly(2026, 11, 2).AddDays(offset))
            .Where(date => date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday)
            .Select(date => new { date = $"{date:yyyy-MM-dd}", slots = new[] { new { start = "10:00", startsAt = $"{date:yyyy-MM-dd}T09:00:00+00:00" } } })
            .ToArray();
        mcp.Web.Answer(HttpMethod.Get, $"/api/public/businesses/{slug}/slots?serviceId={Haircut}",
            new { timeZone = "Europe/Warsaw", days });
        return slug;
    }

    static List<string?> Dates(CallToolResult result) =>
        Structured(result).GetProperty("days").EnumerateArray().Select(day => day.GetProperty("date").GetString()).ToList();

    static JsonElement Structured(CallToolResult result) =>
        result.StructuredContent is { } structured
            ? structured
            : JsonDocument.Parse(Text(result)).RootElement;

    static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
