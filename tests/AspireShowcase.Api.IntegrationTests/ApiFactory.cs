using System.Net.Http.Headers;
using AspireShowcase.Identity;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace AspireShowcase.Api.IntegrationTests;

/// <summary>
/// The API host, as the AppHost runs it, against PostgreSQL in a container. Shared by every test
/// (one container, migrated once by the host's startup) and never reset: each test works with
/// its own users and businesses, which also shows that businesses don't see each other's data.
/// </summary>
/// <remarks>
/// Only what can't run in a container is replaced: Logto (tokens are signed with a test key, and
/// the Management API is a fake), RabbitMQ (MassTransit's in-memory test harness, while the
/// outbox stays in PostgreSQL) and the clock.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// When the tests run: Monday 2 November 2026, 08:00 in <see cref="TestBusiness.TimeZone"/>,
    /// so the free slots are the same on every run.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);

    // The major version the AppHost runs; its image has btree_gist, which Scheduling needs.
    readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public FakeOwnerRoles OwnerRoles { get; } = new();

    /// <summary>Every DbContext the host registers, one per module.</summary>
    public List<Type> DbContextTypes { get; } = [];

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A client calling the API as <paramref name="user"/>, as bff does with their access token.</summary>
    public HttpClient CreateClient(TestUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.For(user));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:app-db", _postgres.GetConnectionString());
        // Never connected to: the test harness replaces the RabbitMQ transport.
        builder.UseSetting("ConnectionStrings:messaging", "amqp://localhost");

        builder.ConfigureTestServices(services =>
        {
            TestTokens.Accept(services);

            services.RemoveAll<IOwnerRoles>();
            services.AddSingleton<IOwnerRoles>(OwnerRoles);
            services.RemoveAll<IUserProfiles>();
            services.AddSingleton<IUserProfiles, FakeUserProfiles>();

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));

            services.AddMassTransitTestHarness(bus => bus.AddConsumer<NotificationsStandIn>());
            // The harness keeps the host from stopping until its test timeout (30 seconds by
            // default) is up. Messages still flow after it; the tests wait on their own.
            services.Configure<TestHarnessOptions>(options => options.TestTimeout = TimeSpan.FromSeconds(5));

            DbContextTypes.AddRange(services
                .Select(service => service.ServiceType)
                .Where(type => type.IsSubclassOf(typeof(DbContext)))
                .Distinct());
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
