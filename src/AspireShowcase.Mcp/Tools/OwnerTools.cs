using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// What an owner does on their dashboard's calendar (user stories MVP-12, MVP-14, V1-1, V1-4 and
/// V1-5): see the bookings, cancel or move one, and book someone who phoned. Each change is
/// confirmed by the owner, through <see cref="AskUser"/>, before web makes it; web emails the
/// client as it does for a change made in the app.
/// </summary>
/// <remarks>
/// Listed to every signed-in user: the MCP server's token says who they are, not whether they
/// own a business, which only the token exchanged for web carries. web answers 403 to a client.
/// </remarks>
[McpServerToolType]
sealed class OwnerTools(WebApi web, TimeProvider time)
{
    [McpServerTool(Name = "owner_calendar", Title = "My business's calendar", ReadOnly = true, OpenWorld = false)]
    [Description("For the owner of a business: its bookings and time off for a day or a week, for everyone or " +
        "one staff member, in the business's time zone, with each client's name and email.")]
    public Task<CalendarResponse> OwnerCalendar(
        [Description("\"day\" or \"week\".")] string view = "week",
        [Description("A day in it, yyyy-MM-dd; leave out for today.")] string? date = null,
        [Description("Only this staff member's bookings; leave out for everyone's.")] Guid? staffMemberId = null,
        CancellationToken cancellation = default) =>
        web.GetCalendarAsync(view, date, staffMemberId, cancellation);

    [McpServerTool(Name = "owner_cancel_booking", Title = "Cancel a client's booking", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("For the owner of a business: cancels a client's booking, such as when someone's ill, once the " +
        "owner confirms it. The client is emailed, and the time is free again at once.")]
    public async Task<ChangeResult<CalendarBooking>> OwnerCancelBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from owner_calendar.")] Guid bookingId,
        [Description("The booking's day, yyyy-MM-dd, from owner_calendar.")] string date,
        CancellationToken cancellation = default)
    {
        var (booking, _) = await FindAsync(bookingId, date, cancellation);

        var answer = await AskUser.AskAsync(context,
            $"Cancel {Describe(booking)}? {booking.ClientName} will get an email.",
            new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["confirm"] = AskUser.YesNo("Cancel it") },
            cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<CalendarBooking>.Declined("The owner kept the booking.");
        }

