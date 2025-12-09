using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Testcontainers.PostgreSql;

namespace dn_ef_graph;

public class UnitTest1 : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder().Build();
    private PooledDbContextFactory<SampleContext> _factory;

    public async ValueTask DisposeAsync()
    {
        await _postgreSqlContainer.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public async ValueTask InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync().ConfigureAwait(false);

        _factory = new PooledDbContextFactory<SampleContext>(
            new DbContextOptionsBuilder<SampleContext>()
                .UseNpgsql(_postgreSqlContainer.GetConnectionString())
                .Options
        );

        using var context = _factory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Test_Add_Caller_And_Calls()
    {
        using var context = _factory.CreateDbContext();

        using var tx = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        var call1 = new PhoneCall() { CallTime = DateTime.UtcNow, PhoneNumber = "123-456-7890" };
        var call2 = new PhoneCall()
        {
            CallTime = DateTime.UtcNow.AddMinutes(-30),
            PhoneNumber = "987-654-3210",
        };

        context.PhoneCalls.Add(call1);
        context.PhoneCalls.Add(call2);

        var caller = new Caller { Name = "John Doe", PhoneCalls = [call1, call2] };

        context.Callers.Add(caller);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Clear changes to get a fresh read.
        context.ChangeTracker.Clear();

        var calls = await context.PhoneCalls.ToListAsync(TestContext.Current.CancellationToken);

        Assert.True(calls[0].Id > 0);
        Assert.True(calls[1].Id > 0);
    }

    [Fact]
    public async Task Test_Add_Call_With_topics()
    {
        using var context = _factory.CreateDbContext();

        using var tx = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        var call1 = new PhoneCall()
        {
            CallTime = DateTime.UtcNow,
            PhoneNumber = "123-456-7890",
            Topics = ["Support", "Billing"],
        };

        context.PhoneCalls.Add(call1);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();

        var calls = await context.PhoneCalls.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(calls);
        Assert.Equal(2, calls[0].Topics.Count);
        Assert.Contains("Support", calls[0].Topics);
        Assert.Contains("Billing", calls[0].Topics);
    }
}

public class SampleContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Caller> Callers => Set<Caller>();
    public DbSet<PhoneCall> PhoneCalls => Set<PhoneCall>();
}

public class PhoneCall
{
    public int Id { get; set; }
    public DateTime CallTime { get; set; }
    public required string PhoneNumber { get; set; }
    public List<string> Topics { get; set; } = [];
}

public class Caller
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<PhoneCall> PhoneCalls { get; set; } = [];
}
