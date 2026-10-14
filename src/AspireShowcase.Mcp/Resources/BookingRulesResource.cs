using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Resources;

/// <summary>
/// How booking works, as one document: the rules the tools' answers and refusals come from,
/// such as the cancellation notice, time zones and time off. A resource rather than a tool, as
/// it's fixed text the user attaches when they want the model to know it (in Claude Code,
/// @aspire-showcase:guide://booking-rules), not data the model looks up.
/// </summary>
[McpServerResourceType]
sealed class BookingRulesResource
{
    public const string Uri = "guide://booking-rules";

    static readonly Lazy<string> Text = new(() =>
    {
        using var stream = typeof(BookingRulesResource).Assembly.GetManifestResourceStream("AspireShowcase.Mcp.Resources.booking-rules.md")!;
        return new StreamReader(stream).ReadToEnd();
    });

    [McpServerResource(UriTemplate = Uri, Name = "booking_rules", Title = "How booking works", MimeType = "text/markdown")]
    [Description("The rules of the booking system: free times, time zones, the cancellation notice, what an owner " +
        "may do, and time off. Read it to explain an answer or a refusal to the user.")]
    public static string Read() => Text.Value;
}
