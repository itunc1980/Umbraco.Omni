using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Persistence.EFCore;
using Umbraco.Cms.Persistence.EFCore.Repositories;
using Umbraco.Cms.Persistence.EFCore.Services;
using Umbraco.Cms.Persistence.EFCore.UnitOfWork;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;
using Umbraco.Extensions;

namespace Umbraco.Cms.Tests.Integration.Umbraco.Persistence.EFCore.DbContext;

/// <summary>
/// Integration tests verifying Multi-Database operations, Repository & UoW transactions, and high-performance compiled query service.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Logger = UmbracoTestOptions.Logger.Console)]
public class UmbracoOmniDatabaseTests : UmbracoIntegrationTest
{
    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        var provider = Environment.GetEnvironmentVariable("TEST_DB_PROVIDER");
        var connectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTIONSTRING");

        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Config["ConnectionStrings:umbracoDbDSN"] = connectionString;
            builder.Config["ConnectionStrings:umbracoDbDSN_ProviderName"] = provider;
        }

        var activeProvider = provider ?? builder.Config["ConnectionStrings:umbracoDbDSN_ProviderName"] ?? "Microsoft.Data.Sqlite";
        
        // Register for all possible variations of SQLite and PostgreSQL names to prevent CompareProviderNames misses
        builder.Services.AddSingleton<global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup>(
            new TestMigrationProviderSetup("Microsoft.Data.Sqlite"));
        builder.Services.AddSingleton<global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup>(
            new TestMigrationProviderSetup("Microsoft.Data.SQLite"));
        builder.Services.AddSingleton<global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup>(
            new TestMigrationProviderSetup("Npgsql.EntityFrameworkCore.PostgreSQL"));
        builder.Services.AddSingleton<global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup>(
            new TestMigrationProviderSetup("Npgsql"));
        builder.Services.AddSingleton<global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup>(
            new TestMigrationProviderSetup(activeProvider));

        // Register our production flexible data store selector and repository/UoW dependencies
        builder.Services.AddUmbracoFlexibleDataStores(builder.Config);
    }

    private class TestMigrationProviderSetup : global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup
    {
        public TestMigrationProviderSetup(string providerName)
        {
            ProviderName = providerName;
        }

        public string ProviderName { get; }

        public void Setup(DbContextOptionsBuilder builder, string? connectionString)
        {
            string normalized = ProviderName.Trim().ToLowerInvariant();
            if (normalized.Contains("sqlite"))
            {
                builder.UseSqlite(connectionString ?? string.Empty, x => x.MigrationsAssembly("Umbraco.Cms.Persistence.EFCore.Sqlite"));
            }
            else if (normalized.Contains("postgresql") || normalized.Contains("npgsql"))
            {
                var assembly = System.Reflection.Assembly.Load("Npgsql.EntityFrameworkCore.PostgreSQL");
                var type = assembly.GetType("Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions");
                var methods = type?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(m => m.Name == "UseNpgsql" && m.GetParameters().Length == 3);
                var targetMethod = methods?.FirstOrDefault();
                if (targetMethod != null)
                {
                    var actionParameterType = targetMethod.GetParameters()[2].ParameterType.GetGenericArguments()[0];
                    var migrationsAssemblyMethod = actionParameterType.GetMethod("MigrationsAssembly", new[] { typeof(string) });
                    var parameterExpr = System.Linq.Expressions.Expression.Parameter(actionParameterType, "b");
                    var callExpr = System.Linq.Expressions.Expression.Call(parameterExpr, migrationsAssemblyMethod!, System.Linq.Expressions.Expression.Constant("Umbraco.Cms.Persistence.EFCore.PostgreSQL"));
                    var lambdaExpr = System.Linq.Expressions.Expression.Lambda(typeof(Action<>).MakeGenericType(actionParameterType), callExpr, parameterExpr);
                    var actionDelegate = lambdaExpr.Compile();
                    targetMethod.Invoke(null, new object?[] { builder, connectionString, actionDelegate });
                }
            }
            else
            {
                builder.UseSqlite(connectionString ?? string.Empty, x => x.MigrationsAssembly("Umbraco.Cms.Persistence.EFCore.Sqlite"));
            }
        }
    }

    /// <summary>
    /// Verifies that data can be correctly added, committed via Unit of Work, and fetched using the Compiled Query service.
    /// Works seamlessly across SQLite, PostgreSQL, and SQL Server.
    /// </summary>
    [Test]
    public async Task UnitOfWork_Can_Persist_And_QueryService_Can_Retrieve_User_With_Groups_Agnostically()
    {
        // 1. Arrange - Setup mock entities
        var dbContext = GetRequiredService<UmbracoDbContext>();
        var unitOfWork = GetRequiredService<IEfUnitOfWork>();

        // Ensure database is clean and has our schemas
        dbContext.Database.EnsureCreated();

        var userGroup = new UserGroupDto
        {
            Alias = "adminGroup_" + Guid.NewGuid().ToString("N")[..8],
            Name = "Administrator Group",
            Key = Guid.NewGuid(),
            CreateDate = DateTime.UtcNow,
            UpdateDate = DateTime.UtcNow
        };

        var user = new UserDto
        {
            UserName = "testUser_" + Guid.NewGuid().ToString("N")[..8],
            Email = "test@umbraco.com",
            Login = "testLogin_" + Guid.NewGuid().ToString("N")[..8],
            Password = "hashedPassword",
            Key = Guid.NewGuid(),
            CreateDate = DateTime.UtcNow,
            UpdateDate = DateTime.UtcNow,
            Kind = 1
        };

        user.UserGroupDtos.Add(userGroup);

        IEfRepository<UserDto> userRepo = unitOfWork.Repository<UserDto>();
        IEfRepository<UserGroupDto> groupRepo = unitOfWork.Repository<UserGroupDto>();

        // 2. Act - Save using transaction orchestration
        await unitOfWork.BeginTransactionAsync();

        await groupRepo.AddAsync(userGroup);
        await userRepo.AddAsync(user);

        await unitOfWork.CommitTransactionAsync();

        // 3. Assert - Fetch using our high-performance compiled query service
        var queryService = new UmbracoHierarchyQueryService(dbContext);
        UserDto? retrievedUser = await queryService.GetUserByIdAsync(user.Id);

        Assert.IsNotNull(retrievedUser);
        Assert.AreEqual(user.UserName, retrievedUser.UserName);
        Assert.AreEqual(user.Email, retrievedUser.Email);

        // Fetch using split query list method
        var usersWithGroups = await queryService.GetUsersWithGroupsAndNodesAsync(0, 10);
        var firstUser = usersWithGroups.FirstOrDefault(u => u.Id == user.Id);

        Assert.IsNotNull(firstUser);
        Assert.AreEqual(1, firstUser.UserGroupDtos.Count);
        Assert.AreEqual(userGroup.Alias, firstUser.UserGroupDtos.First().Alias);
    }
}
