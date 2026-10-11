using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// The tools a signed-in client books, cancels and moves with, each confirmed by the user in a
/// form the MCP client shows (elicitation), on both protocol revisions: the 2026-07-28 one asks
/// statelessly (multi round-trip requests), the 2025-11-25 one over its session.
/// </summary>
public sealed class ClientToolsTests(SignInMcpFactory mcp) : IClassFixture<SignInMcpFactory>
{
    const string Stateless = "2026-07-28";
    const string WithSession = "2025-11-25";

    static readonly Guid Haircut = Guid.Parse("0199b1c4-7a3e-7f00-8a1b-2c3d4e5f6a01");
    static readonly DateTimeOffset Nine = DateTimeOffset.Parse("2026-11-02T09:00:00+00:00");
    static readonly DateTimeOffset Ten = DateTimeOffset.Parse("2026-11-02T10:00:00+00:00");

    [Fact]
    public async Task The_tools_of_a_client_are_not_offered_without_signing_in()
    {
        await using var client = await mcp.ConnectAsync(path: "/mcp/public");

        var tools = await client.ListToolsAsync();

        Assert.DoesNotContain(tools, tool => tool.Name is "book_slot" or "my_bookings" or "cancel_booking");
    }

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task A_client_books_the_time_they_pick_under_the_name_they_give(string protocolVersion)
    {
        var slug = AnswerBusiness();
        mcp.Web.Answer(HttpMethod.Post, $"/api/public/businesses/{slug}/bookings",
            new { id = Guid.NewGuid(), serviceName = "Haircut", staffName = "Anna", date = "2026-11-02", start = "11:00", end = "11:30" },
            HttpStatusCode.Created);
        ElicitRequestParams? asked = null;
        await using var client = await ConnectAsync(protocolVersion, request =>
        {
            asked = request;
            return Accept(new() { ["startsAt"] = Ten.ToString("O"), ["clientName"] = "Radek", ["clientEmail"] = "radek@example.com" });
        });

        var result = await client.CallToolAsync("book_slot", new Dictionary<string, object?> { ["slug"] = slug, ["serviceId"] = Haircut });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Book Haircut (30 min, 80 PLN)", asked!.Message);
        var (_, body, _) = Assert.Single(mcp.Web.Received, request => request.Request == $"POST /api/public/businesses/{slug}/bookings");
        var booking = JsonDocument.Parse(body!).RootElement;
        Assert.Equal(Ten, booking.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal("Radek", booking.GetProperty("clientName").GetString());
        Assert.Equal("radek@example.com", booking.GetProperty("clientEmail").GetString());
    }

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task Nothing_is_booked_when_the_user_declines(string protocolVersion)
    {
        var slug = AnswerBusiness();
        await using var client = await ConnectAsync(protocolVersion,
            _ => ValueTask.FromResult(new ElicitResult { Action = "decline" }));

        var result = await client.CallToolAsync("book_slot", new Dictionary<string, object?> { ["slug"] = slug, ["serviceId"] = Haircut });

        Assert.NotEqual(true, result.IsError);
        Assert.False(Structured(result).GetProperty("done").GetBoolean());
        Assert.DoesNotContain(mcp.Web.Received, request => request.Request.StartsWith($"POST /api/public/businesses/{slug}"));
    }

    [Fact]
    public async Task A_client_that_cannot_ask_the_user_has_to_pass_the_name_and_email()
    {
        var slug = AnswerBusiness();
        await using var client = await mcp.ConnectAsync(
            new McpClientOptions { ProtocolVersion = WithSession }, accessToken: SignInMcpFactory.AccessToken());

        var result = await client.CallToolAsync("book_slot", new Dictionary<string, object?>
        {
            ["slug"] = slug, ["serviceId"] = Haircut, ["startsAt"] = Nine,
        });

        Assert.True(result.IsError);
        Assert.Contains("clientName and clientEmail", Text(result));
    }

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task A_booking_is_cancelled_only_once_the_user_confirms(string protocolVersion)
    {
        var id = AnswerMyBooking();
        mcp.Web.Answer(HttpMethod.Post, $"/api/me/bookings/{id}/cancel", new { });
        var answers = new Queue<bool>([false, true]);
        await using var client = await ConnectAsync(protocolVersion,
            _ => Accept(new() { ["confirm"] = answers.Dequeue() }));

        await client.CallToolAsync("cancel_booking", new Dictionary<string, object?> { ["bookingId"] = id });
        Assert.DoesNotContain(mcp.Web.Received, request => request.Request == $"POST /api/me/bookings/{id}/cancel");

        await client.CallToolAsync("cancel_booking", new Dictionary<string, object?> { ["bookingId"] = id });
        Assert.Contains(mcp.Web.Received, request => request.Request == $"POST /api/me/bookings/{id}/cancel");
    }

    [Fact]
    public async Task A_refusal_of_web_reaches_the_model_with_what_was_wrong()
    {
        var slug = AnswerBusiness();
        mcp.Web.Answer(HttpMethod.Post, $"/api/public/businesses/{slug}/bookings",
            new { title = "One or more validation errors occurred.", errors = new { clientEmail = new[] { "Enter an email address." } } },
            HttpStatusCode.BadRequest);
        await using var client = await ConnectAsync(Stateless,
            _ => Accept(new() { ["startsAt"] = Nine.ToString("O"), ["clientName"] = "Radek", ["clientEmail"] = "not-an-email" }));

        var result = await client.CallToolAsync("book_slot", new Dictionary<string, object?> { ["slug"] = slug, ["serviceId"] = Haircut });

        Assert.True(result.IsError);
        Assert.Contains("clientEmail: Enter an email address.", Text(result));
    }

    /// <summary>A business of its own for the test, with a haircut free at 9:00 and 10:00.</summary>
    string AnswerBusiness()
    {
        var slug = $"business-{Guid.NewGuid():N}";
        mcp.Web.Answer(HttpMethod.Get, $"/api/public/businesses/{slug}", new
        {
            name = "Anna's Hair", slug, timeZone = "UTC", cancellationNoticeHours = 24,
            services = new[] { new { id = Haircut, name = "Haircut", durationMinutes = 30, price = 80m, currency = "PLN", staff = Array.Empty<object>() } },
        });
        mcp.Web.Answer(HttpMethod.Get, $"/api/public/businesses/{slug}/slots?serviceId={Haircut}", new
        {
            timeZone = "UTC",
            days = new[] { new { date = "2026-11-02", slots = new[] { new { start = "09:00", startsAt = Nine }, new { start = "10:00", startsAt = Ten } } } },
        });
        return slug;
    }

    /// <summary>One upcoming booking of the user's, under /api/me/bookings.</summary>
    Guid AnswerMyBooking()
    {
        var id = Guid.NewGuid();
        mcp.Web.Answer(HttpMethod.Get, "/api/me/bookings", new[]
        {
            new
            {
                id, businessName = "Anna's Hair", businessSlug = "anna-hair", serviceName = "Haircut", staffName = "Anna",
                date = "2026-11-02", start = "09:00", end = "09:30", timeZone = "UTC", canChangeUntil = Nine.AddDays(-1),
                businessContactEmail = (string?)null,
            },
        });
        return id;
    }

    Task<McpClient> ConnectAsync(string protocolVersion, Func<ElicitRequestParams, ValueTask<ElicitResult>> user) =>
        mcp.ConnectAsync(
            new McpClientOptions
            {
                ProtocolVersion = protocolVersion,
                Handlers = new() { ElicitationHandler = (request, _) => user(request!) },
            },
            accessToken: SignInMcpFactory.AccessToken());

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
