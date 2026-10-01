using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

/// <summary>
/// The to-do list in Redis. The cache is an optimization, so a Redis failure is never an
/// error for the caller: reads report "not cached" and the API uses the database alone.
/// </summary>
sealed class TodoListCache(IDistributedCache cache, TodoTelemetry telemetry, ILogger<TodoListCache> logger)
{
    // The whole list is cached under one key, and every change to an item removes it.
    const string Key = "todos";

    // A limit on how long a list can be served if a removal is ever missed, which is what
    // happens when Redis can't be reached at the moment of a change.
    static readonly DistributedCacheEntryOptions Options = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    // Each call to an unreachable Redis waits for its timeout. After a failure the cache is
    // left alone for a while, so only one request in that time pays for the wait.
    static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(15);
    long _skipUntil;

    public async Task<List<Todo>?> GetAsync() =>
        await TryAsync(async () => await cache.GetAsync(Key) is { } cached
            ? JsonSerializer.Deserialize<List<Todo>>(cached)
            : null);

    public Task SetAsync(List<Todo> list) =>
        TryAsync<object?>(async () =>
        {
            await cache.SetAsync(Key, JsonSerializer.SerializeToUtf8Bytes(list), Options);
            return null;
        });

    public Task RemoveAsync() =>
        TryAsync<object?>(async () =>
        {
            await cache.RemoveAsync(Key);
            telemetry.CacheInvalidated(Activity.Current);
            return null;
        });

    async Task<T?> TryAsync<T>(Func<Task<T?>> useCache)
    {
        if (Stopwatch.GetTimestamp() < Volatile.Read(ref _skipUntil))
        {
            telemetry.CacheUnavailable(Activity.Current, skipped: true);
            return default;
        }

        try
        {
            return await useCache();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Volatile.Write(ref _skipUntil, Stopwatch.GetTimestamp() + (long)(RetryAfter.TotalSeconds * Stopwatch.Frequency));
            telemetry.CacheUnavailable(Activity.Current, skipped: false);
            logger.LogWarning(exception,
                "The to-do cache is unavailable; using the database only for the next {Seconds} seconds",
                RetryAfter.TotalSeconds);
            return default;
        }
    }
}
