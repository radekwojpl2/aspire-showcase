using System.Globalization;
using System.Security.Claims;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.SharedKernel;

namespace AspireShowcase.Scheduling;

record PublicStaff(Guid Id, string Name);

record PublicService(
    Guid Id, string Name, int DurationMinutes, decimal Price, string Currency, IReadOnlyList<PublicStaff> Staff);

/// <param name="CancellationNoticeHours">Clients can cancel or move a booking up to this many hours before it; 0 for until it starts.</param>
/// <param name="Address">Where it is (V1-6), its lines separated by \n; like <paramref name="Description"/>
/// and <paramref name="LogoUrl"/>, null for none.</param>
record PublicBusiness(
    string Name, string Slug, string TimeZone, int CancellationNoticeHours, IReadOnlyList<PublicService> Services,
    string? Address, string? Description, string? LogoUrl);

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
    public const string SlotTaken = "This time was just taken.";

    public static void Map(IEndpointRouteBuilder api)
    {
        var business = api.MapGroup("/public/businesses/{slug}");

        // MVP-1: no sign-in needed to see what's on offer.
        business.MapGet("/", async (
            string slug, IBusinessDirectory directory, SchedulingDbContext db, BusinessScope scope,
            CancellationToken cancellation) =>
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
            // V1-3: clients see the policy before they book.
            scope.BusinessId = found.Id;
            var policy = await db.PolicyOfAsync(found.Id, cancellation);
            return Results.Ok(new PublicBusiness(
                found.Name, found.Slug, found.TimeZone, (int)policy.Notice.TotalHours, services, found.Address,
                found.Description, found.LogoUrl));
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
            SchedulingDbContext db, IServiceScopeFactory scopes, SchedulingTelemetry telemetry, TimeProvider time,
            CancellationToken cancellation) =>
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

            return await BookAsync(
                offer, request, user.FindFirstValue("sub"), BookedBy.Client,
                id => $"/api/public/businesses/{slug}/bookings/{id}",
                availability, db, scopes, telemetry, activity, time, cancellation);
        })
        .RequireAuthorization()
        .WithName("BookSlot");
    }

    /// <summary>
    /// Books a free slot of the offer, for the client online or for the business on their behalf
    /// (V1-5): the same rules either way. Answers 201 with the confirmation, 400 when the attendee
    /// or time is missing, or 409 when the slot isn't free.
    /// </summary>
    /// <param name="userId">The client's user ID; null for someone without an account.</param>
    /// <param name="location">Where the new booking can be found, for the 201's Location header.</param>
    public static async Task<IResult> BookAsync(
        Offer offer, BookSlot request, string? userId, BookedBy by, Func<BookingId, string> location,
        Availability availability, SchedulingDbContext db, IServiceScopeFactory scopes, SchedulingTelemetry telemetry,
        System.Diagnostics.Activity? activity, TimeProvider time, CancellationToken cancellation)
    {
        var source = by.ToString().ToLowerInvariant();
        Attendee attendee;
        try
        {
            attendee = Attendee.Create(userId, request.ClientName, request.ClientEmail);
        }
        catch (DomainValidationException exception)
        {
            return Results.ValidationProblem(exception.Errors.ToDictionary());
        }
        if (request.StartsAt is not { } startsAt)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["startsAt"] = ["Choose a time."] });
        }

        activity?.SetTag("service.buffer_minutes", (int)offer.Service.Buffer.TotalMinutes);
        activity?.SetTag("booking.booked_by", source);

        // Only a slot that's free right now, on the grid and within someone's hours, can be booked.
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, offer.TimeZone).DateTime);
        var slot = (await availability.FreeSlotsAsync(offer, day, 1, cancellation))
            .SingleOrDefault(free => free.Start == startsAt.ToUniversalTime());
        // The booking keeps the policy the client saw (V1-3).
        var notice = (await db.PolicyOfAsync(offer.Business.Id, cancellation)).Notice;

        // "Anyone" takes whoever is free; if the database refuses one (someone booked them a
        // moment ago), the next one is tried. Each attempt gets a scope of its own, so a refused
        // one leaves no outbox message behind for the next to send.
        foreach (var staffMemberId in slot?.FreeStaff ?? [])
        {
            await using var attempt = scopes.CreateAsyncScope();
            attempt.ServiceProvider.GetRequiredService<BusinessScope>().BusinessId = offer.Business.Id;
            var booking = Booking.Book(
                offer.Business.Id, staffMemberId, offer.Service.Id, startsAt, offer.Service.Duration, offer.Service.Buffer,
                notice, attendee, time.GetUtcNow(), by);
            var result = await attempt.ServiceProvider.GetRequiredService<Bookings>().AddAsync(booking, cancellation);
            telemetry.Booking(result, source);
            if (result == BookingResult.Booked)
            {
                activity?.SetTag("booking.id", booking.Id.Value);
                return Results.Created(location(booking.Id), ToConfirmation(booking, offer, staffMemberId));
            }
        }

        // Nobody to try at all is a taken slot too; refused attempts were counted above.
        if (slot is null || slot.FreeStaff.Count == 0)
        {
            telemetry.Booking(BookingResult.SlotTaken, source);
        }
        return Results.Problem(title: SlotTaken, statusCode: StatusCodes.Status409Conflict);
    }

    public static StaffMemberId? ToStaffId(Guid? id) => id is { } value ? new StaffMemberId(value) : null;

    public static PublicSlots ToPublicSlots(IReadOnlyList<FreeSlot> slots, Offer offer) => new(
        offer.Business.TimeZone,
        slots
            .Select(slot => (Slot: slot, Local: TimeZoneInfo.ConvertTime(slot.Start, offer.TimeZone)))
            .GroupBy(slot => DateOnly.FromDateTime(slot.Local.DateTime))
            .Select(day => new PublicDay(
                day.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.Select(slot => new PublicSlot(slot.Local.ToString("HH:mm", CultureInfo.InvariantCulture), slot.Slot.Start))
                    .ToList()))
            .ToList());

    public static BookingConfirmation ToConfirmation(Booking booking, Offer offer, StaffMemberId staffMemberId)
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
