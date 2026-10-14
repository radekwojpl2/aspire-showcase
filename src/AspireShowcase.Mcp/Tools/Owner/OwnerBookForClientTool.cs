using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Owner;

/// <summary>An owner booking someone who phoned or walked in (user story V1-5), once they confirm it.</summary>
[McpServerToolType]
sealed class OwnerBookForClientTool(WebApi web)
{
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
            fields["startsAt"] = AskUser.Choice("When", FreeTimes.Choices(slots));
        }
        fields["clientName"] = AskUser.Text("Client's name", clientName);
        fields["clientEmail"] = AskUser.Text("Client's email", clientEmail);

        var when = startsAt is { } at ? $" on {FreeTimes.Local(at, business.TimeZone)}" : "";
        var answer = await AskUser.AskAsync(context,
            $"Book {service.Name} ({service.DurationMinutes} min, {service.Price} {service.Currency}) for a client{when}?",
            fields, cancellation);
        if (answer is { Accepted: false })
        {
            return ChangeResult<BookingConfirmation>.Declined("The owner didn't book.");
        }

        var request = new BookSlot(
            serviceId, staffMemberId,
            startsAt ?? FreeTimes.Picked(answer),
            answer?.Text("clientName") ?? clientName,
            answer?.Text("clientEmail") ?? clientEmail);
        if (request.StartsAt is null || string.IsNullOrWhiteSpace(request.ClientName) || string.IsNullOrWhiteSpace(request.ClientEmail))
        {
            throw new McpException("Ask the owner for the time and the client's name and email, and pass them as startsAt, clientName and clientEmail.");
        }

        return ChangeResult<BookingConfirmation>.Made(await web.BookForClientAsync(request, cancellation));
    }
}
