using System.Diagnostics;
using OpenTelemetry;

namespace AspireShowcase.Mcp;

/// <summary>
/// Names each POST /mcp request's span after the MCP operation it carried, such as
/// "POST /mcp tools/call book_slot", so a list of traces shows which tool ran without opening
/// them.
/// </summary>
/// <remarks>
/// The operation's span, from the MCP SDK, ends first: it's noted on the request's span, which is
/// renamed as it ends. Not before: ASP.NET Core's instrumentation names it from its route then.
/// </remarks>
sealed class McpSpanNames : BaseProcessor<Activity>
{
    const string Operation = "mcp.operation";

    public override void OnEnd(Activity activity)
    {
        if (activity.Source.Name == "Experimental.ModelContextProtocol"
            && activity.Parent is { Source.Name: "Microsoft.AspNetCore" } request)
        {
            request.SetCustomProperty(Operation, activity.DisplayName);
        }
        else if (activity.GetCustomProperty(Operation) is string operation)
        {
            activity.DisplayName = $"{activity.DisplayName.TrimEnd('/')} {operation}";
        }
    }
}
