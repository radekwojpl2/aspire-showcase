using System.Net;
using ModelContextProtocol;

namespace AspireShowcase.Mcp;

/// <summary>
/// The API's endpoints the tools use. The MCP server holds no rules of its own: what a tool
/// may do, and why it can't, is web's answer.
/// </summary>
sealed class WebApi(HttpClient http)
{
    public Task<PublicBusiness> GetBusinessAsync(string slug, CancellationToken cancellation) =>
        GetAsync<PublicBusiness>($"/api/public/businesses/{Uri.EscapeDataString(slug)}", cancellation);

    public Task<PublicSlots> GetFreeSlotsAsync(
        string slug, Guid serviceId, Guid? staffMemberId, CancellationToken cancellation) =>
        GetAsync<PublicSlots>(
            $"/api/public/businesses/{Uri.EscapeDataString(slug)}/slots?serviceId={serviceId}" +
            (staffMemberId is { } staff ? $"&staffMemberId={staff}" : ""),
            cancellation);

    async Task<T> GetAsync<T>(string path, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(path, cancellation);
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
            throw new McpException(problem?.Title ?? (response.StatusCode == HttpStatusCode.NotFound
                ? "Not found."
                : $"The request was refused ({(int)response.StatusCode})."));
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

    sealed record Problem(string? Title);
}