        await web.CancelBookingAsync(bookingId, cancellation);
        return ChangeResult<CalendarBooking>.Made(booking);
    }

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
        return slots with { Days = PublicTools.Window(slots, from, days, time) };
    }

    [McpServerTool(Name = "owner_reschedule_booking", Title = "Move a client's booking", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("For the owner of a business: moves a client's booking to another time or staff member. Without " +
        "startsAt, the owner picks one of the next free times; either way they confirm the move before it's made. " +
        "The client is emailed.")]
    public async Task<ChangeResult<BookingConfirmation>> OwnerRescheduleBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from owner_calendar.")] Guid bookingId,
        [Description("The booking's day, yyyy-MM-dd, from owner_calendar.")] string date,
        [Description("A startsAt from owner_find_reschedule_slots; leave out to let the owner pick.")] DateTimeOffset? startsAt = null,
        [Description("Who it should be with; leave out for anyone who's free.")] Guid? staffMemberId = null,
        CancellationToken cancellation = default)
    {
        var (booking, timeZone) = await FindAsync(bookingId, date, cancellation);

        var fields = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        if (startsAt is null)
        {
            var slots = await web.GetBookingSlotsAsync(bookingId, staffMemberId, cancellation);
            fields["startsAt"] = AskUser.Choice("New time", ClientTools.Choices(slots));
        }
        else
        {
            fields["confirm"] = AskUser.YesNo($"Move it to {ClientTools.Local(startsAt.Value, timeZone)}");
        }

        var answer = await AskUser.AskAsync(context,
            $"Move {Describe(booking)}? {booking.ClientName} will get an email.", fields, cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The owner kept the booking where it was.");
        }

        var newStart = startsAt ?? ClientTools.Picked(answer)
            ?? throw new McpException("Ask the owner for the new time, and pass it as startsAt.");
        return ChangeResult<BookingConfirmation>.Made(
            await web.RescheduleBookingAsync(bookingId, newStart, staffMemberId, cancellation));
    }

    [McpServerTool(Name = "owner_book_for_client", Title = "Book a client", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("For the owner of a business: books a service for a client who phoned or walked in, under the " +
        "client's name and email, which get the same emails as a booking made online. Without startsAt, the " +
        "owner picks one of the next free times; either way they confirm the booking before it's made. If their " +
        "client can't ask them, pass startsAt, clientName and clientEmail, after asking them.")]
    public async Task<ChangeResult<BookingConfirmation>> OwnerBookForClient(
        RequestContext<CallToolRequestParams> context,
        [Description("The service's ID, from get_business with the owner's booking link, which whoami gives.")] Guid serviceId,
        [Description("A startsAt from find_free_slots; leave out to let the owner pick.")] DateTimeOffset? startsAt = null,
        [Description("Only with this staff member; leave out for anyone who's free.")] Guid? staffMemberId = null,
        [Description("The client's name, as the calendar will show it.")] string? clientName = null,
        [Description("The client's email, where their confirmation goes.")] string? clientEmail = null,
        CancellationToken cancellation = default)
    {
        var slug = (await web.GetMeAsync(cancellation)).Business?.Slug
            ?? throw new McpException("Only the owner of a business can do that, and the user isn't one.");
        var business = await web.GetBusinessAsync(slug, cancellation);
        var service = business.Services.FirstOrDefault(service => service.Id == serviceId)
            ?? throw new McpException($"{business.Name} has no service {serviceId}; get_business lists them.");

        var fields = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        if (startsAt is null)
        {
            var slots = await web.GetFreeSlotsAsync(slug, serviceId, staffMemberId, cancellation);
            fields["startsAt"] = AskUser.Choice("When", ClientTools.Choices(slots));
        }
        fields["clientName"] = AskUser.Text("Client's name", clientName);
        fields["clientEmail"] = AskUser.Text("Client's email", clientEmail);

        var when = startsAt is { } at ? $" on {ClientTools.Local(at, business.TimeZone)}" : "";
        var answer = await AskUser.AskAsync(context,
            $"Book {service.Name} ({service.DurationMinutes} min, {service.Price} {service.Currency}) for a client{when}?",
            fields, cancellation);
        if (answer is { Accepted: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The owner didn't book.");
        }

        var request = new BookSlot(
            serviceId, staffMemberId,
            startsAt ?? ClientTools.Picked(answer),
            answer?.Text("clientName") ?? clientName,
            answer?.Text("clientEmail") ?? clientEmail);
        if (request.StartsAt is null || string.IsNullOrWhiteSpace(request.ClientName) || string.IsNullOrWhiteSpace(request.ClientEmail))
        {
            throw new McpException("Ask the owner for the time and the client's name and email, and pass them as startsAt, clientName and clientEmail.");
        }

        return ChangeResult<BookingConfirmation>.Made(await web.BookForClientAsync(request, cancellation));
    }

    /// <summary>
    /// The booking, from its day of the calendar: web has no endpoint for one booking, and the
    /// owner is asked about it by what the calendar shows.
    /// </summary>
    async Task<(CalendarBooking Booking, string TimeZone)> FindAsync(Guid bookingId, string date, CancellationToken cancellation)
    {
        var calendar = await web.GetCalendarAsync("day", date, null, cancellation);
        return calendar.Bookings.FirstOrDefault(booking => booking.Id == bookingId) is { } found
            ? (found, calendar.TimeZone)
            : throw new McpException($"The business has no booking {bookingId} on {date}; owner_calendar lists them.");
    }

    static string Describe(CalendarBooking booking) =>
        $"{booking.ServiceName} with {booking.StaffName} for {booking.ClientName} on {booking.Day} at {booking.Start}";
}
