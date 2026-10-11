using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// The MCP server's own metrics: how tool calls end, how users answer the confirmations, and how
/// often the user's token is exchanged at Logto. The tests in this class run one at a time.
/// </summary>
public sealed class TelemetryTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    [Fact]
    public async Task Tool_calls_are_counted_by_tool_and_by_how_they_ended()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/me", new { name = "Radek", email = "radek@example.com", business = (object?)null });
        using var calls = Collect("mcp.tool.calls");
        await using var client = await mcp.ConnectAsync();

        await client.CallToolAsync("whoami");
        await client.CallToolAsync("get_business", new Dictionary<string, object?> { ["slug"] = "nobody" });

        Assert.Contains(calls.GetMeasurementSnapshot(), call => Is(call, "tool", "whoami") && Is(call, "outcome", "done"));
        Assert.Contains(calls.GetMeasurementSnapshot(), call => Is(call, "tool", "get_business") && Is(call, "outcome", "error"));
    }

    [Fact]
    public async Task A_declined_confirmation_is_counted_as_declined()
    {
        var id = Guid.NewGuid();
        mcp.Web.Answer(HttpMethod.Get, "/api/businesses/mine/bookings?view=day&date=2026-11-02", new
        {
            view = "day", date = "2026-11-02", firstDay = "2026-11-02", lastDay = "2026-11-02", timeZone = "UTC",
            staff = Array.Empty<object>(),
            bookings = new[]
            {
                new
                {
                    id, day = "2026-11-02", start = "09:00", end = "09:30", staffMemberId = Guid.NewGuid(),
                    staffName = "Anna", serviceName = "Haircut", clientName = "Radek", clientEmail = "radek@example.com",
                },
            },
            timeOff = Array.Empty<object>(),
        });
        using var answers = Collect("mcp.confirmations");
        await using var client = await mcp.ConnectAsync(new McpClientOptions
        {
            ProtocolVersion = "2025-11-25",
            Handlers = new() { ElicitationHandler = (_, _) => ValueTask.FromResult(new ElicitResult { Action = "decline" }) },
        });

        await client.CallToolAsync("owner_cancel_booking", new Dictionary<string, object?> { ["bookingId"] = id, ["date"] = "2026-11-02" });

        var answer = Assert.Single(answers.GetMeasurementSnapshot());
        Assert.True(Is(answer, "tool", "owner_cancel_booking") && Is(answer, "answer", "decline"));
    }

    [Fact]
    public async Task The_token_for_web_is_exchanged_once_and_then_reused()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/me", new { name = "Radek", email = "radek@example.com", business = (object?)null });
        using var exchanges = Collect("mcp.token.exchanges");
        await using var client = await mcp.ConnectAsync(accessToken: McpFactory.AccessToken(userId: $"user-{Guid.NewGuid():N}"));

        await client.CallToolAsync("whoami");
        await client.CallToolAsync("whoami");

        Assert.Equal(["exchanged", "cached"], exchanges.GetMeasurementSnapshot().Select(exchange => exchange.Tags["result"]));
    }

    MetricCollector<long> Collect(string instrument) =>
        new(mcp.Services.GetRequiredService<IMeterFactory>(), "AspireShowcase.Mcp", instrument);

    static bool Is(CollectedMeasurement<long> measurement, string tag, string value) =>
        measurement.Tags.TryGetValue(tag, out var actual) && Equals(actual, value);
}
