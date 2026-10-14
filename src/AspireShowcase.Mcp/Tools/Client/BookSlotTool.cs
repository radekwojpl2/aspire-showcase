using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Client;

/// <summary>
/// Booking a free time, as a signed-in client does on the booking page (user story MVP-4).
/// The user confirms it, through <see cref="AskUser"/>, before web makes it.
/// </summary>
[McpServerToolType]
sealed class BookSlotTool(WebApi web)
{
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
            fields["startsAt"] = AskUser.Choice("When", FreeTimes.Choices(slots));
        }
        fields["clientName"] = AskUser.Text("Your name", clientName);
        fields["clientEmail"] = AskUser.Text("Your email", clientEmail);

        var when = startsAt is { } at ? $" on {FreeTimes.Local(at, business.TimeZone)}" : "";
        var answer = await AskUser.AskAsync(context,
            $"Book {service.Name} ({service.DurationMinutes} min, {service.Price} {service.Currency}) at {business.Name}{when}?",
            fields, cancellation);
        if (answer is { Accepted: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The user didn't book.");
        }

        var request = new BookSlot(
            serviceId, staffMemberId,
            startsAt ?? FreeTimes.Picked(answer),
            answer?.Text("clientName") ?? clientName,
            answer?.Text("clientEmail") ?? clientEmail);
        if (request.StartsAt is null || string.IsNullOrWhiteSpace(request.ClientName) || string.IsNullOrWhiteSpace(request.ClientEmail))
        {
            throw new McpException("Ask the user for the time, their name and their email, and pass them as startsAt, clientName and clientEmail.");
        }

        return ChangeResult<BookingConfirmation>.Made(await web.BookAsync(slug, request, cancellation));
    }
}
