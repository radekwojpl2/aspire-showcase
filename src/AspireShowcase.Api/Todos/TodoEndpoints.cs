using Microsoft.EntityFrameworkCore;

record CreateTodo(string? Title);

record UpdateTodo(string? Title, bool IsDone);

static class TodoEndpoints
{
    /// <summary>
    /// CRUD for to-do items, stored in the app's PostgreSQL database. The list is served
    /// from Redis when it's cached there. Each operation runs in its own span and reports
    /// what happened to <see cref="TodoTelemetry"/>.
    /// </summary>
    public static void MapTodos(this IEndpointRouteBuilder api)
    {
        var todos = api.MapGroup("/todos");

        // Failures switched on from the Aspire dashboard; does nothing unless one is on.
        todos.AddEndpointFilter(async (context, next) =>
        {
            await context.HttpContext.RequestServices.GetRequiredService<SimulatedFailures>()
                .ApplyAsync(context.HttpContext);
            return await next(context);
        });

        todos.MapGet("/", async (AppDbContext db, TodoListCache cache, TodoTelemetry telemetry) =>
        {
            using var activity = telemetry.StartActivity("todos.list");

            if (await cache.GetAsync() is { } cachedList)
            {
                telemetry.ListRead(activity, cacheHit: true, cachedList);
                return cachedList;
            }

            var list = await db.Todos.AsNoTracking().OrderBy(todo => todo.Id).ToListAsync();
            await cache.SetAsync(list);
            telemetry.ListRead(activity, cacheHit: false, list);
            return list;
        })
        .WithName("GetTodos");

        todos.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Todos.FindAsync(id) is { } todo ? Results.Ok(todo) : Results.NotFound())
        .WithName("GetTodo");

        todos.MapPost("/", async (
            CreateTodo request, AppDbContext db, TodoListCache cache, TodoTelemetry telemetry) =>
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

            await cache.RemoveAsync();
            return Results.CreatedAtRoute("GetTodo", new { id = todo.Id }, todo);
        })
        .WithName("CreateTodo");

        todos.MapPut("/{id:int}", async (
            int id, UpdateTodo request, AppDbContext db, TodoListCache cache, TodoTelemetry telemetry) =>
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

            await cache.RemoveAsync();
            return Results.Ok(todo);
        })
        .WithName("UpdateTodo");

        todos.MapDelete("/{id:int}", async (
            int id, AppDbContext db, TodoListCache cache, TodoTelemetry telemetry) =>
        {
            using var activity = telemetry.StartActivity("todos.delete");

            if (await db.Todos.Where(todo => todo.Id == id).ExecuteDeleteAsync() == 0)
            {
                telemetry.Rejected(activity, "not found");
                return Results.NotFound();
            }

            telemetry.Changed(activity, "deleted", id);

            await cache.RemoveAsync();
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
