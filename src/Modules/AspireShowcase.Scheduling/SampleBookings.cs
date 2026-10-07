using System.Globalization;
using AspireShowcase.BusinessSetup.PublicClient;

namespace AspireShowcase.Scheduling;

/// <summary>
/// Development only: fills the next 7 days of every business with made-up bookings, so the
/// calendar has something to show before clients can book (MVP-4). Called from a command on the
/// "web" resource in the Aspire dashboard; not mapped outside Development.
/// </summary>
/// <remarks>
/// Each booking goes through <see cref="Booking.Book"/> and <see cref="Bookings.AddAsync"/>, as
/// real ones will: within the staff member's hours, for a service they do, and refused by the
/// database if it overlaps one of theirs, which running the command twice shows.
/// </remarks>
static class SampleBookings
{
    static readonly string[] Clients =
    [
        "Ola Nowak", "Jan Kowalski", "Emma Smith", "Lucas Martin", "Sofia Rossi", "Noah Müller",
        "Zofia Wiśniewska", "Liam Brown", "Mia Dubois", "Piotr Zieliński",
    ];

    record Result(int Booked, int SlotTaken);

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/dev/sample-bookings", async (
            IBusinessDirectory directory, SchedulingDbContext db, BusinessScope scope, Bookings bookings,
            SchedulingTelemetry telemetry, TimeProvider time, CancellationToken cancellation) =>
        {
            using var activity = telemetry.StartActivity("bookings.sample");
            var random = new Random();
            var now = time.GetUtcNow();
            int booked = 0, taken = 0;

            foreach (var business in await directory.ListAsync(cancellation))
            {
                scope.BusinessId = business.Id;
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
                var services = (await directory.ServicesAsync(business.Id, cancellation))
                    .Where(service => !service.IsHidden)
                    .ToList();
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

                foreach (var member in await directory.StaffAsync(business.Id, cancellation))
                {
                    var theirs = services
                        .Where(service => member.DoesAllServices || member.ServiceIds.Contains(service.Id))
                        .ToList();
                    if (theirs.Count == 0)
                    {
                        continue;
                    }

                    for (var day = today; day < today.AddDays(7); day = day.AddDays(1))
                    {
                        foreach (var period in member.WorkingHours.Where(period => period.Day == day.DayOfWeek))
                        {
                            // Walk through the period, booking about two slots in three.
                            if (AvailabilityCalculator.Instant(day, period.Start, timeZone) is not { } start ||
                                AvailabilityCalculator.Instant(day, period.End, timeZone) is not { } end)
                            {
                                continue;
                            }
                            while (true)
                            {
                                var service = theirs[random.Next(theirs.Count)];
                                if (start + service.Duration > end)
                                {
                                    break;
                                }
                                if (start > now && random.Next(3) > 0)
                                {
                                    var client = Clients[random.Next(Clients.Length)];
                                    var booking = Booking.Book(
                                        business.Id, member.Id, service.Id, start, service.Duration, service.Buffer,
                                        Attendee.Create(null, client, Email(client)), now);
                                    // Made-up clients at example.com: nobody gets emails about them.
                                    booking.ClearEvents();
                                    var result = await bookings.AddAsync(booking, cancellation);
                                    telemetry.Booking(result, "sample");
                                    if (result == BookingResult.Booked)
                                    {
                                        booked++;
                                    }
                                    else
                                    {
                                        taken++;
                                    }
                                }
                                start += service.Duration + service.Buffer + TimeSpan.FromMinutes(15 * random.Next(3));
                            }
                        }
                    }
                }
            }

            activity?.SetTag("bookings.booked", booked);
            activity?.SetTag("bookings.slot_taken", taken);
            return Results.Ok(new Result(booked, taken));
        });


    // "Zofia Wiśniewska" -> zofia.wisniewska@example.com
    static string Email(string name)
    {
        var ascii = new string(name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return $"{ascii.ToLowerInvariant().Replace(' ', '.')}@example.com";
    }
}
