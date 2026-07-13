using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.DistributedLocking;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;
using Umbraco.Cms.Persistence.EFCore.Migrations;
using Umbraco.Cms.Persistence.EFCore.PostgreSQL.Services;

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL;

/// <summary>
/// Composer for registering PostgreSQL EF Core migration services.
/// </summary>
public class EFCorePostgreSQLComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IMigrationProvider, PostgreSQLMigrationProvider>();
        builder.Services.AddSingleton<IMigrationProviderSetup, PostgreSQLMigrationProviderSetup>();
        builder.Services.AddSingleton<ISqlSyntaxProvider, PostgreSqlSyntaxProvider>();
        builder.Services.AddSingleton<IBulkSqlInsertProvider, PostgreSqlBulkSqlInsertProvider>();
        builder.Services.AddSingleton<IDatabaseProviderMetadata, PostgreSqlDatabaseProviderMetadata>();
        builder.Services.AddSingleton<IDistributedLockingMechanism, PostgreSqlDistributedLockingMechanism>();
    }
}
