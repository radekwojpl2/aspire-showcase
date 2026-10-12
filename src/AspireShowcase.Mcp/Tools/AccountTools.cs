using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// Who the user is signed in as. The MCP server's token carries only their user ID, so web looks
/// up the rest: their name and email, and the business they own, if any.
/// </summary>
[McpServerToolType]
sealed class AccountTools(WebApi web)
{
    [McpServerTool(Name = "whoami", Title = "Who am I", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Who the user is signed in as: their name, their email, and the business they own, with its " +
        "booking link, or null for a client.")]
    public Task<Me> WhoAmI(CancellationToken cancellation) => web.GetMeAsync(cancellation);
}
