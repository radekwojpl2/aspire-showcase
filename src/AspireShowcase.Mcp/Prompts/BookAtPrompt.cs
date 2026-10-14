using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Prompts;

/// <summary>
/// A ready-made request the user starts themselves, such as a slash command in Claude Code: it
/// walks the model through the tools in the right order. It only writes the request; the tools
/// still ask the user before anything changes.
/// </summary>
[McpServerPromptType]
sealed class BookAtPrompt
{
    [McpServerPrompt(Name = "book_at", Title = "Book at a business")]
    [Description("Book a service at a business, from its services and free times.")]
    public static string BookAt(
        [Description("The business's booking link, such as \"anna-hair\", or words from its name.")] string business,
        [Description("The service to book, such as \"haircut\"; leave out to choose from the list.")] string? service = null) =>
        $"""
        I want to book {(service is null ? "a service" : $"a {service}")} at "{business}".

        1. If "{business}" isn't a booking link, find the business with list_businesses and use its booking link.
        2. Show me its services, with their duration and price, and who does them, with get_business.
        3. {(service is null ? "Ask me which service I want." : $"Pick the service that matches \"{service}\", or ask me if none or several do.")}
        4. Show me its free times for the next seven days with find_free_slots, grouped by day.
        5. Ask me which time I want, then book it with book_slot.
        """;
}
