using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// Custom telemetry for the to-do list, next to what the integrations record on their own:
/// a span per operation with events for what happened inside it, and one metric of each kind
/// (counter, histogram, gauge).
/// </summary>
/// <remarks>
/// The source and the meter are named after the application, which is the name the
/// ServiceDefaults project subscribes to. Titles are left out: they are user input.
/// </remarks>
sealed class TodoTelemetry
{
    const string Name = "AspireShowcase.Api";

    static readonly ActivitySource Source = new(Name);

    readonly Counter<long> _changes;
    readonly Counter<long> _listReads;
    readonly Histogram<double> _completionTime;

    // What the gauge reports: the list as of its last read from the database.
    ItemCounts? _items;

    public TodoTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        // Counters only go up; the tag splits them into series.
        _changes = meter.CreateCounter<long>(
            "todos.changes", "{todo}", "To-do items created, updated and deleted.");
        _listReads = meter.CreateCounter<long>(
            "todos.list.reads", "{read}", "Reads of the to-do list, by whether the cache answered.");

        // A histogram keeps the distribution, so percentiles can be read from it. The default
        // buckets are meant for milliseconds; these fit seconds to days.
        _completionTime = meter.CreateHistogram(
            "todos.completion.time", "s", "Time from creating a to-do item to ticking it off.",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [1, 5, 15, 60, 300, 900, 3600, 14400, 86400],
            });

        // A gauge is a current value, asked for each time metrics are exported.
        meter.CreateObservableGauge(
            "todos.items",
            () => _items is { } items
                ? [
                    new Measurement<long>(items.Open, new KeyValuePair<string, object?>("state", "open")),
                    new Measurement<long>(items.Done, new KeyValuePair<string, object?>("state", "done")),
                ]
                : Array.Empty<Measurement<long>>(),
            "{todo}", "Items in the to-do list when it was last read from the database.");
    }

    /// <summary>Starts a span for one to-do operation, under the request's span.</summary>
    public Activity? StartActivity(string name) => Source.StartActivity(name);

    public void ListRead(Activity? activity, bool cacheHit, IReadOnlyCollection<Todo> list)
    {
        _listReads.Add(1, new KeyValuePair<string, object?>("result", cacheHit ? "hit" : "miss"));
        activity?.SetTag("todos.count", list.Count);
        activity?.AddEvent(new ActivityEvent(cacheHit ? "cache.hit" : "cache.miss"));

        if (!cacheHit)
        {
            var done = list.Count(todo => todo.IsDone);
            _items = new ItemCounts(list.Count - done, done);
        }
    }

    /// <param name="change">created, updated or deleted.</param>
    public void Changed(Activity? activity, string change, int id)
    {
        _changes.Add(1, new KeyValuePair<string, object?>("change", change));
        activity?.SetTag("todo.id", id);
        activity?.AddEvent(new ActivityEvent($"todo.{change}"));
    }

    public void Completed(Activity? activity, Todo todo)
    {
        var seconds = (DateTime.UtcNow - todo.CreatedAt).TotalSeconds;
        _completionTime.Record(seconds);
        activity?.AddEvent(new ActivityEvent(
            "todo.completed", tags: new ActivityTagsCollection { ["todo.completion.time"] = seconds }));
    }

    public void CacheInvalidated(Activity? activity) =>
        activity?.AddEvent(new ActivityEvent("cache.invalidated"));

    /// <param name="skipped">Redis wasn't tried, because it failed moments ago.</param>
    public void CacheUnavailable(Activity? activity, bool skipped) =>
        activity?.AddEvent(new ActivityEvent(
            "cache.unavailable", tags: new ActivityTagsCollection { ["skipped"] = skipped }));

    public void Rejected(Activity? activity, string reason) =>
        activity?.AddEvent(new ActivityEvent(
            "todo.rejected", tags: new ActivityTagsCollection { ["reason"] = reason }));

    sealed record ItemCounts(long Open, long Done);
}
