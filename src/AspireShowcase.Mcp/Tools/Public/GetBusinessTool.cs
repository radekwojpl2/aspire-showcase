using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Public;

/// <summary>
/// What a business's public booking page shows (user stories MVP-1 and MVP-2): the business with its
/// services and staff.
/// </summary>
[McpServerToolType]
sealed class GetBusinessTool(WebApi web)
{
    [McpServerTool(Name = "get_business", Title = "Get a business", ReadOnly = true, OpenWorld = false)]
    [Description("A business's public booking page: its name, address, time zone, cancellation notice, " +
        "and its services with their duration, price and the staff who offer them. The IDs it returns are " +
        "what find_free_slots takes.")]
    public Task<PublicBusiness> GetBusiness(
        [Description("The business's booking link, the last part of its URL, such as \"anna-hair\".")] string slug,
        CancellationToken cancellation) =>
        web.GetBusinessAsync(slug, cancellation);
}
