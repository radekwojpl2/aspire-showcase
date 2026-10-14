using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.Public;

/// <summary>
/// The list of businesses, to find one by name instead of knowing its booking link. web needs
/// no user for it, but the MCP server only lets in signed-in users.
/// </summary>
[McpServerToolType]
sealed class ListBusinessesTool(WebApi web)
{
    [McpServerTool(Name = "list_businesses", Title = "Find businesses", ReadOnly = true, OpenWorld = false)]
    [Description("The businesses on the platform, by name, with their booking link, address and description. " +
        "Use get_business with a booking link for its services, prices and staff.")]
    public Task<IReadOnlyList<BusinessListing>> ListBusinesses(
        [Description("Text the business's name or description has in it, such as \"hair\"; leave out for every business.")]
        string? query = null,
        CancellationToken cancellation = default) =>
        web.ListBusinessesAsync(query, cancellation);
}
