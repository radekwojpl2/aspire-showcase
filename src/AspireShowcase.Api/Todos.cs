using Microsoft.EntityFrameworkCore;

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
    /// <summary>CRUD for to-do items, stored in the app's PostgreSQL database.</summary>
    public static void MapTodos(this IEndpointRouteBuilder api)
    {
        var todos = api.MapGroup("/todos");

        todos.MapGet("/", async (AppDbContext db) =>
            await db.Todos.AsNoTracking().OrderBy(todo => todo.Id).ToListAsync())
        .WithName("GetTodos");

        todos.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Todos.FindAsync(id) is { } todo ? Results.Ok(todo) : Results.NotFound())
        .WithName("GetTodo");

        todos.MapPost("/", async (CreateTodo request, AppDbContext db) =>
        {
            if (ValidateTitle(request.Title) is { } problem)
            {
                return problem;
            }

            var todo = new Todo { Title = request.Title!.Trim(), CreatedAt = DateTime.UtcNow };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return Results.CreatedAtRoute("GetTodo", new { id = todo.Id }, todo);
        })
        .WithName("CreateTodo");

        todos.MapPut("/{id:int}", async (int id, UpdateTodo request, AppDbContext db) =>
        {
            if (ValidateTitle(request.Title) is { } problem)
            {
                return problem;
            }

            if (await db.Todos.FindAsync(id) is not { } todo)
            {
                return Results.NotFound();
            }

            todo.Title = request.Title!.Trim();
            todo.IsDone = request.IsDone;
            await db.SaveChangesAsync();
            return Results.Ok(todo);
        })
        .WithName("UpdateTodo");

        todos.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
            await db.Todos.Where(todo => todo.Id == id).ExecuteDeleteAsync() == 0
                ? Results.NotFound()
                : Results.NoContent())
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
