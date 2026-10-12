using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// The slot picker, an MCP App: hosts that support the MCP Apps extension show find_free_slots's
/// result as a page of free times rather than as text.
/// </summary>
public sealed class SlotPickerTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    const string Uri = "ui://aspire-showcase/slot-picker";

    [Fact]
    public async Task Finding_free_times_names_the_slot_picker_as_its_page()
    {
        await using var client = await mcp.ConnectAsync();

        var tool = Assert.Single(await client.ListToolsAsync(), tool => tool.Name == "find_free_slots");

        Assert.Equal(Uri, tool.ProtocolTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
    }

    [Fact]
    public async Task The_slot_picker_is_an_html_page_marked_as_an_mcp_app()
    {
        await using var client = await mcp.ConnectAsync();

        var resource = await client.ReadResourceAsync(Uri);

        var page = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));
        Assert.Equal("text/html;profile=mcp-app", page.MimeType);
        Assert.Contains("ui/initialize", page.Text);
    }
}
