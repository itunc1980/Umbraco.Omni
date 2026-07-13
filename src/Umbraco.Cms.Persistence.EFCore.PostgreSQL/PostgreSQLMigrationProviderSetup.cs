using Microsoft.EntityFrameworkCore;
using Umbraco.Cms.Persistence.EFCore.Migrations;

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL;

/// <summary>
/// Configures the EF Core DbContext to use PostgreSQL as the database provider.
/// </summary>
public class PostgreSQLMigrationProviderSetup : IMigrationProviderSetup
{
    /// <inheritdoc />
    public string ProviderName => Constants.ProviderNames.PostgreSQL;

    /// <inheritdoc />
    public void Setup(DbContextOptionsBuilder builder, string? connectionString)
    {
        builder.UseNpgsql(connectionString, x =>
        {
            x.MigrationsAssembly(GetType().Assembly.FullName);
        });
    }
}
