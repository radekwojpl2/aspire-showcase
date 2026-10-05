static class DevelopmentExtensions
{
    /// <summary>
    /// Local runs only: a command on the "web" resource in the dashboard that fills the next 7
    /// days of every business with sample bookings, so the owner's calendar has something to show
    /// before clients can book. It posts to an endpoint the API maps in Development only.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithSampleDataCommands(this IResourceBuilder<ProjectResource> web)
    {
        if (!web.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return web;
        }

        return web.WithHttpCommand("/api/dev/sample-bookings", "Add sample bookings", commandName: "sample-bookings",
            commandOptions: new()
            {
                Description = "Books the next 7 days of every business, within its staff's hours. Run it again to add more; overlaps are refused.",
                IconName = "CalendarAdd",
            });
    }
}
