using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// An owner's time off, which web only lets the owner of a business change: added and removed
/// once the owner confirms, on both protocol revisions.
/// </summary>
public sealed class TimeOffToolsTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    const string Stateless = "2026-07-28";
    const string WithSession = "2025-11-25";

    static readonly Guid Anna = Guid.Parse("0199b1c4-7a3e-7f00-8a1b-2c3d4e5f6b01");

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task An_owner_adds_time_off_for_a_staff_member_once_they_confirm(string protocolVersion)
    {
        AnswerStaff();
        var booking = Guid.NewGuid();
        mcp.Web.Answer(HttpMethod.Post, "/api/businesses/mine/time-off", new
        {
            id = Guid.NewGuid(), staffMemberId = Anna, staffName = "Anna", from = "2026-11-06T13:00", to = "2026-11-06T18:00", note = "Dentist",
            bookings = new[] { new { id = booking, day = "2026-11-06", start = "14:00", end = "14:30", staffName = "Anna", serviceName = "Haircut", clientName = "Radek" } },
        }, HttpStatusCode.Created);
        ElicitRequestParams? asked = null;
        await using var client = await ConnectAsync(protocolVersion, request =>
        {
            asked = request;
            return Accept(new() { ["confirm"] = true });
        });

        var result = await client.CallToolAsync("add_time_off", new Dictionary<string, object?>
        {
            ["from"] = "2026-11-06T13:00", ["to"] = "2026-11-06T18:00", ["staffMemberId"] = Anna, ["note"] = "Dentist",
        });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Block Anna from 2026-11-06 13:00 to 2026-11-06 18:00 (Dentist)?", asked!.Message);
        var (_, body, _) = mcp.Web.Received.Last(request => request.Request == "POST /api/businesses/mine/time-off");
        var sent = JsonDocument.Parse(body!).RootElement;
        Assert.Equal(Anna, sent.GetProperty("staffMemberId").GetGuid());
        Assert.Equal("2026-11-06T13:00", sent.GetProperty("from").GetString());
        var inside = Structured(result).GetProperty("result").GetProperty("bookings");
        Assert.Equal(booking, inside[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task No_time_off_is_added_when_the_owner_declines()
    {
        var sent = mcp.Web.Received.Count(request => request.Request == "POST /api/businesses/mine/time-off");
        await using var client = await ConnectAsync(WithSession, _ => ValueTask.FromResult(new ElicitResult { Action = "decline" }));

        var result = await client.CallToolAsync("add_time_off", new Dictionary<string, object?>
        {
            ["from"] = "2026-11-07T00:00", ["to"] = "2026-11-08T00:00",
        });

        Assert.False(Structured(result).GetProperty("done").GetBoolean());
        Assert.Equal(sent, mcp.Web.Received.Count(request => request.Request == "POST /api/businesses/mine/time-off"));
    }

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task An_owner_removes_time_off_once_they_confirm(string protocolVersion)
    {
        var id = Guid.NewGuid();
        mcp.Web.Answer(HttpMethod.Get, "/api/businesses/mine/time-off", new
        {
            timeZone = "UTC",
            timeOff = new[]
            {
                new { id, staffMemberId = (Guid?)null, staffName = (string?)null, from = "2026-12-24T00:00", to = "2026-12-27T00:00", note = "Christmas", bookings = Array.Empty<object>() },
            },
        });
        mcp.Web.Answer(HttpMethod.Delete, $"/api/businesses/mine/time-off/{id}", new { }, HttpStatusCode.NoContent);
        ElicitRequestParams? asked = null;
        await using var client = await ConnectAsync(protocolVersion, request =>
        {
            asked = request;
            return Accept(new() { ["confirm"] = true });
        });

        var result = await client.CallToolAsync("remove_time_off", new Dictionary<string, object?> { ["timeOffId"] = id });

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("Remove the time off of the whole business from 2026-12-24 00:00 to 2026-12-27 00:00 (Christmas)?", asked!.Message);
        Assert.Contains(mcp.Web.Received, request => request.Request == $"DELETE /api/businesses/mine/time-off/{id}");
    }

    /// <summary>The owner's staff, as the calendar of the day of the time off lists them.</summary>
    void AnswerStaff() =>
        mcp.Web.Answer(HttpMethod.Get, "/api/businesses/mine/bookings?view=day&date=2026-11-06", new
        {
            view = "day", date = "2026-11-06", firstDay = "2026-11-06", lastDay = "2026-11-06", timeZone = "UTC",
            staff = new[] { new { id = Anna, name = "Anna" } },
            bookings = Array.Empty<object>(),
            timeOff = Array.Empty<object>(),
        });

    Task<McpClient> ConnectAsync(string protocolVersion, Func<ElicitRequestParams, ValueTask<ElicitResult>> user) =>
        mcp.ConnectAsync(
            new McpClientOptions
            {
                ProtocolVersion = protocolVersion,
                Handlers = new() { ElicitationHandler = (request, _) => user(request!) },
            });

    static ValueTask<ElicitResult> Accept(Dictionary<string, object> values) =>
        ValueTask.FromResult(new ElicitResult
        {
            Action = "accept",
            Content = values.ToDictionary(value => value.Key, value => JsonSerializer.SerializeToElement(value.Value)),
        });

    static JsonElement Structured(CallToolResult result) =>
        result.StructuredContent is { } structured ? structured : JsonDocument.Parse(Text(result)).RootElement;

    static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
