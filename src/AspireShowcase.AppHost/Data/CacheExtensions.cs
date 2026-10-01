static class CacheExtensions
{
    /// <summary>
    /// Redis for the API's cache of the to-do list, as a container both locally and in Azure
    /// (a Container App reachable only inside the environment). It keeps nothing on disk:
    /// a restart empties the cache and the API fills it again from the database.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithCache(this IResourceBuilder<ProjectResource> web)
    {
        var cache = web.ApplicationBuilder.AddRedis("cache");

        return web.WithReference(cache).WaitFor(cache);
    }
}
