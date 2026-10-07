using AspireShowcase.BusinessSetup.PublicClient;
using Microsoft.EntityFrameworkCore;

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
sealed class BusinessSetupDbContext(DbContextOptions<BusinessSetupDbContext> options) : DbContext(options)
{
    public const string Schema = "business_setup";

    public DbSet<Business> Businesses => Set<Business>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    /// <summary>Provider settings that go with the model, for AddNpgsqlDbContext.</summary>
    public static void Configure(DbContextOptionsBuilder options) =>
        options.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public"));

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
