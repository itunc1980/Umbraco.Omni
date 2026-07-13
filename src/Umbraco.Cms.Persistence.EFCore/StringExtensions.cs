using Umbraco.Cms.Core;

namespace Umbraco.Cms.Persistence.EFCore;

/// <summary>
/// Provides extension methods for string operations related to EF Core persistence.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Compares two database provider names, handling case variations for SQLite provider names.
    /// </summary>
    /// <param name="connectionProvider">The connection provider name to compare.</param>
    /// <param name="compareString">The string to compare against.</param>
    /// <returns><c>true</c> if the provider names match; otherwise, <c>false</c>.</returns>
    internal static bool CompareProviderNames(this string connectionProvider, string? compareString)
    {
        if (connectionProvider is null || compareString is null)
        {
            return false;
        }

        if (string.Equals(connectionProvider, compareString, System.StringComparison.InvariantCultureIgnoreCase))
        {
            return true;
        }

        if (connectionProvider is "Microsoft.Data.SqlClient" or Constants.ProviderNames.SQLServer && compareString is "Microsoft.Data.SqlClient" or Constants.ProviderNames.SQLServer)
        {
            return true;
        }

        if (connectionProvider is "Microsoft.Data.SQLite" or Constants.ProviderNames.SQLLite && compareString is "Microsoft.Data.SQLite" or Constants.ProviderNames.SQLLite)
        {
            return true;
        }

        return connectionProvider is "PostgreSQL" or "Npgsql" or Constants.ProviderNames.PostgreSQL && compareString is "PostgreSQL" or "Npgsql" or Constants.ProviderNames.PostgreSQL;
    }
}
