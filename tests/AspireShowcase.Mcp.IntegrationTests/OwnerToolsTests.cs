using System.Net;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>The owner's calendar, which web only shows to the owner of a business.</summary>
public sealed class OwnerToolsTests(SignInMcpFactory mcp) : IClassFixture<SignInMcpFactory>
{
    [Fact]
    public async Task An_owner_sees_the_bookings_of_the_day_asked_for()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/businesses/mine/bookings?view=day&date=2026-11-02", new
        {
            view = "day", date = "2026-11-02", firstDay = "2026-11-02", lastDay = "2026-11-02", timeZone = "UTC",
            staff = Array.Empty<object>(),
            bookings = new[]
            {
                new
                {
                    id = Guid.NewGuid(), day = "2026-11-02", start = "09:00", end = "09:30", staffMemberId = Guid.NewGuid(),
                    staffName = "Anna", serviceName = "Haircut", clientName = "Radek", clientEmail = "radek@example.com",
                },
            },
            timeOff = Array.Empty<object>(),
        });
        await using var client = await mcp.ConnectAsync(accessToken: SignInMcpFactory.AccessToken());

        var result = await client.CallToolAsync("owner_calendar", new Dictionary<string, object?> { ["view"] = "day", ["date"] = "2026-11-02" });

        Assert.NotEqual(true, result.IsError);
        var calendar = JsonDocument.Parse(Text(result)).RootElement;
        Assert.Equal("Radek", calendar.GetProperty("bookings")[0].GetProperty("clientName").GetString());
    }

    [Fact]
    public async Task Someone_who_owns_no_business_is_told_so()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/businesses/mine/bookings?view=week&date=2026-12-01", new { }, HttpStatusCode.Forbidden);
        await using var client = await mcp.ConnectAsync(accessToken: SignInMcpFactory.AccessToken());

        var result = await client.CallToolAsync("owner_calendar", new Dictionary<string, object?> { ["date"] = "2026-12-01" });

        Assert.True(result.IsError);
        Assert.Contains("Only the owner of a business", Text(result));
    }

    static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
