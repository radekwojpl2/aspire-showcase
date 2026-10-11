using System.Diagnostics;
using System.Diagnostics.Metrics;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp;

/// <summary>
/// Custom telemetry for the MCP server, next to the MCP SDK's own span per operation: how tool
/// calls end, how users answer the confirmations, and how often a token for web is exchanged.
/// </summary>
/// <remarks>
/// The source and the meter are named after the application, which is the name the
/// ServiceDefaults project subscribes to. Arguments and answers are left out: they hold user input.
/// </remarks>
sealed class McpTelemetry
{
    const string Name = "AspireShowcase.Mcp";

    static readonly ActivitySource Source = new(Name);

    readonly Counter<long> _toolCalls;
    readonly Counter<long> _confirmations;
    readonly Counter<long> _tokenExchanges;

    public McpTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _toolCalls = meter.CreateCounter<long>(
            "mcp.tool.calls", "{call}", "Tool calls, by tool and outcome (done, error, asked, failed).");
        _confirmations = meter.CreateCounter<long>(
            "mcp.confirmations", "{answer}", "Answers to the forms that confirm a change, by tool and answer.");
        _tokenExchanges = meter.CreateCounter<long>(
            "mcp.token.exchanges", "{token}", "Tokens for web, by result (cached, exchanged, refused).");
    }

    /// <summary>
    /// Counts every tool call by how it ended: done; error, when the tool answered with an error the
    /// model reads, such as a refusal of web's; asked, when it's waiting for the user's answer to a
    /// stateless question; failed, when it threw.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> CountToolCalls(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (request, cancellation) =>
    {
        var telemetry = request.Services!.GetRequiredService<McpTelemetry>();
        var tool = request.Params?.Name ?? "unknown";
        try
        {
            var result = await next(request, cancellation);
            telemetry.ToolCalled(tool, result.IsError == true ? "error" : "done");
            return result;
        }
        catch (InputRequiredException)
        {
            telemetry.ToolCalled(tool, "asked");
            throw;
        }
        catch (McpException)
        {
            // The SDK turns it into the error result the model reads, after this filter.
            telemetry.ToolCalled(tool, "error");
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            telemetry.ToolCalled(tool, "failed");
            throw;
        }
    };

    void ToolCalled(string tool, string outcome)
    {
        _toolCalls.Add(1, new("tool", tool), new("outcome", outcome));
        // On the SDK's span for the call, so a trace says how it ended.
        Activity.Current?.SetTag("mcp.tool.outcome", outcome);
    }

    /// <param name="fields">What the form asks for, such as startsAt or confirm; not the values.</param>
    public static void Asked(IEnumerable<string> fields) =>
        Activity.Current?.AddEvent(new ActivityEvent("mcp.user.asked",
            tags: new ActivityTagsCollection { ["fields"] = string.Join(",", fields) }));

    /// <param name="answer">accept, decline or cancel, as the client sent it; unsupported when it can't show forms.</param>
    public void Answered(string tool, string answer)
    {
        _confirmations.Add(1, new("tool", tool), new("answer", answer));
        Activity.Current?.AddEvent(new ActivityEvent("mcp.user.answered",
            tags: new ActivityTagsCollection { ["answer"] = answer }));
    }

    /// <summary>Starts a span for exchanging the user's token at Logto, around its HTTP call.</summary>
    public Activity? StartTokenExchange() => Source.StartActivity("mcp.token.exchange");

    /// <param name="result">cached, exchanged, or refused by Logto.</param>
    public void TokenExchanged(Activity? activity, string result)
    {
        _tokenExchanges.Add(1, new KeyValuePair<string, object?>("result", result));
        activity?.SetTag("mcp.token.result", result);
    }
}
