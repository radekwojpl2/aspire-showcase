static class Parameters
{
    /// <summary>
    /// A parameter that's empty until set in user secrets (locally, Parameters:{name}) or by the
    /// Deploy workflow (Azure), for settings that only exist once something is set up elsewhere.
    /// Pass it on with <see cref="WithOptionalEnvironment"/>.
    /// </summary>
    public static IResourceBuilder<ParameterResource> AddOptionalParameter(
        this IDistributedApplicationBuilder builder, string name, string description, bool secret = false) =>
        builder.AddParameter(name, () => builder.Configuration[$"Parameters:{name}"] ?? "", secret: secret)
            .WithDescription(description);

    /// <summary>
    /// Sets an environment variable from an optional parameter, except in Azure while the
    /// parameter is empty: a secret one becomes a Container App secret, and Container Apps refuse
    /// a secret without a value. The apps read a missing setting the same as an empty one.
    /// </summary>
    public static IResourceBuilder<T> WithOptionalEnvironment<T>(
        this IResourceBuilder<T> resource, string name, IResourceBuilder<ParameterResource> parameter)
        where T : IResourceWithEnvironment
    {
        var builder = resource.ApplicationBuilder;
        if (builder.ExecutionContext.IsPublishMode &&
            string.IsNullOrEmpty(builder.Configuration[$"Parameters:{parameter.Resource.Name}"]))
        {
            return resource;
        }
        return resource.WithEnvironment(name, parameter);
    }
}
