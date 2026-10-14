using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools.TimeOff;

/// <summary>Removing time off (user story V1-1), confirmed by the owner through <see cref="AskUser"/>.</summary>
[McpServerToolType]
sealed class RemoveTimeOffTool(WebApi web)
{
    [McpServerTool(Name = "remove_time_off", Title = "Remove time off", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("For the owner of a business: removes one of its time off, once the owner confirms it; the time " +
        "can be booked again.")]
    public async Task<ChangeResult<TimeOffResponse>> RemoveTimeOff(
        RequestContext<CallToolRequestParams> context,
        [Description("The time off's ID, from list_time_off.")] Guid timeOffId,
        CancellationToken cancellation = default)
    {
        var timeOff = (await web.GetTimeOffAsync(cancellation)).TimeOff.FirstOrDefault(timeOff => timeOff.Id == timeOffId)
            ?? throw new McpException($"The business has no time off {timeOffId} that hasn't ended; list_time_off lists them.");

        var answer = await AskUser.AskAsync(context,
            $"Remove the time off of {timeOff.StaffName ?? "the whole business"} from {timeOff.From.Replace('T', ' ')} " +
            $"to {timeOff.To.Replace('T', ' ')}{(timeOff.Note is null ? "" : $" ({timeOff.Note})")}? Clients can book that time again.",
            new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["confirm"] = AskUser.YesNo("Remove it") },
            cancellation);
        if (answer is { Confirmed: false })
        {
            return ChangeResult<TimeOffResponse>.Declined("The owner kept the time off.");
        }

        await web.RemoveTimeOffAsync(timeOffId, cancellation);
        return ChangeResult<TimeOffResponse>.Made(timeOff);
    }
}
