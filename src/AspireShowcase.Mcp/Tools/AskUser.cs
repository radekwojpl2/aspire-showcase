using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspireShowcase.Mcp.Tools;

/// <summary>
/// Asks the user something in the middle of a tool call (MCP elicitation): the client shows
/// a form, such as Claude Code's dialog in the terminal, and the tool goes on with the answer.
/// It's the user who answers, not the model, so a tool that books or cancels can't be talked
/// into it by the model alone.
/// </summary>
/// <remarks>
/// Asked as a multi round-trip request: the tool returns an "input required" result, and the
/// client calls it again with the answer, so the tool reads the answer on its second run. That
/// works statelessly for a client on the 2026-07-28 protocol revision; for one on the
/// initialize handshake, the SDK sends the question over its session instead. A client that
/// can't show forms answers null; the tool then needs the values as arguments, and the
/// client's own approval of the tool call is the confirmation.
/// </remarks>
static class AskUser
{
    // The tool asks at most one question, so it's always under this key.
    const string Key = "ask";

    public static async Task<Answer?> AskAsync(
        RequestContext<CallToolRequestParams> context, string message,
        IDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> fields, CancellationToken cancellation)
    {
        var request = new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = new()
            {
                Properties = fields,
                Required = [.. fields.Keys],
            },
        };

        // The second run of a stateless call, with the answer.
        if (context.Params?.InputResponses?.TryGetValue(Key, out var response) == true)
        {
            return Answer.From(response.Deserialize(InputResponse.ElicitResultJsonTypeInfo));
        }

        var server = context.Server;
        if (CapabilitiesOf(context)?.Elicitation is null)
        {
            return null;
        }

        // The SDK also runs this over the session of a client on the initialize handshake.
        if (server.IsMrtrSupported)
        {
            throw new InputRequiredException(
                new Dictionary<string, InputRequest> { [Key] = InputRequest.ForElicitation(request) }, null);
        }

        return Answer.From(await server.ElicitAsync(request, cancellation));
    }

    /// <summary>
    /// What the client can do: declared with each request on the 2026-07-28 revision, in its
    /// _meta, which the SDK doesn't carry over to the server's ClientCapabilities; at the
    /// initialize handshake before that.
    /// </summary>
    static ClientCapabilities? CapabilitiesOf(RequestContext<CallToolRequestParams> context) =>
        context.Params?.Meta?[MetaKeys.ClientCapabilities] is { } declared
            ? declared.Deserialize<ClientCapabilities>(McpJsonUtilities.DefaultOptions)
            : context.Server.ClientCapabilities;

    public static ElicitRequestParams.StringSchema Text(string title, string? value) =>
        new() { Title = title, Default = value };

    public static ElicitRequestParams.BooleanSchema YesNo(string title) => new() { Title = title, Default = true };

    public static ElicitRequestParams.TitledSingleSelectEnumSchema Choice(
        string title, IEnumerable<(string Value, string Title)> options) =>
        new() { Title = title, OneOf = [.. options.Select(option => new ElicitRequestParams.EnumSchemaOption { Const = option.Value, Title = option.Title })] };
}

/// <summary>What the user answered: whether they went ahead, and the values they gave.</summary>
sealed record Answer(bool Accepted, IReadOnlyDictionary<string, JsonElement> Values)
{
    public static Answer From(ElicitResult? result) => new(
        result?.Action == "accept",
        result?.Content is { } content ? content.ToDictionary() : new Dictionary<string, JsonElement>());

    public string? Text(string key) =>
        Values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Accepted, and every yes/no question answered yes.</summary>
    public bool Confirmed => Accepted && Values.Values.All(value => value.ValueKind != JsonValueKind.False);
}
