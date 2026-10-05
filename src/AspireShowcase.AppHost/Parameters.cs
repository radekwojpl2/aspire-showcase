static class Parameters
{
    /// <summary>
    /// A parameter that's empty until set in user secrets (locally, Parameters:{name}) or by the
    /// Deploy workflow (Azure), for settings that only exist once something is set up elsewhere.
    /// </summary>
    public static IResourceBuilder<ParameterResource> AddOptionalParameter(
        this IDistributedApplicationBuilder builder, string name, string description, bool secret = false) =>
        builder.AddParameter(name, () => builder.Configuration[$"Parameters:{name}"] ?? "", secret: secret)
            .WithDescription(description);
}
