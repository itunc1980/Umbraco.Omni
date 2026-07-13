using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using StackExchange.Profiling.Data;
using Umbraco.Cms.Core;
using Umbraco.Cms.Infrastructure.Persistence.FaultHandling;

namespace Umbraco.Extensions;

/// <summary>
/// Contains extension methods for <see cref="System.Data.Common.DbConnection"/> to extend database connection capabilities.
/// </summary>
public static class DbConnectionExtensions
{
    /// <summary>
    /// Determines whether a database connection can be successfully opened using the specified connection string and provider factory.
    /// </summary>
    /// <param name="connectionString">The connection string used to establish the database connection.</param>
    /// <param name="factory">The database provider factory used to create the connection.</param>
    /// <returns>True if the connection is available and can be opened; otherwise, false.</returns>
    public static bool IsConnectionAvailable(string? connectionString, DbProviderFactory? factory)
    {
        DbProviderFactory? resolvedFactory = factory;

        string? connStrLower = connectionString?.ToLowerInvariant();

        if (connStrLower != null)
        {
            bool isPostgreSql = connStrLower.Contains("host=") && connStrLower.Contains("port=") &&
                                (connStrLower.Contains("username=") || connStrLower.Contains("password=") || connStrLower.Contains("user id="));

            bool isSqlite = connStrLower.Contains("data source=") &&
                            (connStrLower.Contains(".db") || connStrLower.Contains(".sqlite"));

            bool isMySql = connStrLower.Contains("port=") && connStrLower.Contains("server=") &&
                           (connStrLower.Contains("uid=") || connStrLower.Contains("user=") || connStrLower.Contains("pwd="));

            bool isOracle = connStrLower.Contains("oracle") || (connStrLower.Contains("user id=") && connStrLower.Contains("data source=") && !isSqlite);

            if (isPostgreSql)
            {
                try
                {
                    var assembly = System.Reflection.Assembly.Load("Npgsql");
                    var type = assembly.GetType("Npgsql.NpgsqlFactory");
                    var instanceField = type?.GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    resolvedFactory = instanceField?.GetValue(null) as DbProviderFactory ?? resolvedFactory;
                }
                catch
                {
                    // Fallback
                }
            }
            else if (isSqlite)
            {
                try
                {
                    var assembly = System.Reflection.Assembly.Load("Microsoft.Data.Sqlite");
                    var type = assembly.GetType("Microsoft.Data.Sqlite.SqliteFactory");
                    var instanceField = type?.GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    resolvedFactory = instanceField?.GetValue(null) as DbProviderFactory ?? resolvedFactory;
                }
                catch
                {
                    // Fallback
                }
            }
            else if (isMySql)
            {
                try
                {
                    System.Reflection.Assembly? assembly = null;
                    try
                    {
                        assembly = System.Reflection.Assembly.Load("MySqlConnector");
                    }
                    catch
                    {
                        assembly = System.Reflection.Assembly.Load("MySql.Data");
                    }
                    var type = assembly?.GetType("MySqlConnector.MySqlConnectorFactory") ?? assembly?.GetType("MySql.Data.MySqlClient.MySqlClientFactory");
                    var instanceField = type?.GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    resolvedFactory = instanceField?.GetValue(null) as DbProviderFactory ?? resolvedFactory;
                }
                catch
                {
                    // Fallback
                }
            }
            else if (isOracle)
            {
                try
                {
                    var assembly = System.Reflection.Assembly.Load("Oracle.ManagedDataAccess");
                    var type = assembly.GetType("Oracle.ManagedDataAccess.Client.OracleClientFactory");
                    var instanceField = type?.GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    resolvedFactory = instanceField?.GetValue(null) as DbProviderFactory ?? resolvedFactory;
                }
                catch
                {
                    // Fallback
                }
            }
        }

        DbConnection? connection = resolvedFactory?.CreateConnection();

        if (connection == null)
        {
            throw new InvalidOperationException($"Could not create a connection for provider \"{resolvedFactory}\".");
        }

        connection.ConnectionString = connectionString;
        using (connection)
        {
            return connection.IsAvailable();
        }
    }

    /// <summary>
    /// Checks whether the specified database connection is available by attempting to open and close it.
    /// </summary>
    /// <param name="connection">The <see cref="IDbConnection"/> to test for availability.</param>
    /// <returns>
    /// <c>true</c> if the connection can be opened and closed without throwing a <see cref="DbException"/>; otherwise, <c>false</c>.
    /// </returns>
    public static bool IsAvailable(this IDbConnection connection)
    {
        try
        {
            connection.Open();
            connection.Close();
        }
        catch (DbException e)
        {
            // Don't swallow this error, the exception is super handy for knowing "why" its not available
            StaticApplicationLogging.Logger.LogWarning(e, "Configured database is reporting as not being available.");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Unwraps a database connection.
    /// </summary>
    /// <remarks>
    ///     UmbracoDatabase wraps the original database connection in various layers (see
    ///     OnConnectionOpened); this unwraps and returns the original database connection.
    /// </remarks>
    internal static IDbConnection UnwrapUmbraco(this IDbConnection connection)
    {
        IDbConnection? unwrapped = connection;

        IDbConnection c;
        do
        {
            c = unwrapped;

            if (unwrapped is ProfiledDbConnection profiled)
            {
                unwrapped = profiled.WrappedConnection;
            }

            if (unwrapped is RetryDbConnection retrying)
            {
                unwrapped = retrying.Inner;
            }
        }
        while (c != unwrapped);

        return unwrapped;
    }
}
