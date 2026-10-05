using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>Each module's migrations, as the host applies them on startup.</summary>
[Collection(ApiCollection.Name)]
public sealed class MigrationTests(ApiFactory api)
{
    [Fact]
    public async Task Every_module_has_a_migration_for_its_whole_model()
    {
        using var scope = api.Services.CreateScope();

        Assert.NotEmpty(api.DbContextTypes);
        foreach (var type in api.DbContextTypes)
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(type);
            // A model change committed without its migration (dotnet ef migrations add) fails here.
            Assert.False(db.Database.HasPendingModelChanges(), $"{type.Name} has changes no migration covers.");
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
    }
}
