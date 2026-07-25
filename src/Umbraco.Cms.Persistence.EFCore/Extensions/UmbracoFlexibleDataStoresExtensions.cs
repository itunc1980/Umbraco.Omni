using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Persistence.EFCore;
using Umbraco.Cms.Persistence.EFCore.Repositories;
using Umbraco.Cms.Persistence.EFCore.UnitOfWork;

namespace Umbraco.Extensions;

/// <summary>
/// Extension methods for configuring flexible RDBMS database stores using EF Core in Umbraco.
/// </summary>
public static class UmbracoFlexibleDataStoresExtensions
{
    /// <summary>
    /// Configures the dynamic EF Core Database Selection Layer based on configuration.
    /// Supports PostgreSQL, MSSQL, MySQL, Oracle, and SQLite.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The updated service collection.</returns>
    /// <exception cref="NotSupportedException">Thrown when the configured database provider is not supported.</exception>
    public static IServiceCollection AddUmbracoFlexibleDataStores(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Read and validate connection string provider early to fail fast on application start
        var connectionStringsSection = configuration.GetSection("ConnectionStrings");
        var providerName = connectionStringsSection["umbracoDbDSN_ProviderName"]
                           ?? connectionStringsSection["ProviderName"];

        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = ConnectionStrings.DefaultProviderName; // Default is Microsoft.Data.SqlClient (MSSQL)
        }

        // Validate early
        ValidateProviderName(providerName);

        // 2. Register UmbracoDbContext within Umbraco's scoping infrastructure
        services.AddUmbracoDbContext<UmbracoDbContext>(
            (options, connString, resolvedProvider, sp) =>
            {
                // Suppress pending model changes warnings for dynamic multi-provider compatibility
                options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

                // Register the entity sets needed by OpenIddict
                options.UseOpenIddict();

                var finalProvider = resolvedProvider ?? providerName ?? ConnectionStrings.DefaultProviderName;
                var finalConnString = connString;

                if (string.IsNullOrWhiteSpace(finalConnString))
                {
                    // If resolved connection string is empty at runtime, we cannot configure options.
                    return;
                }

                ConfigureProvider(options, finalProvider, finalConnString);
            },
            shareUmbracoConnection: true);

        // 3. Register generic Repository and Unit of Work patterns
        services.AddScoped(typeof(IEfRepository<>), typeof(EfRepository<>));
        services.AddScoped<IEfUnitOfWork, EfUnitOfWork>();

