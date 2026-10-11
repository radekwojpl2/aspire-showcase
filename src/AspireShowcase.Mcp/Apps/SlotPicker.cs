using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Apps;

/// <summary>
/// The slot picker's page, as a ui:// resource of the MCP Apps extension. find_free_slots names
/// it in its _meta, and hosts that support MCP Apps show it with the tool's result; others,
/// such as Claude Code in a terminal, show the result as text.
/// </summary>
[McpServerResourceType]
sealed class SlotPicker
{
    public const string Uri = "ui://aspire-showcase/slot-picker";

    // The MIME type marks it as an MCP App rather than an ordinary HTML document.
    const string MimeType = "text/html;profile=mcp-app";

    static readonly Lazy<string> Html = new(() =>
    {
        using var stream = typeof(SlotPicker).Assembly.GetManifestResourceStream("AspireShowcase.Mcp.Apps.slot-picker.html")!;
        return new StreamReader(stream).ReadToEnd();
    });

    [McpServerResource(UriTemplate = Uri, Name = "slot_picker", Title = "Free times", MimeType = MimeType)]
    [Description("Free times of a service as buttons, for picking one to book.")]
    public static string Read() => Html.Value;
}
