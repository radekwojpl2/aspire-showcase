using System.Globalization;
using AspireShowcase.BusinessSetup.PublicClient;
using AspireShowcase.Identity;
using AspireShowcase.Scheduling.PublicClient;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AspireShowcase.Scheduling;

/// <summary>
/// Turns a booking event into a <see cref="BookingNotice"/>: looks up the business, service and
/// staff names, the times in the business's time zone, and the owner's email (from Logto, through
/// Identity), and publishes it for the notifications service.
/// </summary>
/// <remarks>
/// Kept out of the booking request on purpose: booking stays quick and doesn't fail when Logto is
/// slow or down. If a lookup fails, MassTransit retries the message.
/// </remarks>
sealed class BookingNoticeConsumer(
    SchedulingDbContext db, IBusinessDirectory directory, IUserProfiles profiles)
    : IConsumer<PublicClient.BookingConfirmed>, IConsumer<PublicClient.BookingCancelled>,
        IConsumer<PublicClient.BookingRescheduled>
{
    public async Task Consume(ConsumeContext<PublicClient.BookingConfirmed> context)
    {
        if (await BuildAsync(context.Message.BookingId, "confirmed", null, context.CancellationToken) is { } notice)
        {
            await context.Publish(notice);
        }
    }

    public async Task Consume(ConsumeContext<PublicClient.BookingCancelled> context)
    {
        var message = context.Message;
        if (await BuildAsync(message.BookingId, "cancelled", message.CancelledBy, context.CancellationToken) is { } notice)
        {
            await context.Publish(notice);
        }
    }

    // V1-4: the notice says where the booking was, in the business's time zone, as well as where it is.
    public async Task Consume(ConsumeContext<PublicClient.BookingRescheduled> context)
    {
        var message = context.Message;
        if (await BuildAsync(message.BookingId, "rescheduled", null, context.CancellationToken) is { } notice)
        {
            var before = TimeZoneInfo.ConvertTime(message.PreviousStart, TimeZoneInfo.FindSystemTimeZoneById(notice.TimeZone));
            await context.Publish(notice with
            {
                RescheduledBy = message.RescheduledBy,
                PreviousDate = before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PreviousStart = before.ToString("HH:mm", CultureInfo.InvariantCulture),
            });
        }
    }

    // Null when there's nobody to tell any more, such as a booking of a business that's gone.
    async Task<BookingNotice?> BuildAsync(Guid id, string kind, string? cancelledBy, CancellationToken cancellation)
    {
        // Not tied to one request's business: messages come for every business.
        var bookingId = new BookingId(id);
        var booking = await db.Bookings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == bookingId, cancellation);
        if (booking is null || await directory.FindAsync(booking.BusinessId, cancellation) is not { } business)
        {
            return null;
        }

        var service = (await directory.ServicesAsync(business.Id, cancellation)).SingleOrDefault(s => s.Id == booking.ServiceId);
        var staffMember = (await directory.StaffAsync(business.Id, cancellation)).SingleOrDefault(s => s.Id == booking.StaffMemberId);
        var owner = await profiles.FindAsync(business.OwnerId, cancellation);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        var start = TimeZoneInfo.ConvertTime(booking.Start, timeZone);
        var end = TimeZoneInfo.ConvertTime(booking.End, timeZone);
        return new BookingNotice(
            kind,
            cancelledBy,
            booking.Id.Value,
            business.Name,
            business.Slug,
            service?.Name ?? "A removed service",
            staffMember?.Name ?? "A former staff member",
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            start.ToString("HH:mm", CultureInfo.InvariantCulture),
            end.ToString("HH:mm", CultureInfo.InvariantCulture),
            business.TimeZone,
            new BookingParty(booking.Attendee.Name, booking.Attendee.Email),
            new BookingParty(owner?.Name ?? business.Name, owner?.Email));
    }
}

/// <summary>
/// Consumes through the inbox and outbox in Scheduling's database: a redelivered event is
/// handled once, and the notice is published once.
/// </summary>
sealed class BookingNoticeConsumerDefinition : ConsumerDefinition<BookingNoticeConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<BookingNoticeConsumer> consumerConfigurator,
        IRegistrationContext context) =>
        endpointConfigurator.UseEntityFrameworkOutbox<SchedulingDbContext>(context);
}
