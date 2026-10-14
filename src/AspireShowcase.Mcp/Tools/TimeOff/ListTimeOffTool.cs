using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.TimeOff;

/// <summary>
/// An owner's time off (user story V1-1): when the business or a staff member is away, so no
/// one can book them then.
/// </summary>
[McpServerToolType]
sealed class ListTimeOffTool(WebApi web)
{
    [McpServerTool(Name = "list_time_off", Title = "My business's time off", ReadOnly = true, OpenWorld = false)]
    [Description("For the owner of a business: its time off that hasn't ended yet, for the whole business or one " +
        "staff member, in the business's time zone, with the bookings still inside each.")]
    public Task<TimeOffList> ListTimeOff(CancellationToken cancellation) => web.GetTimeOffAsync(cancellation);
}
