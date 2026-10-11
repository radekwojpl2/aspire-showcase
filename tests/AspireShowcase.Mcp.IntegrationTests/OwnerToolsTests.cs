using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// The owner's tools, which web only lets the owner of a business use: the calendar, and cancelling,
/// moving and booking for clients, each confirmed by the owner on both protocol revisions.
/// </summary>
public sealed class OwnerToolsTests(McpFactory mcp) : IClassFixture<McpFactory>
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
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("owner_calendar", new Dictionary<string, object?> { ["view"] = "day", ["date"] = "2026-11-02" });

        Assert.NotEqual(true, result.IsError);
        var calendar = JsonDocument.Parse(Text(result)).RootElement;
        Assert.Equal("Radek", calendar.GetProperty("bookings")[0].GetProperty("clientName").GetString());
    }

    [Fact]
    public async Task Someone_who_owns_no_business_is_told_so()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/businesses/mine/bookings?view=week&date=2026-12-01", new { }, HttpStatusCode.Forbidden);
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("owner_calendar", new Dictionary<string, object?> { ["date"] = "2026-12-01" });

        Assert.True(result.IsError);
        Assert.Contains("Only the owner of a business", Text(result));
    }

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task An_owner_cancels_a_booking_only_once_they_confirm(string protocolVersion)
    {
        var id = AnswerBookingOnTheCalendar();
        mcp.Web.Answer(HttpMethod.Post, $"/api/businesses/mine/bookings/{id}/cancel", new { }, HttpStatusCode.NoContent);
        ElicitRequestParams? asked = null;
        await using var client = await ConnectAsync(protocolVersion, request =>
        {
            asked = request;
            return Accept(new() { ["confirm"] = true });
        });

        var result = await client.CallToolAsync("owner_cancel_booking", new Dictionary<string, object?> { ["bookingId"] = id, ["date"] = Day });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Cancel Haircut with Anna for Radek on 2026-11-02 at 09:00? Radek will get an email.", asked!.Message);
        Assert.Contains(mcp.Web.Received, request => request.Request == $"POST /api/businesses/mine/bookings/{id}/cancel");
    }

    [Fact]
    public async Task Nothing_is_cancelled_when_the_owner_declines()
    {
        var id = AnswerBookingOnTheCalendar();
        await using var client = await ConnectAsync(WithSession, _ => ValueTask.FromResult(new ElicitResult { Action = "decline" }));

        var result = await client.CallToolAsync("owner_cancel_booking", new Dictionary<string, object?> { ["bookingId"] = id, ["date"] = Day });

        Assert.False(Structured(result).GetProperty("done").GetBoolean());
        Assert.DoesNotContain(mcp.Web.Received, request => request.Request.StartsWith($"POST /api/businesses/mine/bookings/{id}"));
    }

    [Fact]
    public async Task A_booking_that_is_not_on_the_day_given_is_an_error_the_model_can_read()
    {
        AnswerBookingOnTheCalendar();
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("owner_cancel_booking", new Dictionary<string, object?> { ["bookingId"] = Guid.NewGuid(), ["date"] = Day });

        Assert.True(result.IsError);
        Assert.Contains("owner_calendar lists them", Text(result));
    }

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task An_owner_moves_a_booking_to_the_time_they_pick(string protocolVersion)
    {
        var id = AnswerBookingOnTheCalendar();
        mcp.Web.Answer(HttpMethod.Get, $"/api/businesses/mine/bookings/{id}/slots", OneDayOfSlots());
        mcp.Web.Answer(HttpMethod.Post, $"/api/businesses/mine/bookings/{id}/reschedule",
            new { id, serviceName = "Haircut", staffName = "Anna", date = Day, start = "10:00", end = "10:30" });
        await using var client = await ConnectAsync(protocolVersion, _ => Accept(new() { ["startsAt"] = Ten.ToString("O") }));

        var result = await client.CallToolAsync("owner_reschedule_booking", new Dictionary<string, object?> { ["bookingId"] = id, ["date"] = Day });

        Assert.NotEqual(true, result.IsError);
        var (_, body, _) = Assert.Single(mcp.Web.Received, request => request.Request == $"POST /api/businesses/mine/bookings/{id}/reschedule");
        Assert.Equal(Ten, JsonDocument.Parse(body!).RootElement.GetProperty("startsAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(Stateless)]
    [InlineData(WithSession)]
    public async Task An_owner_books_a_client_who_phoned_under_the_name_and_email_they_give(string protocolVersion)
    {
        var slug = AnswerMyBusiness();
        mcp.Web.Answer(HttpMethod.Post, "/api/businesses/mine/bookings",
            new { id = Guid.NewGuid(), serviceName = "Haircut", staffName = "Anna", date = Day, start = "09:00", end = "09:30" },
            HttpStatusCode.Created);
        ElicitRequestParams? asked = null;
        await using var client = await ConnectAsync(protocolVersion, request =>
        {
            asked = request;
            return Accept(new() { ["startsAt"] = Nine.ToString("O"), ["clientName"] = "Maria", ["clientEmail"] = "maria@example.com" });
        });

        var result = await client.CallToolAsync("owner_book_for_client", new Dictionary<string, object?> { ["serviceId"] = Haircut });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Book Haircut (30 min, 80 PLN) for a client", asked!.Message);
        Assert.Contains(mcp.Web.Received, request => request.Request.StartsWith($"GET /api/public/businesses/{slug}/slots"));
        var (_, body, _) = mcp.Web.Received.Last(request => request.Request == "POST /api/businesses/mine/bookings");
        var booking = JsonDocument.Parse(body!).RootElement;
        Assert.Equal(Nine, booking.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal("Maria", booking.GetProperty("clientName").GetString());
        Assert.Equal("maria@example.com", booking.GetProperty("clientEmail").GetString());
    }

    [Fact]
    public async Task A_client_cannot_book_for_someone_else()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/me", new { name = "Radek", email = "radek@example.com", business = (object?)null });
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("owner_book_for_client", new Dictionary<string, object?> { ["serviceId"] = Haircut });

        Assert.True(result.IsError);
        Assert.Contains("Only the owner of a business", Text(result));
    }

    const string Stateless = "2026-07-28";
    const string WithSession = "2025-11-25";
    const string Day = "2026-11-02";

    static readonly Guid Haircut = Guid.Parse("0199b1c4-7a3e-7f00-8a1b-2c3d4e5f6a01");
    static readonly DateTimeOffset Nine = DateTimeOffset.Parse("2026-11-02T09:00:00+00:00");
    static readonly DateTimeOffset Ten = DateTimeOffset.Parse("2026-11-02T10:00:00+00:00");

    /// <summary>One booking on the owner's calendar for <see cref="Day"/>: Radek's haircut with Anna at 09:00.</summary>
    Guid AnswerBookingOnTheCalendar()
    {
        var id = Guid.NewGuid();
        mcp.Web.Answer(HttpMethod.Get, $"/api/businesses/mine/bookings?view=day&date={Day}", new
        {
            view = "day", date = Day, firstDay = Day, lastDay = Day, timeZone = "UTC",
            staff = Array.Empty<object>(),
            bookings = new[]
            {
                new
                {
                    id, day = Day, start = "09:00", end = "09:30", staffMemberId = Guid.NewGuid(),
                    staffName = "Anna", serviceName = "Haircut", clientName = "Radek", clientEmail = "radek@example.com",
                },
            },
            timeOff = Array.Empty<object>(),
        });
        return id;
    }

    /// <summary>The signed-in user owns a business, with a haircut and free times on <see cref="Day"/>.</summary>
    string AnswerMyBusiness()
    {
        var slug = $"business-{Guid.NewGuid():N}";
        mcp.Web.Answer(HttpMethod.Get, "/api/me", new { name = "Anna", email = "anna@example.com", business = new { name = "Anna's Hair", slug } });
        mcp.Web.Answer(HttpMethod.Get, $"/api/public/businesses/{slug}", new
        {
            name = "Anna's Hair", slug, timeZone = "UTC", cancellationNoticeHours = 24,
            services = new[] { new { id = Haircut, name = "Haircut", durationMinutes = 30, price = 80m, currency = "PLN", staff = Array.Empty<object>() } },
        });
        mcp.Web.Answer(HttpMethod.Get, $"/api/public/businesses/{slug}/slots?serviceId={Haircut}", OneDayOfSlots());
        return slug;
    }

    static object OneDayOfSlots() => new
    {
        timeZone = "UTC",
        days = new[] { new { date = Day, slots = new[] { new { start = "09:00", startsAt = Nine }, new { start = "10:00", startsAt = Ten } } } },
    };

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
