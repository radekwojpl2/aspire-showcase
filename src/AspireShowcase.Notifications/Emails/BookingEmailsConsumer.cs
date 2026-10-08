using AspireShowcase.Scheduling.PublicClient;
using MassTransit;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// notifications-db: MassTransit's inbox, which records the messages this service has handled,
/// so a redelivered one doesn't send its emails again. It survives restarts.
/// </summary>
sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

/// <summary>
/// Sends the emails about a booking (user stories MVP-5, MVP-7, MVP-13 and MVP-14) when Scheduling
/// publishes a <see cref="BookingNotice"/>.
/// </summary>
/// <remarks>
/// Each email is sent once: the inbox drops a redelivered message, and Resend drops a repeated
/// request by its idempotency key (the message ID and the recipient), in case this service fails
/// between sending and recording. A failure to send throws, so MassTransit retries the message;
/// an email Resend refuses for good, such as to an address it won't send to, is logged and
/// counted instead, and the booking's other emails still go out.
/// </remarks>
sealed class BookingEmailsConsumer(
    ResendEmailSender sender, EmailSettings settings, NotificationStore store, PendingDigest pending,
    NotificationTelemetry telemetry, ILogger<BookingEmailsConsumer> logger) : IConsumer<BookingNotice>
{
    public async Task Consume(ConsumeContext<BookingNotice> context)
    {
        using var activity = telemetry.StartActivity("notifications.emails");

        foreach (var email in BookingEmails.For(context.Message, settings.AppUrl, settings.From))
        {
            if (!settings.IsConfigured)
            {
                // Locally without a Resend key: nothing to send with.
                logger.LogWarning("Email isn't set up, so the {Kind} email to the {Recipient} wasn't sent", email.Kind, email.Recipient);
                telemetry.Email(activity, email.Kind, "skipped");
                continue;
            }

            try
            {
                await sender.SendAsync(email.Message, $"{context.MessageId}-{email.Recipient}", context.CancellationToken);
            }
            catch (EmailRefusedException refused)
            {
                telemetry.Email(activity, email.Kind, "refused");
                logger.LogWarning("Resend refused the {Kind} email to the {Recipient} for booking {BookingId}: {Reason}",
                    email.Kind, email.Recipient, context.Message.BookingId, refused.Reason);
                continue;
            }
            telemetry.Email(activity, email.Kind, "sent");
            logger.LogInformation("Sent the {Kind} email to the {Recipient} for booking {BookingId}",
                email.Kind, email.Recipient, context.Message.BookingId);

            // Also into the latest notifications and the digest, without the personal data.
            var notification = store.Add($"email.{email.Kind}", $"Email to the {email.Recipient}: {email.Kind}", out _);
            pending.Add(notification.Kind, activity);
        }
    }
}

/// <summary>Consumes through the inbox and outbox in notifications-db.</summary>
sealed class BookingEmailsConsumerDefinition : ConsumerDefinition<BookingEmailsConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<BookingEmailsConsumer> consumerConfigurator,
        IRegistrationContext context) =>
        endpointConfigurator.UseEntityFrameworkOutbox<NotificationsDbContext>(context);
}
