using AspireShowcase.Scheduling.Application.Notices;
using AspireShowcase.Scheduling.PublicClient;
using MassTransit;

namespace AspireShowcase.Scheduling.Infrastructure;

/// <summary>
/// Publishes a <see cref="BookingNotice"/> for each booking event, for the notifications service;
/// <see cref="BuildBookingNotice"/> works out what it says. If that fails, MassTransit retries.
/// </summary>
sealed class BookingNoticeConsumer(BuildBookingNotice notices)
    : IConsumer<PublicClient.BookingConfirmed>, IConsumer<PublicClient.BookingCancelled>,
        IConsumer<PublicClient.BookingRescheduled>
{
    public async Task Consume(ConsumeContext<PublicClient.BookingConfirmed> context)
    {
        if (await notices.HandleAsync(context.Message.BookingId, "confirmed", null, context.CancellationToken) is { } notice)
        {
            await context.Publish(notice with { BookedBy = context.Message.BookedBy, OccurredAt = context.Message.OccurredAt });
        }
    }

    public async Task Consume(ConsumeContext<PublicClient.BookingCancelled> context)
    {
        var message = context.Message;
        if (await notices.HandleAsync(message.BookingId, "cancelled", message.CancelledBy, context.CancellationToken) is { } notice)
        {
            await context.Publish(notice with { OccurredAt = message.OccurredAt });
        }
    }

    // V1-4: the notice says where the booking was, as well as where it is.
    public async Task Consume(ConsumeContext<PublicClient.BookingRescheduled> context)
    {
        var message = context.Message;
        if (await notices.RescheduledAsync(
                message.BookingId, message.RescheduledBy, message.PreviousStart, context.CancellationToken) is { } notice)
        {
            await context.Publish(notice with { OccurredAt = message.OccurredAt });
        }
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
