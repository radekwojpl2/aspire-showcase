using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

class Todo
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; }
}

class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const int MaxTitleLength = 200;

    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Todo>().Property(todo => todo.Title).HasMaxLength(MaxTitleLength);
}

record CreateTodo(string? Title);

record UpdateTodo(string? Title, bool IsDone);

static class TodoEndpoints
{
    // The whole list is cached under one key, and every change to an item removes it.
    const string ListCacheKey = "todos";

    // A limit on how long a list can be served if a removal is ever missed.
    static readonly DistributedCacheEntryOptions ListCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// CRUD for to-do items, stored in the app's PostgreSQL database. The list is served
    /// from Redis when it's cached there. Each operation runs in its own span and reports
    /// what happened to <see cref="TodoTelemetry"/>.
    /// </summary>
    public static void MapTodos(this IEndpointRouteBuilder api)
    {
        var todos = api.MapGroup("/todos");

        todos.MapGet("/", async (AppDbContext db, IDistributedCache cache, TodoTelemetry telemetry) =>
        {
            using var activity = telemetry.StartActivity("todos.list");

            if (await cache.GetAsync(ListCacheKey) is { } cached)
            {
                var cachedList = JsonSerializer.Deserialize<List<Todo>>(cached)!;
                telemetry.ListRead(activity, cacheHit: true, cachedList);
                return cachedList;
            }

            var list = await db.Todos.AsNoTracking().OrderBy(todo => todo.Id).ToListAsync();
            await cache.SetAsync(ListCacheKey, JsonSerializer.SerializeToUtf8Bytes(list), ListCacheOptions);
            telemetry.ListRead(activity, cacheHit: false, list);
            return list;
        })
        .WithName("GetTodos");

        todos.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Todos.FindAsync(id) is { } todo ? Results.Ok(todo) : Results.NotFound())
        .WithName("GetTodo");

        todos.MapPost("/", async (
            CreateTodo request, AppDbContext db, IDistributedCache cache, TodoTelemetry telemetry) =>
        {
            using var activity = telemetry.StartActivity("todos.create");

            if (ValidateTitle(request.Title) is { } problem)
            {
                telemetry.Rejected(activity, "invalid title");
                return problem;
            }

            var todo = new Todo { Title = request.Title!.Trim(), CreatedAt = DateTime.UtcNow };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            telemetry.Changed(activity, "created", todo.Id);

            await cache.RemoveAsync(ListCacheKey);
            telemetry.CacheInvalidated(activity);
            return Results.CreatedAtRoute("GetTodo", new { id = todo.Id }, todo);
        })
        .WithName("CreateTodo");

        todos.MapPut("/{id:int}", async (
            int id, UpdateTodo request, AppDbContext db, IDistributedCache cache, TodoTelemetry telemetry) =>
        {
            using var activity = telemetry.StartActivity("todos.update");

            if (ValidateTitle(request.Title) is { } problem)
            {
                telemetry.Rejected(activity, "invalid title");
                return problem;
            }

            if (await db.Todos.FindAsync(id) is not { } todo)
            {
                telemetry.Rejected(activity, "not found");
                return Results.NotFound();
            }

            var completed = request.IsDone && !todo.IsDone;
            todo.Title = request.Title!.Trim();
            todo.IsDone = request.IsDone;
            await db.SaveChangesAsync();
            telemetry.Changed(activity, "updated", todo.Id);
            if (completed)
            {
                telemetry.Completed(activity, todo);
            }

            await cache.RemoveAsync(ListCacheKey);
            telemetry.CacheInvalidated(activity);
            return Results.Ok(todo);
        })
        .WithName("UpdateTodo");

        todos.MapDelete("/{id:int}", async (
            int id, AppDbContext db, IDistributedCache cache, TodoTelemetry telemetry) =>
        {
            using var activity = telemetry.StartActivity("todos.delete");

            if (await db.Todos.Where(todo => todo.Id == id).ExecuteDeleteAsync() == 0)
            {
                telemetry.Rejected(activity, "not found");
                return Results.NotFound();
            }

            telemetry.Changed(activity, "deleted", id);

            await cache.RemoveAsync(ListCacheKey);
            telemetry.CacheInvalidated(activity);
            return Results.NoContent();
        })
        .WithName("DeleteTodo");
    }

    static IResult? ValidateTitle(string? title)
    {
        var length = title?.Trim().Length ?? 0;
        return length is 0 or > AppDbContext.MaxTitleLength
            ? Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = [$"Title is required and can be at most {AppDbContext.MaxTitleLength} characters."],
            })
            : null;
    }
}
