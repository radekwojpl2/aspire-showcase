using Aspire.Hosting.JavaScript;

static class EmailExtensions
{
    /// <summary>
    /// Lets the notifications service send booking emails through Resend: the API key, the sender
    /// on a domain verified in Resend, and the app's address for links in the emails. The key and
    /// sender are empty until set; then emails are skipped and logged.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithEmail(
        this IResourceBuilder<ProjectResource> notifications, IResourceBuilder<ProjectResource> bff,
        IResourceBuilder<ViteAppResource> frontend)
    {
        var builder = notifications.ApplicationBuilder;
        var apiKey = builder.AddOptionalParameter("resend-api-key", "API key of the Resend account emails are sent with.", secret: true);
        var sender = builder.AddOptionalParameter(
            "email-sender", "Sender of the emails, on a domain verified in Resend, e.g. Bookings <bookings@yourdomain.com>.");

        // Where clients open the app: Vite locally, bff (which serves the React app) in Azure.
        var appUrl = builder.ExecutionContext.IsRunMode ? frontend.GetEndpoint("http") : bff.GetEndpoint("https");

        return notifications
            .WithOptionalEnvironment("Email__ResendApiKey", apiKey)
            .WithOptionalEnvironment("Email__From", sender)
            .WithEnvironment("Email__AppUrl", appUrl);
    }
}
