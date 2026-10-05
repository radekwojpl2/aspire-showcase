using System.Globalization;
using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.Scheduling;

record PublicStaff(Guid Id, string Name);

record PublicService(
    Guid Id, string Name, int DurationMinutes, decimal Price, string Currency, IReadOnlyList<PublicStaff> Staff);

record PublicBusiness(string Name, string Slug, string TimeZone, IReadOnlyList<PublicService> Services);

/// <param name="Start">The local time, HH:mm.</param>
/// <param name="StartsAt">The instant, to book it with.</param>
record PublicSlot(string Start, DateTimeOffset StartsAt);

/// <param name="Date">The local date, yyyy-MM-dd.</param>
record PublicDay(string Date, IReadOnlyList<PublicSlot> Slots);

record PublicSlots(string TimeZone, IReadOnlyList<PublicDay> Days);

/// <param name="StaffMemberId">Who to book, or null for anyone who's free.</param>
record BookSlot(Guid? ServiceId, Guid? StaffMemberId, DateTimeOffset? StartsAt, string? ClientName, string? ClientEmail);

record BookingConfirmation(Guid Id, string ServiceName, string StaffName, string Date, string Start, string End);

/// <summary>
/// The public booking page's API, under /public/businesses/{slug} (user stories MVP-1 to MVP-4):
/// the business and its free slots for anyone, and booking for signed-in clients.
/// </summary>
static class PublicBookingEndpoints
{
    const string SlotTaken = "This time was just taken.";

    public static void Map(IEndpointRouteBuilder api)
    {
        var business = api.MapGroup("/public/businesses/{slug}");

        // MVP-1: no sign-in needed to see what's on offer.
        business.MapGet("/", async (string slug, IBusinessDirectory directory, CancellationToken cancellation) =>
        {
            if (await directory.FindBySlugAsync(slug, cancellation) is not { } found)
            {
                return Results.NotFound();
            }

            var staff = await directory.StaffAsync(found.Id, cancellation);
            var services = (await directory.ServicesAsync(found.Id, cancellation))
                .Where(service => !service.IsHidden)
                .Select(service => new PublicService(
                    service.Id.Value, service.Name, (int)service.Duration.TotalMinutes, service.Price, service.Currency,
                    // MVP-2: only the staff who do the service are offered.
                    staff.Where(member => member.DoesAllServices || member.ServiceIds.Contains(service.Id))
                        .Select(member => new PublicStaff(member.Id.Value, member.Name))
                        .ToList()))
                .ToList();
            return Results.Ok(new PublicBusiness(found.Name, found.Slug, found.TimeZone, services));
        })
        .WithName("GetPublicBusiness");

        // MVP-1 and MVP-2: free slots for the next 4 weeks, for one staff member or anyone.
        business.MapGet("/slots", async (
            string slug, Guid? serviceId, Guid? staffMemberId, IBusinessDirectory directory, Availability availability,
            SchedulingTelemetry telemetry, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("availability.slots");

            if (serviceId is null ||
                await directory.FindBySlugAsync(slug, cancellation) is not { } found ||
                await availability.FindOfferAsync(found, new ServiceId(serviceId.Value), ToStaffId(staffMemberId), cancellation)
                    is not { } offer)
            {
                return Results.NotFound();
            }

            var slots = await availability.FreeSlotsAsync(offer, availability.Today(offer), Availability.DaysAhead, cancellation);
            activity?.SetTag("availability.slots", slots.Count);
            return Results.Ok(ToPublicSlots(slots, offer));
        })
        .WithName("GetFreeSlots");

        // MVP-4: booking needs a signed-in client, whose account the booking is tied to (MVP-3).
        business.MapPost("/bookings", async (
            string slug, BookSlot request, ClaimsPrincipal user, IBusinessDirectory directory, Availability availability,
            IServiceScopeFactory scopes, SchedulingTelemetry telemetry, TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("bookings.book");

            if (request.ServiceId is null ||
                await directory.FindBySlugAsync(slug, cancellation) is not { } found ||
                await availability.FindOfferAsync(
                    found, new ServiceId(request.ServiceId.Value), ToStaffId(request.StaffMemberId), cancellation)
                    is not { } offer)
            {
                return Results.NotFound();
            }

            Attendee attendee;
            try
            {
                attendee = Attendee.Create(user.FindFirstValue("sub"), request.ClientName, request.ClientEmail);
            }
            catch (DomainValidationException exception)
            {
                return Results.ValidationProblem(exception.Errors.ToDictionary());
            }
            if (request.StartsAt is not { } startsAt)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["startsAt"] = ["Choose a time."] });
            }

            // Only a slot that's free right now, on the grid and within someone's hours, can be booked.
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, offer.TimeZone).DateTime);
            var slot = (await availability.FreeSlotsAsync(offer, day, 1, cancellation))
                .SingleOrDefault(free => free.Start == startsAt.ToUniversalTime());

            // "Anyone" takes whoever is free; if the database refuses one (someone booked them a
            // moment ago), the next one is tried. Each attempt gets a scope of its own, so a refused
            // one leaves no outbox message behind for the next to send.
            foreach (var staffMemberId in slot?.FreeStaff ?? [])
            {
                await using var attempt = scopes.CreateAsyncScope();
                attempt.ServiceProvider.GetRequiredService<BusinessScope>().BusinessId = found.Id;
                var booking = Booking.Book(
                    found.Id, staffMemberId, offer.Service.Id, startsAt, offer.Service.Duration, attendee, time.GetUtcNow());
                var result = await attempt.ServiceProvider.GetRequiredService<Bookings>().AddAsync(booking, cancellation);
                telemetry.Booking(result, "client");
                if (result == BookingResult.Booked)
                {
                    activity?.SetTag("booking.id", booking.Id.Value);
                    return Results.Created(
                        $"/api/public/businesses/{slug}/bookings/{booking.Id}", ToConfirmation(booking, offer, staffMemberId));
                }
            }

            // Nobody to try at all is a taken slot too; refused attempts were counted above.
            if (slot is null || slot.FreeStaff.Count == 0)
            {
                telemetry.Booking(BookingResult.SlotTaken, "client");
            }
            return Results.Problem(title: SlotTaken, statusCode: StatusCodes.Status409Conflict);
        })
        .RequireAuthorization()
        .WithName("BookSlot");
    }

    static StaffMemberId? ToStaffId(Guid? id) => id is { } value ? new StaffMemberId(value) : null;

    static PublicSlots ToPublicSlots(IReadOnlyList<FreeSlot> slots, Offer offer) => new(
        offer.Business.TimeZone,
        slots
            .Select(slot => (Slot: slot, Local: TimeZoneInfo.ConvertTime(slot.Start, offer.TimeZone)))
            .GroupBy(slot => DateOnly.FromDateTime(slot.Local.DateTime))
            .Select(day => new PublicDay(
                day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.Select(slot => new PublicSlot(slot.Local.ToString("HH:mm", CultureInfo.InvariantCulture), slot.Slot.Start))
                    .ToList()))
            .ToList());

    static BookingConfirmation ToConfirmation(Booking booking, Offer offer, StaffMemberId staffMemberId)
    {
        var start = TimeZoneInfo.ConvertTime(booking.Start, offer.TimeZone);
        var end = TimeZoneInfo.ConvertTime(booking.End, offer.TimeZone);
        return new BookingConfirmation(
            booking.Id.Value,
            offer.Service.Name,
            offer.Staff.Single(member => member.Id == staffMemberId).Name,
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            start.ToString("HH:mm", CultureInfo.InvariantCulture),
            end.ToString("HH:mm", CultureInfo.InvariantCulture));
    }
}
