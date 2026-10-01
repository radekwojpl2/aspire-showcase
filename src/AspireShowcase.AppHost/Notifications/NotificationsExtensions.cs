static class NotificationsExtensions
{
    /// <summary>
    /// The notifications service: a second ASP.NET Core project, called by the API only.
    /// It has no external endpoint, so in Azure it's reachable only inside the Container
    /// Apps environment.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddNotifications(this IDistributedApplicationBuilder builder) =>
        builder.AddProject<Projects.AspireShowcase_Notifications>("notifications")
            .WithHttpHealthCheck("/health");

    /// <summary>
    /// Lets the API call the service as http://notifications: the reference passes its
    /// address in the form service discovery reads.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithNotifications(
        this IResourceBuilder<ProjectResource> web, IResourceBuilder<ProjectResource> notifications) =>
        web.WithReference(notifications).WaitFor(notifications);
}
