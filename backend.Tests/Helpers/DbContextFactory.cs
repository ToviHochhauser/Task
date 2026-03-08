using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using backend.Data;

namespace backend.Tests.Helpers;

/// <summary>
/// Creates isolated AppDbContext instances backed by the EF Core InMemory provider.
/// Each call with a unique <paramref name="dbName"/> gives a completely separate database,
/// which prevents test-to-test data bleed.
/// </summary>
public static class DbContextFactory
{
    /// <summary>
    /// Creates a fresh in-memory AppDbContext.
    /// <para>
    /// Pass a fixed <paramref name="dbName"/> when you need multiple context instances
    /// to share the same data within a single test (e.g., Arrange via one context,
    /// Assert via another). Omit for full per-test isolation.
    /// </para>
    /// </summary>
    public static AppDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