        return services;
    }

    private static void ValidateProviderName(string providerName)
    {
        string normalized = providerName.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "postgresql":
            case "npgsql":
            case "npgsql.entityframeworkcore.postgresql":
            case "mssql":
            case "sqlserver":
            case "microsoft.data.sqlclient":
            case "system.data.sqlclient":
            case "mysql":
            case "pomelo":
            case "pomelo.entityframeworkcore.mysql":
            case "oracle":
            case "oracle.entityframeworkcore":
            case "sqlite":
            case "microsoft.data.sqlite":
                break;
            default:
                throw new NotSupportedException(
                    $"The configured database provider '{providerName}' is not supported. " +
                    "Supported providers are: PostgreSQL, MSSQL, MySQL, Oracle, SQLite.");
        }
    }

    private static void ConfigureProvider(DbContextOptionsBuilder options, string providerName, string connectionString)
    {
        string normalized = providerName.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "mssql":
            case "sqlserver":
            case "microsoft.data.sqlclient":
            case "system.data.sqlclient":
                options.UseSqlServer(connectionString, x => x.MigrationsAssembly("Umbraco.Cms.Persistence.EFCore.SqlServer"));
                break;

            case "sqlite":
            case "microsoft.data.sqlite":
                options.UseSqlite(connectionString, x => x.MigrationsAssembly("Umbraco.Cms.Persistence.EFCore.Sqlite"));
                break;

            case "postgresql":
            case "npgsql":
            case "npgsql.entityframeworkcore.postgresql":
                InvokeUseMethod(
                    options,
                    "Npgsql.EntityFrameworkCore.PostgreSQL",
                    "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions",
                    "UseNpgsql",
                    connectionString,
                    "Umbraco.Cms.Persistence.EFCore.PostgreSQL");
                break;

            case "mysql":
            case "pomelo":
            case "pomelo.entityframeworkcore.mysql":
                InvokeMySqlMethod(options, connectionString, "Umbraco.Cms.Persistence.EFCore.MySQL");
                break;

            case "oracle":
            case "oracle.entityframeworkcore":
                InvokeUseMethod(
                    options,
                    "Oracle.EntityFrameworkCore",
                    "Microsoft.EntityFrameworkCore.OracleDbContextOptionsBuilderExtensions",
                    "UseOracle",
                    connectionString,
                    "Umbraco.Cms.Persistence.EFCore.Oracle");
                break;

            default:
                throw new NotSupportedException(
                    $"The database provider '{providerName}' is not supported. " +
                    "Supported providers are: PostgreSQL, MSSQL, MySQL, Oracle, SQLite.");
        }
    }

    private static void InvokeUseMethod(DbContextOptionsBuilder options, string assemblyName, string className, string methodName, string connectionString, string migrationsAssembly)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            var type = assembly.GetType(className);
            if (type == null)
            {
                throw new TypeLoadException($"Could not load type '{className}' from assembly '{assemblyName}'.");
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == methodName && m.GetParameters().Length == 3);

            MethodInfo? targetMethod = null;
            Type? actionParameterType = null;

            foreach (var m in methods)
            {
                var parameters = m.GetParameters();
                if (parameters[0].ParameterType == typeof(DbContextOptionsBuilder) &&
                    parameters[1].ParameterType == typeof(string) &&
                    parameters[2].ParameterType.IsGenericType &&
                    parameters[2].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
                {
                    targetMethod = m;
                    actionParameterType = parameters[2].ParameterType.GetGenericArguments()[0];
                    break;
                }
            }

            if (targetMethod == null || actionParameterType == null)
            {
                var simpleMethod = type.GetMethod(methodName, new[] { typeof(DbContextOptionsBuilder), typeof(string) });
                if (simpleMethod == null)
                {
                    throw new MissingMethodException($"Could not find method '{methodName}' on type '{className}'.");
                }

                simpleMethod.Invoke(null, new object?[] { options, connectionString });
                return;
            }

            var migrationsAssemblyMethod = actionParameterType.GetMethod("MigrationsAssembly", new[] { typeof(string) });
            if (migrationsAssemblyMethod == null)
            {
                throw new MissingMethodException($"Could not find MigrationsAssembly method on {actionParameterType.FullName}.");
            }

            var parameterExpr = System.Linq.Expressions.Expression.Parameter(actionParameterType, "builder");
            var callExpr = System.Linq.Expressions.Expression.Call(parameterExpr, migrationsAssemblyMethod, System.Linq.Expressions.Expression.Constant(migrationsAssembly));
            var lambdaExpr = System.Linq.Expressions.Expression.Lambda(typeof(Action<>).MakeGenericType(actionParameterType), callExpr, parameterExpr);
            var actionDelegate = lambdaExpr.Compile();

            targetMethod.Invoke(null, new object?[] { options, connectionString, actionDelegate });
        }
        catch (Exception ex)
        {
            throw new NotSupportedException(
                $"Failed to load and configure the EF Core provider for {assemblyName}. Ensure that the '{assemblyName}' NuGet package is installed in your startup project.",
                ex);
        }
    }

    private static void InvokeMySqlMethod(DbContextOptionsBuilder options, string connectionString, string migrationsAssembly)
    {
        try
        {
            var assembly = Assembly.Load("Pomelo.EntityFrameworkCore.MySql");
            var serverVersionType = assembly.GetType("Microsoft.EntityFrameworkCore.ServerVersion");
            if (serverVersionType == null)
            {
                throw new TypeLoadException("Could not load Microsoft.EntityFrameworkCore.ServerVersion from Pomelo assembly.");
            }

            var autoDetectMethod = serverVersionType.GetMethod("AutoDetect", new[] { typeof(string) });
            if (autoDetectMethod == null)
            {
                throw new MissingMethodException("Could not find ServerVersion.AutoDetect method.");
            }

            var serverVersion = autoDetectMethod.Invoke(null, new object[] { connectionString });

            var extensionType = assembly.GetType("Microsoft.EntityFrameworkCore.MySqlDbContextOptionsBuilderExtensions");
            if (extensionType == null)
            {
                throw new TypeLoadException("Could not load MySqlDbContextOptionsBuilderExtensions from Pomelo assembly.");
            }

            var methods = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "UseMySql" && m.GetParameters().Length == 4);

            MethodInfo? targetMethod = null;
            Type? actionParameterType = null;

            foreach (var m in methods)
            {
                var parameters = m.GetParameters();
                if (parameters[0].ParameterType == typeof(DbContextOptionsBuilder) &&
                    parameters[1].ParameterType == typeof(string) &&
                    parameters[2].ParameterType == serverVersionType &&
                    parameters[3].ParameterType.IsGenericType &&
                    parameters[3].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
                {
                    targetMethod = m;
                    actionParameterType = parameters[3].ParameterType.GetGenericArguments()[0];
                    break;
                }
            }

            if (targetMethod == null || actionParameterType == null)
            {
                var simpleMethod = extensionType.GetMethod("UseMySql", new[] { typeof(DbContextOptionsBuilder), typeof(string), serverVersionType });
                if (simpleMethod == null)
                {
                    throw new MissingMethodException("Could not find UseMySql method on Pomelo extension.");
                }

                simpleMethod.Invoke(null, new object?[] { options, connectionString, serverVersion });
                return;
            }

            var migrationsAssemblyMethod = actionParameterType.GetMethod("MigrationsAssembly", new[] { typeof(string) });
            if (migrationsAssemblyMethod == null)
            {
                throw new MissingMethodException($"Could not find MigrationsAssembly method on {actionParameterType.FullName}.");
            }

            var parameterExpr = System.Linq.Expressions.Expression.Parameter(actionParameterType, "builder");
            var callExpr = System.Linq.Expressions.Expression.Call(parameterExpr, migrationsAssemblyMethod, System.Linq.Expressions.Expression.Constant(migrationsAssembly));
            var lambdaExpr = System.Linq.Expressions.Expression.Lambda(typeof(Action<>).MakeGenericType(actionParameterType), callExpr, parameterExpr);
            var actionDelegate = lambdaExpr.Compile();

            targetMethod.Invoke(null, new object?[] { options, connectionString, serverVersion, actionDelegate });
        }
        catch (Exception ex)
        {
            throw new NotSupportedException(
                "Failed to load and configure the Pomelo MySQL provider. Ensure that the 'Pomelo.EntityFrameworkCore.MySql' NuGet package is installed in your startup project.",
                ex);
        }
    }
}
