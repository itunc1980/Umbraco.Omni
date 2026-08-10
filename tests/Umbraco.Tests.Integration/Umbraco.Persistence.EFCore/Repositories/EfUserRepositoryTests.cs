using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Persistence;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.Querying;
using Umbraco.Cms.Persistence.EFCore;
using Umbraco.Cms.Persistence.EFCore.Repositories;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Umbraco.Cms.Tests.Integration.Umbraco.Persistence.EFCore.Repositories;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Logger = UmbracoTestOptions.Logger.Console)]
public class EfUserRepositoryTests : UmbracoIntegrationTest
{
    private ISqlContext SqlContext => GetRequiredService<ISqlContext>();

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        // Setup same configuration as UmbracoOmniDatabaseTests
        var provider = Environment.GetEnvironmentVariable("TEST_DB_PROVIDER");
        var connectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTIONSTRING");

        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Config["ConnectionStrings:umbracoDbDSN"] = connectionString;
            builder.Config["ConnectionStrings:umbracoDbDSN_ProviderName"] = provider;
        }

        var activeProvider = provider ?? builder.Config["ConnectionStrings:umbracoDbDSN_ProviderName"] ?? "Microsoft.Data.Sqlite";
        
        builder.Services.AddSingleton<global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup>(
            new TestMigrationProviderSetup("Microsoft.Data.Sqlite"));
        builder.Services.AddSingleton<global::Umbraco.Cms.Persistence.EFCore.Migrations.IMigrationProviderSetup>(
            new TestMigrationProviderSetup(activeProvider));

        builder.Services.AddUmbracoFlexibleDataStores(builder.Config);
        
        // Ensure EfUserRepository is explicitly registered for test verification
        builder.Services.AddUnique<IUserRepository, EfUserRepository>(ServiceLifetime.Scoped);
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
            builder.UseSqlite(connectionString ?? string.Empty, x => x.MigrationsAssembly("Umbraco.Cms.Persistence.EFCore.Sqlite"));
        }
    }

    [Test]
    public void UserRepository_Can_Perform_CRUD_Operations()
    {
        var dbContext = GetRequiredService<UmbracoDbContext>();
        dbContext.Database.EnsureCreated();

        var userRepo = GetRequiredService<IUserRepository>();
        var globalSettings = GetRequiredService<IOptions<GlobalSettings>>().Value;

        // 1. Create and Save User
        var user = new User(globalSettings, "John Doe", "john@doe.com", "johndoe", "hashed_password");
        userRepo.Save(user);

        Assert.IsTrue(user.HasIdentity);
        Assert.AreNotEqual(0, user.Id);

        // 2. Retrieve User
        var retrieved = userRepo.Get(user.Id);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(user.Name, retrieved.Name);
        Assert.AreEqual(user.Email, retrieved.Email);
        Assert.AreEqual(user.Username, retrieved.Username);

        // 3. Update User
        retrieved.Name = "John Updated";
        retrieved.Email = "john_updated@doe.com";
        userRepo.Save(retrieved);

        var updated = userRepo.Get(user.Id);
        Assert.IsNotNull(updated);
        Assert.AreEqual("John Updated", updated.Name);
        Assert.AreEqual("john_updated@doe.com", updated.Email);

        // 4. Query Paged results
        var query = new Query<IUser>(SqlContext);
        query.Where(u => u.Username == "johndoe");

        var results = userRepo.GetPagedResultsByQuery(query, 0, 10, out long totalRecords, u => u.Name, Direction.Ascending);
        Assert.AreEqual(1, totalRecords);
        Assert.AreEqual("John Updated", results.First().Name);

        // 5. Check User States
        var states = userRepo.GetUserStates();
        Assert.IsTrue(states.ContainsKey(UserState.All));
        Assert.IsTrue(states[UserState.All] >= 1);

        // 6. Delete User
        userRepo.Delete(updated);
        var deleted = userRepo.Get(user.Id);
        Assert.IsNull(deleted);
    }

    [Test]
    public void UserRepository_Can_Manage_Sessions()
    {
        var dbContext = GetRequiredService<UmbracoDbContext>();
        dbContext.Database.EnsureCreated();

        var userRepo = GetRequiredService<IUserRepository>();
        var globalSettings = GetRequiredService<IOptions<GlobalSettings>>().Value;

        // Create user
        var user = new User(globalSettings, "Session Tester", "session@test.com", "sessiontester", "password");
        userRepo.Save(user);

        // Create login session
        var sessionId = userRepo.CreateLoginSession(user.Id, "127.0.0.1");
        Assert.AreNotEqual(Guid.Empty, sessionId);

        // Validate session
        var isValid = userRepo.ValidateLoginSession(user.Id, sessionId);
        Assert.IsTrue(isValid);

        // Clear session
        userRepo.ClearLoginSession(sessionId);
        var isStillValid = userRepo.ValidateLoginSession(user.Id, sessionId);
        Assert.IsFalse(isStillValid);
    }
}
