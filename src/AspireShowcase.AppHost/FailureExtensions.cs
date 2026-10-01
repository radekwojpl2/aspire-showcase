static class FailureExtensions
{
    /// <summary>
    /// Local runs only: commands on the "web" resource in the dashboard that make the to-do
    /// endpoints fail or slow down, to see how failures look in traces, logs and metrics.
    /// Each one posts to an endpoint the API maps in Development only.
    /// </summary>
    /// <remarks>
    /// A third failure needs no command: stop the "cache" resource from the dashboard, and
    /// the API carries on with the database alone.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithFailureCommands(this IResourceBuilder<ProjectResource> web)
    {
        if (!web.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return web;
        }

        return web
            .WithHttpCommand("/api/failures/errors", "Fail to-do requests", commandName: "fail-todos", commandOptions: new()
            {
                Description = "Every to-do request throws and answers 500, until failures are stopped.",
                IconName = "Bug",
            })
            .WithHttpCommand("/api/failures/slow-queries", "Slow down to-do queries", commandName: "slow-todos", commandOptions: new()
            {
                Description = "Every to-do request first runs a 2-second database query, until failures are stopped.",
                IconName = "Timer",
            })
            .WithHttpCommand("/api/failures/reset", "Stop simulated failures", commandName: "stop-failures", commandOptions: new()
            {
                Description = "Turns off the failing and slow to-do requests.",
                IconName = "ArrowReset",
            });
    }
}
