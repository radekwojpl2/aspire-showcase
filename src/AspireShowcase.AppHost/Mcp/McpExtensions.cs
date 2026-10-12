static class McpExtensions
{
    /// <summary>
    /// The MCP server: lets AI agents (Claude, VS Code...) use the booking system through tools,
    /// at /mcp. It calls web like bff does, so it's the second thing web is reachable from.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddMcp(
        this IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> web)
    {
        var mcp = builder.AddProject<Projects.AspireShowcase_Mcp>("mcp")
            .WithHttpHealthCheck("/health")
            .WithExternalHttpEndpoints()
            .WithoutConnectionProperties()
            .WithReference(web)
            .WaitFor(web)
            // Link on the dashboard: the address to connect an MCP client to.
            .WithUrl("/mcp", "MCP endpoint")
            // Clients on the initialize handshake keep a session in memory, which another
            // replica wouldn't know: one replica, as there's no session affinity.
            .PublishAsAzureContainerApp((_, app) => app.Template.Scale.MaxReplicas = 1);

        // Locally, the MCP Inspector in the dashboard, to list the tools and call them by hand.
        if (builder.ExecutionContext.IsRunMode)
        {
            builder.AddMcpInspector("mcp-inspector")
                .WithMcpServer(mcp, path: "/mcp")
                .WaitFor(mcp);
        }

        return mcp;
    }
}
