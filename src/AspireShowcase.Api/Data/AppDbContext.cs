using Microsoft.EntityFrameworkCore;

class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const int MaxTitleLength = 200;

    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Todo>().Property(todo => todo.Title).HasMaxLength(MaxTitleLength);
}
