using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Messenger.Tests;

/// <summary>
/// Spins up a throwaway PostgreSQL container once for the whole test collection and applies
/// the real EF Core migrations — the same schema <c>Program.cs</c> creates on startup.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("ulak_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public MessengerDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<MessengerDbContext>().UseNpgsql(ConnectionString).Options);
}

[CollectionDefinition("ulak-db")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
