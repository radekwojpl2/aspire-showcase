using System.Net;
using ModelContextProtocol;

namespace AspireShowcase.Mcp;

/// <summary>
/// The API's endpoints the tools use. The MCP server holds no rules of its own: what a tool
/// may do, and why it can't, is web's answer.
/// </summary>
sealed class WebApi(HttpClient http)
{
    public Task<IReadOnlyList<BusinessListing>> ListBusinessesAsync(string? query, CancellationToken cancellation) =>
        GetAsync<IReadOnlyList<BusinessListing>>(
            "/api/public/businesses" + (string.IsNullOrWhiteSpace(query) ? "" : $"?query={Uri.EscapeDataString(query)}"),
            cancellation);

    public Task<PublicBusiness> GetBusinessAsync(string slug, CancellationToken cancellation) =>
        GetAsync<PublicBusiness>($"/api/public/businesses/{Uri.EscapeDataString(slug)}", cancellation);

    public Task<PublicSlots> GetFreeSlotsAsync(
        string slug, Guid serviceId, Guid? staffMemberId, CancellationToken cancellation) =>
        GetAsync<PublicSlots>(
            $"/api/public/businesses/{Uri.EscapeDataString(slug)}/slots?serviceId={serviceId}" +
            (staffMemberId is { } staff ? $"&staffMemberId={staff}" : ""),
            cancellation);

    public Task<Me> GetMeAsync(CancellationToken cancellation) => GetAsync<Me>("/api/me", cancellation);

    // A client's own bookings (MVP-4, MVP-7, V1-4); web refuses them without a signed-in user.

    public async Task<BookingConfirmation> BookAsync(string slug, BookSlot request, CancellationToken cancellation)
    {
        using var response = await http.PostAsJsonAsync(
            $"/api/public/businesses/{Uri.EscapeDataString(slug)}/bookings", request, cancellation);
        return await ReadAsync<BookingConfirmation>(response, cancellation);
    }

    public Task<IReadOnlyList<ClientBooking>> GetMyBookingsAsync(CancellationToken cancellation) =>
        GetAsync<IReadOnlyList<ClientBooking>>("/api/me/bookings", cancellation);

    public async Task CancelMyBookingAsync(Guid id, CancellationToken cancellation)
    {
        using var response = await http.PostAsync($"/api/me/bookings/{id}/cancel", null, cancellation);
        await EnsureSuccessAsync(response, cancellation);
    }

    public Task<PublicSlots> GetMyBookingSlotsAsync(Guid id, Guid? staffMemberId, CancellationToken cancellation) =>
        GetAsync<PublicSlots>(
            $"/api/me/bookings/{id}/slots" + (staffMemberId is { } staff ? $"?staffMemberId={staff}" : ""), cancellation);

    public async Task<BookingConfirmation> RescheduleMyBookingAsync(
        Guid id, DateTimeOffset startsAt, Guid? staffMemberId, CancellationToken cancellation)
    {
        using var response = await http.PostAsJsonAsync(
            $"/api/me/bookings/{id}/reschedule", new { startsAt, staffMemberId }, cancellation);
        return await ReadAsync<BookingConfirmation>(response, cancellation);
    }

    // The owner's calendar (MVP-12, V1-1); web refuses it to anyone but a business's owner.

    public Task<CalendarResponse> GetCalendarAsync(
        string view, string? date, Guid? staffMemberId, CancellationToken cancellation) =>
        GetAsync<CalendarResponse>(
            $"/api/businesses/mine/bookings?view={Uri.EscapeDataString(view)}" +
            (date is null ? "" : $"&date={Uri.EscapeDataString(date)}") +
            (staffMemberId is { } staff ? $"&staffMemberId={staff}" : ""),
            cancellation);

    async Task<T> GetAsync<T>(string path, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(path, cancellation);
        return await ReadAsync<T>(response, cancellation);
    }

    static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellation)
    {
        await EnsureSuccessAsync(response, cancellation);
        return (await response.Content.ReadFromJsonAsync<T>(cancellation))!;
    }

    /// <summary>
    /// Turns web's refusal into a tool error the model reads: the problem's title is written
    /// for people ("This time was just taken."), so it's passed on as it is.
    /// </summary>
    static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode is not HttpStatusCode.Unauthorized)
        {
            var problem = await ReadProblemAsync(response, cancellation);
            throw new McpException(problem?.Message ?? (response.StatusCode switch
            {
                HttpStatusCode.NotFound => "Not found.",
                // The owner role is checked by web, on the token exchanged for the user's.
                HttpStatusCode.Forbidden => "Only the owner of a business can do that, and the user isn't one.",
                _ => $"The request was refused ({(int)response.StatusCode}).",
            }));
        }

        response.EnsureSuccessStatusCode();
    }

    static async Task<Problem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>(cancellation);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <param name="Errors">What was wrong with each field, for a validation problem.</param>
    sealed record Problem(string? Title, Dictionary<string, string[]>? Errors)
    {
        public string? Message => Errors is { Count: > 0 }
            ? string.Join(" ", Errors.Select(error => $"{error.Key}: {string.Join(" ", error.Value)}"))
            : Title;
    }
}
