using AspireShowcase.BuildingBlocks.Infrastructure;
using AspireShowcase.BusinessSetup.Application;
using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AspireShowcase.BusinessSetup;

/// <summary>
/// Business Setup's own view of app-db: only its tables, all in the business_setup schema.
/// Other modules get their own context and schema, so none of them can reach into another's tables.
/// </summary>
/// <remarks>
/// Its migration history stays in public.__EFMigrationsHistory (see <see cref="Configure"/>):
/// the migrations were the API's before modules became projects, and existing databases have them
/// recorded there. A new module should keep its history table in its own schema.
/// </remarks>
sealed class BusinessSetupDbContext(DbContextOptions<BusinessSetupDbContext> options) : DbContext(options), IBusinessSetupDbContext
{
    public const string Schema = "business_setup";

    // Which unique rule each unique index enforces; see the entity configurations.
    static readonly Dictionary<string, UniqueRule> UniqueIndexes = new()
    {
        [BusinessConfiguration.SlugIndex] = UniqueRule.Slug,
        [BusinessConfiguration.OwnerIndex] = UniqueRule.Owner,
        [ServiceConfiguration.NameIndex] = UniqueRule.ServiceName,
        [StaffMemberConfiguration.NameIndex] = UniqueRule.StaffName,
    };

    public DbSet<Business> Businesses => Set<Business>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    /// <summary>Provider settings that go with the model, for AddNpgsqlDbContext.</summary>
    public static void Configure(DbContextOptionsBuilder options) =>
        options.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public"));

    /// <summary>Saves, turning a broken unique index into the rule it enforces, for the use cases.</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique &&
            unique.ConstraintName is { } index && UniqueIndexes.TryGetValue(index, out var rule))
        {
            throw new AlreadyExistsException(rule, exception);
        }
    }

    // The execution strategy retries transient database failures, which needs the whole
    // transaction inside it; each try starts from no changes.
    public Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellation) =>
        Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            ChangeTracker.Clear();
            await using var transaction = await Database.BeginTransactionAsync(cancellation);
            await work(cancellation);
            await transaction.CommitAsync(cancellation);
        });

    // Typed IDs are stored as the uuid they wrap, wherever they appear.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<BusinessId>().HaveConversion<TypedIdConverter<BusinessId>>();
        configurationBuilder.Properties<ServiceId>().HaveConversion<TypedIdConverter<ServiceId>>();
        configurationBuilder.Properties<StaffMemberId>().HaveConversion<TypedIdConverter<StaffMemberId>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BusinessSetupDbContext).Assembly);
    }
}
