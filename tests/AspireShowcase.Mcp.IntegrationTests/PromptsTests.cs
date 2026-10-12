using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>The ready-made requests the user starts themselves, such as slash commands in Claude Code.</summary>
public sealed class PromptsTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    [Fact]
    public async Task The_server_offers_its_prompts_with_their_arguments()
    {
        await using var client = await mcp.ConnectAsync();

        var prompts = await client.ListPromptsAsync();

        Assert.Equal(["book_at", "my_upcoming_bookings", "owner_week"], prompts.Select(prompt => prompt.Name).Order());
        var bookAt = prompts.Single(prompt => prompt.Name == "book_at").ProtocolPrompt.Arguments!;
        Assert.True(bookAt.Single(argument => argument.Name == "business").Required);
        Assert.NotEqual(true, bookAt.Single(argument => argument.Name == "service").Required);
    }

    [Fact]
    public async Task Booking_at_a_business_asks_for_its_services_and_free_times_and_then_to_book()
    {
        await using var client = await mcp.ConnectAsync();

        var prompt = await client.GetPromptAsync("book_at",
            new Dictionary<string, object?> { ["business"] = "anna-hair", ["service"] = "haircut" });

        var text = Text(prompt);
        Assert.Contains("a haircut at \"anna-hair\"", text);
        Assert.Contains("get_business", text);
        Assert.Contains("find_free_slots", text);
        Assert.Contains("book_slot", text);
    }

    [Fact]
    public async Task The_week_of_the_business_is_the_one_with_the_given_day_in_it()
    {
        await using var client = await mcp.ConnectAsync();

        var prompt = await client.GetPromptAsync("owner_week", new Dictionary<string, object?> { ["week"] = "2026-11-09" });

        var text = Text(prompt);
        Assert.Contains("view \"week\" and date 2026-11-09", text);
        Assert.Contains("list_time_off", text);
    }

    static string Text(GetPromptResult prompt)
    {
        var message = Assert.Single(prompt.Messages);
        Assert.Equal(Role.User, message.Role);
        return Assert.IsType<TextContentBlock>(message.Content).Text;
    }
}
