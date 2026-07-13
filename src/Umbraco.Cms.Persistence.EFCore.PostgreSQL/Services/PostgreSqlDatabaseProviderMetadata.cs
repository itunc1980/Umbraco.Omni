using System.Runtime.Serialization;
using Npgsql;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Install.Models;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL.Services;

/// <summary>
/// Provides metadata for the PostgreSQL database provider.
/// </summary>
[DataContract]
public class PostgreSqlDatabaseProviderMetadata : IDatabaseProviderMetadata
{
    /// <inheritdoc />
    public Guid Id => new("5b1cdb31-c423-45a8-9d2a-8c54c37e1bfa");

    /// <inheritdoc />
    public int SortOrder => 3;

    /// <inheritdoc />
    public string DisplayName => "PostgreSQL (via EF Core)";

    /// <inheritdoc />
    public string DefaultDatabaseName => Core.Constants.System.UmbracoDefaultDatabaseName;

    /// <inheritdoc />
    public string ProviderName => "PostgreSQL";

    /// <inheritdoc />
    public bool SupportsQuickInstall => false;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool RequiresServer => true;

    /// <inheritdoc />
    public string? ServerPlaceholder => "localhost";

    /// <inheritdoc />
    public bool RequiresCredentials => true;

    /// <inheritdoc />
    public bool SupportsIntegratedAuthentication => false;

    /// <inheritdoc />
    public bool SupportsTrustServerCertificate => true;

    /// <inheritdoc />
    public bool RequiresConnectionTest => true;

    /// <inheritdoc />
    public bool ForceCreateDatabase => false;

    /// <inheritdoc />
    public bool CanRecognizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrEmpty(builder.Host) && !string.IsNullOrEmpty(builder.Database);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string GenerateConnectionString(DatabaseModel databaseModel)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = databaseModel.Server,
            Database = databaseModel.DatabaseName,
            Username = databaseModel.Login,
            Password = databaseModel.Password
        };

        return builder.ConnectionString;
    }
}
