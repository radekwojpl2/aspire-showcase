using System.Net;
using System.Net.Http.Headers;

/// <summary>Email settings, from the AppHost.</summary>
sealed class EmailSettings
{
    /// <summary>The sender, such as "Studio Anna &lt;bookings@yourdomain.com&gt;", on a domain verified in Resend.</summary>
    public string? From { get; init; }

    public string? ResendApiKey { get; init; }

    /// <summary>The app's public address, for links in emails.</summary>
    public string? AppUrl { get; init; }

    public bool IsConfigured => !string.IsNullOrEmpty(From) && !string.IsNullOrEmpty(ResendApiKey);
}

/// <summary>One email to send.</summary>
sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>
/// Resend refused an email for good, such as for an address it won't send to: asking again
/// wouldn't change the answer. <see cref="Reason"/> is Resend's explanation.
/// </summary>
sealed class EmailRefusedException(string reason) : Exception($"Resend refused the email: {reason}")
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Sends email through Resend's API (https://resend.com/docs/api-reference/emails/send-email).
/// </summary>
/// <remarks>
/// Every email has an idempotency key, so Resend sends it once even if this service asks twice,
/// say when it crashed between sending and recording it. The resilience defaults from
/// ServiceDefaults retry failures and Resend's rate limit (429).
/// </remarks>
sealed class ResendEmailSender(HttpClient http, EmailSettings settings)
{
    /// <exception cref="EmailRefusedException">Resend refused this email for good (400 or 422).</exception>
    /// <exception cref="HttpRequestException">Resend couldn't take it now, or couldn't be reached; trying
    /// again later may work.</exception>
    public async Task SendAsync(EmailMessage email, string idempotencyKey, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from = settings.From,
                to = new[] { email.To },
                subject = email.Subject,
                text = email.Text,
                html = email.Html,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await http.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            // Resend explains what's wrong in the body, such as a sender domain that isn't verified.
            var reason = await response.Content.ReadAsStringAsync(cancellation);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                throw new EmailRefusedException(reason);
            }
            throw new HttpRequestException(
                $"Resend answered {(int)response.StatusCode}: {reason}", null, response.StatusCode);
        }
    }
}
