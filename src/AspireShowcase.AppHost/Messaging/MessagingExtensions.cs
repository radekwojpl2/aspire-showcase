static class MessagingExtensions
{
    /// <summary>
    /// RabbitMQ, the message bus MassTransit runs on: a container locally, with its management UI
    /// on the dashboard, and a Container App in Azure, reachable only inside the environment.
    /// </summary>
    /// <remarks>
    /// It keeps nothing on disk, so a restart loses messages still waiting in its queues. The
    /// outbox only guards publishing (a message reaches RabbitMQ, or stays in the outbox); a
    /// volume, or a managed broker, would be the next step for a real product.
    /// </remarks>
    public static IResourceBuilder<RabbitMQServerResource> AddMessaging(this IDistributedApplicationBuilder builder)
    {
        var messaging = builder.AddRabbitMQ("messaging");
        if (builder.ExecutionContext.IsRunMode)
        {
            messaging.WithManagementPlugin();
        }
        return messaging;
    }

    /// <summary>Gives a service the bus's connection string (ConnectionStrings__messaging).</summary>
    public static IResourceBuilder<ProjectResource> WithMessaging(
        this IResourceBuilder<ProjectResource> project, IResourceBuilder<RabbitMQServerResource> messaging) =>
        project.WithReference(messaging).WaitFor(messaging);
}
