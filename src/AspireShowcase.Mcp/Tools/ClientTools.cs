using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// What a signed-in client does on the booking page and under My bookings (user stories MVP-4,
/// MVP-7, V1-4): book a free time, see their bookings, cancel or move one. Each change is
/// confirmed by the user, through <see cref="AskUser"/>, before web makes it.
/// </summary>
[McpServerToolType]
[Authorize]
sealed class ClientTools(WebApi web)
{
    // As many free times as a form can offer in one list.
    const int MaxChoices = 20;

    [McpServerTool(Name = "book_slot", Title = "Book a time", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Books a service at a business for the signed-in user. Without startsAt, the user picks one of " +
        "the next free times; either way they confirm the booking, with the name and email it's under, before " +
        "it's made. If their client can't ask them, pass startsAt, clientName and clientEmail, after asking them.")]
    public async Task<ChangeResult<BookingConfirmation>> BookSlot(
        RequestContext<CallToolRequestParams> context,
        [Description("The business's booking link, such as \"anna-hair\".")] string slug,
        [Description("The service's ID, from get_business.")] Guid serviceId,
        [Description("A startsAt from find_free_slots; leave out to let the user pick.")] DateTimeOffset? startsAt = null,
        [Description("Only with this staff member; leave out for anyone who's free.")] Guid? staffMemberId = null,
        [Description("The name to book under, as the business will see it.")] string? clientName = null,
        [Description("The email the confirmation goes to.")] string? clientEmail = null,
        CancellationToken cancellation = default)
    {
        var business = await web.GetBusinessAsync(slug, cancellation);
        var service = business.Services.FirstOrDefault(service => service.Id == serviceId)
            ?? throw new McpException($"{business.Name} has no service {serviceId}; get_business lists them.");

        var fields = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        if (startsAt is null)
        {
            var slots = await web.GetFreeSlotsAsync(slug, serviceId, staffMemberId, cancellation);
            fields["startsAt"] = AskUser.Choice("When", Choices(slots));
        }
        fields["clientName"] = AskUser.Text("Your name", clientName);
        fields["clientEmail"] = AskUser.Text("Your email", clientEmail);

        var when = startsAt is { } at ? $" on {Local(at, business.TimeZone)}" : "";
        var answer = await AskUser.AskAsync(context,
            $"Book {service.Name} ({service.DurationMinutes} min, {service.Price} {service.Currency}) at {business.Name}{when}?",
            fields, cancellation);
        if (answer is { Accepted: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The user didn't book.");
        }

        var request = new BookSlot(
            serviceId, staffMemberId,
            startsAt ?? Picked(answer),
            answer?.Text("clientName") ?? clientName,
            answer?.Text("clientEmail") ?? clientEmail);
        if (request.StartsAt is null || string.IsNullOrWhiteSpace(request.ClientName) || string.IsNullOrWhiteSpace(request.ClientEmail))
        {
            throw new McpException("Ask the user for the time, their name and their email, and pass them as startsAt, clientName and clientEmail.");
        }

        return ChangeResult<BookingConfirmation>.Made(await web.BookAsync(slug, request, cancellation));
    }

    [McpServerTool(Name = "my_bookings", Title = "My bookings", ReadOnly = true, OpenWorld = false)]
    [Description("The signed-in user's upcoming bookings, with every business they booked with, and until when " +
        "each can still be cancelled or moved.")]
    public Task<IReadOnlyList<ClientBooking>> MyBookings(CancellationToken cancellation) =>
        web.GetMyBookingsAsync(cancellation);

    [McpServerTool(Name = "cancel_booking", Title = "Cancel a booking", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Cancels one of the signed-in user's bookings, once they confirm it. The time is free again at once.")]
    public async Task<ChangeResult<ClientBooking>> CancelBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from my_bookings.")] Guid bookingId,
        CancellationToken cancellation = default)
    {
        var booking = await FindMineAsync(bookingId, cancellation);

        var answer = await AskUser.AskAsync(context,
            $"Cancel {Describe(booking)}?",
            new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["confirm"] = AskUser.YesNo("Cancel it") },
            cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<ClientBooking>.Declined("The user kept the booking.");
        }

        await web.CancelMyBookingAsync(bookingId, cancellation);
        return ChangeResult<ClientBooking>.Made(booking);
    }

    [McpServerTool(Name = "find_reschedule_slots", Title = "Find times to move a booking to", ReadOnly = true, OpenWorld = false)]
    [Description("The free times one of the signed-in user's bookings can be moved to, day by day, in the " +
        "business's time zone.")]
    public async Task<PublicSlots> FindRescheduleSlots(
        [Description("The booking's ID, from my_bookings.")] Guid bookingId,
        [Description("Only times with this staff member; leave out for anyone who's free.")] Guid? staffMemberId = null,
        [Description("The first day, yyyy-MM-dd; leave out for today.")] string? from = null,
        [Description("How many days from the first one, up to 28.")] int days = 7,
        CancellationToken cancellation = default)
    {
        var slots = await web.GetMyBookingSlotsAsync(bookingId, staffMemberId, cancellation);
        return slots with { Days = PublicTools.Window(slots.Days, from, days) };
    }

    [McpServerTool(Name = "reschedule_booking", Title = "Move a booking", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Moves one of the signed-in user's bookings to another time. Without startsAt, the user picks " +
        "one of the next free times; either way they confirm the move before it's made.")]
    public async Task<ChangeResult<BookingConfirmation>> RescheduleBooking(
        RequestContext<CallToolRequestParams> context,
        [Description("The booking's ID, from my_bookings.")] Guid bookingId,
        [Description("A startsAt from find_reschedule_slots; leave out to let the user pick.")] DateTimeOffset? startsAt = null,
        [Description("Who it should be with; leave out for anyone who's free.")] Guid? staffMemberId = null,
        CancellationToken cancellation = default)
    {
        var booking = await FindMineAsync(bookingId, cancellation);

        var fields = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        if (startsAt is null)
        {
            var slots = await web.GetMyBookingSlotsAsync(bookingId, staffMemberId, cancellation);
            fields["startsAt"] = AskUser.Choice("New time", Choices(slots));
        }
        else
        {
            fields["confirm"] = AskUser.YesNo($"Move it to {Local(startsAt.Value, booking.TimeZone)}");
        }

        var answer = await AskUser.AskAsync(context, $"Move {Describe(booking)}?", fields, cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The user kept the booking where it was.");
        }

        var newStart = startsAt ?? Picked(answer)
            ?? throw new McpException("Ask the user for the new time, and pass it as startsAt.");
        return ChangeResult<BookingConfirmation>.Made(
            await web.RescheduleMyBookingAsync(bookingId, newStart, staffMemberId, cancellation));
    }

    async Task<ClientBooking> FindMineAsync(Guid bookingId, CancellationToken cancellation) =>
        (await web.GetMyBookingsAsync(cancellation)).FirstOrDefault(booking => booking.Id == bookingId)
            ?? throw new McpException($"None of the user's upcoming bookings is {bookingId}; my_bookings lists them.");

    static string Describe(ClientBooking booking) =>
        $"{booking.ServiceName} with {booking.StaffName} at {booking.BusinessName} on {booking.Date} at {booking.Start}";

    /// <summary>The next free times, as choices: the instant to book, shown as the local day and time.</summary>
    static IEnumerable<(string Value, string Title)> Choices(PublicSlots slots)
    {
        var choices = slots.Days
            .SelectMany(day => day.Slots.Select(slot => (slot.StartsAt.ToString("O", CultureInfo.InvariantCulture), $"{day.Date} {slot.Start}")))
            .Take(MaxChoices)
            .ToList();
        return choices.Count > 0 ? choices : throw new McpException("There are no free times to choose from.");
    }

    /// <summary>The time the user picked, if they were asked to.</summary>
    static DateTimeOffset? Picked(Answer? answer) =>
        DateTimeOffset.TryParse(answer?.Text("startsAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var picked)
            ? picked
            : null;

    static string Local(DateTimeOffset at, string timeZone) =>
        TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).ToString("yyyy-MM-dd HH:mm");
}

/// <summary>A change the user was asked to confirm: done, with its result, or declined.</summary>
sealed record ChangeResult<T>(bool Done, string? Message, T? Result)
{
    public static ChangeResult<T> Declined(string message) => new(false, message, default);

    public static ChangeResult<T> Made(T result) => new(true, null, result);
}
