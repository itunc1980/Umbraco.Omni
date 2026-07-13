using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Umbraco.Cms.Persistence.EFCore.Migrations;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL;

/// <summary>
/// Provides EF Core migrations for PostgreSQL.
/// </summary>
public class PostgreSQLMigrationProvider : IMigrationProvider
{
    private readonly IDbContextFactory<UmbracoDbContext> _dbContextFactory;

    public PostgreSQLMigrationProvider(IDbContextFactory<UmbracoDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <inheritdoc />
    public string ProviderName => Constants.ProviderNames.PostgreSQL;

    /// <inheritdoc />
    public async Task MigrateAsync(EFCoreMigration migration)
    {
        UmbracoDbContext context = await _dbContextFactory.CreateDbContextAsync();
        await context.MigrateDatabaseAsync(GetMigrationType(migration));
    }

    /// <inheritdoc />
    public async Task MigrateAllAsync()
    {
        UmbracoDbContext context = await _dbContextFactory.CreateDbContextAsync();

        if (context.Database.CurrentTransaction is not null)
        {
            throw new System.InvalidOperationException("Cannot migrate all when a transaction is active.");
        }

        await context.Database.MigrateAsync();
    }

    private static System.Type GetMigrationType(EFCoreMigration migration) =>
        migration switch
        {
            EFCoreMigration.InitialCreate => typeof(Migrations.InitialCreate),
            _ => throw new System.ArgumentOutOfRangeException(nameof(migration), $@"Not expected migration value for PostgreSQL: {migration}")
        };
}
